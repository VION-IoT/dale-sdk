using System;
using System.Collections.Immutable;
using System.Reflection;
using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Introspection
{
    /// <summary>
    ///     Routes Dale source attributes into the three sibling per-property metadata documents:
    ///     <c>Schema</c> (data shape + JSON Schema annotations), <c>Presentation</c> (UI hints),
    ///     and <c>Runtime</c> (Dale-runtime behaviour flags).
    ///     Used by <see cref="LogicBlockIntrospection" /> when emitting introspection JSON.
    /// </summary>
    internal static class PropertyMetadataBuilder
    {
        /// <summary>
        ///     Builds a typed <see cref="PropertyMetadata" /> document for the given property.
        ///     The <paramref name="typeRef" /> is supplied by the caller (built from the property's CLR type
        ///     by the introspection pipeline).
        ///     The <paramref name="structFieldAnnotations" /> map carries per-struct-field
        ///     <c>[StructField]</c> data when the property is a struct or array-of-struct;
        ///     pass <see cref="ImmutableDictionary{TKey,TValue}.Empty" /> when not applicable.
        /// </summary>
        public static PropertyMetadata Build(PropertyInfo property, TypeRef typeRef, ImmutableDictionary<string, TypeAnnotations> structFieldAnnotations)
        {
            var sp = property.GetCustomAttribute<ServicePropertyAttribute>();
            var mp = property.GetCustomAttribute<ServiceMeasuringPointAttribute>();

            var annotations = ExtractTypeAnnotations(sp, mp, HasPublicSetter(property));
            var schema = new TypeSchema(typeRef, annotations, structFieldAnnotations);
            var presentation = ExtractPresentation(property);
            var runtime = ExtractRuntime(property);

            return new PropertyMetadata(schema, presentation, runtime);
        }

        /// <summary>
        ///     Builds a typed <see cref="PropertyMetadata" /> document with split sources:
        ///     <paramref name="schemaSource" /> supplies the schema-bearing attributes
        ///     (<c>[ServiceProperty]</c> / <c>[ServiceMeasuringPoint]</c>), while
        ///     <paramref name="presentationSource" /> supplies the UI-hint and runtime attributes
        ///     (<c>[Display]</c>, <c>[Category]</c>, <c>[Importance]</c>, <c>[UIHint]</c>,
        ///     <c>[StatusIndicator]</c>, <c>[Persistent]</c>).
        ///     Used for interface-bound properties where the interface owns the schema contract and
        ///     the implementing logic-block property owns the UI hints.
        /// </summary>
        public static PropertyMetadata BuildSplit(PropertyInfo schemaSource,
                                                  PropertyInfo presentationSource,
                                                  TypeRef typeRef,
                                                  ImmutableDictionary<string, TypeAnnotations> structFieldAnnotations)
        {
            var sp = schemaSource.GetCustomAttribute<ServicePropertyAttribute>();
            var mp = schemaSource.GetCustomAttribute<ServiceMeasuringPointAttribute>();

            // Writability is governed by the implementing logic-block property — that's the actual
            // binding target when cloud calls SetPropertyValue. The interface only declares intent.
            var annotations = ExtractTypeAnnotations(sp, mp, HasPublicSetter(presentationSource));
            var schema = new TypeSchema(typeRef, annotations, structFieldAnnotations);
            var presentation = ExtractPresentation(presentationSource);
            var runtime = ExtractRuntime(presentationSource);

            return new PropertyMetadata(schema, presentation, runtime);
        }

        private static bool HasPublicSetter(PropertyInfo property) =>
            property.SetMethod is not null && property.SetMethod.IsPublic;

        private static TypeAnnotations ExtractTypeAnnotations(ServicePropertyAttribute? sp, ServiceMeasuringPointAttribute? mp, bool hasPublicSetter)
        {
            // Title / Unit: prefer ServiceProperty's value if both are present (which would be unusual).
            var title = sp?.Title ?? mp?.Title;
            var unit = sp?.Unit ?? mp?.Unit;

            // Minimum / Maximum: NegativeInfinity / PositiveInfinity are the sentinel "absent" values.
            // Convert finite values to nullable-bearing fields; leave null otherwise.
            double? minimum = null;
            if (sp is not null && !double.IsNegativeInfinity(sp.Minimum))
            {
                minimum = sp.Minimum;
            }
            else if (mp is not null && !double.IsNegativeInfinity(mp.Minimum))
            {
                minimum = mp.Minimum;
            }

            double? maximum = null;
            if (sp is not null && !double.IsPositiveInfinity(sp.Maximum))
            {
                maximum = sp.Maximum;
            }
            else if (mp is not null && !double.IsPositiveInfinity(mp.Maximum))
            {
                maximum = mp.Maximum;
            }

            // ReadOnly: a measuring point alone (without a service-property attribute) marks the property as read-only.
            // Also: any property without a public setter is read-only on the wire — matches the legacy `Writable` rule
            // (e.g. `[ServiceProperty] public int Foo { get; private set; }` exposes a metric the gateway publishes
            // but the cloud cannot write back to).
            var readOnly = (mp is not null && sp is null) || !hasPublicSetter;

            return new TypeAnnotations
                   {
                       Title = title,
                       Unit = unit,
                       Minimum = minimum,
                       Maximum = maximum,
                       ReadOnly = readOnly,
                   };
        }

        private static Presentation ExtractPresentation(PropertyInfo property)
        {
            var display = property.GetCustomAttribute<DisplayAttribute>();
            var category = property.GetCustomAttribute<CategoryAttribute>();
            var importance = property.GetCustomAttribute<ImportanceAttribute>();
            var uiHint = property.GetCustomAttribute<UIHintAttribute>();
            var statusIndicator = property.GetCustomAttribute<StatusIndicatorAttribute>();

            // DisplayAttribute.Order uses -1 as the absent-sentinel. Map only non-negative values.
            int? order = null;
            if (display is not null && display.Order >= 0)
            {
                order = display.Order;
            }

            var statusMappings = ExtractStatusMappings(property, statusIndicator);

            var presentation = new Presentation
                               {
                                   DisplayName = display?.Name,
                                   Group = display?.Group,
                                   Order = order,
                                   Category = category?.Category.ToString(),
                                   Importance = importance?.Importance.ToString(),
                                   UIHint = uiHint?.Widget,
                                   StatusMappings = statusMappings,
                               };

            // If everything is null/empty, return the canonical None instance for cheap equality.
            return presentation.IsEmpty ? Presentation.None : presentation;
        }

        private static ImmutableDictionary<string, string>? ExtractStatusMappings(PropertyInfo property, StatusIndicatorAttribute? statusAttr)
        {
            if (statusAttr is null)
            {
                return null;
            }

            // Only meaningful on (nullable-)enum-typed properties; silently ignore otherwise (DALE006 already warns).
            var enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!enumType.IsEnum)
            {
                return null;
            }

            var builder = ImmutableDictionary.CreateBuilder<string, string>();
            foreach (var name in Enum.GetNames(enumType))
            {
                var memberInfo = enumType.GetField(name);
                var severity = memberInfo?.GetCustomAttribute<StatusSeverityAttribute>();
                if (severity is not null)
                {
                    builder[name] = severity.Severity.ToString().ToLowerInvariant();
                }
            }

            return builder.Count > 0 ? builder.ToImmutable() : null;
        }

        private static RuntimeMetadata ExtractRuntime(PropertyInfo property)
        {
            // Persistent: presence of [Persistent] without Exclude=true => Persistent=true in output.
            // [Persistent(Exclude = true)] records as Persistent=false (treat opt-out as not persistent).
            var persistentAttr = property.GetCustomAttribute<PersistentAttribute>();
            var persistent = persistentAttr is not null && !persistentAttr.Exclude;

            var runtime = new RuntimeMetadata { Persistent = persistent };
            return runtime.IsEmpty ? RuntimeMetadata.None : runtime;
        }
    }
}