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
            var hasIdentityTitle = HasIdentityBearingTitle(typeRef);

            var annotations = ExtractTypeAnnotations(sp, mp, HasPublicSetter(property), hasIdentityTitle);
            var schema = new TypeSchema(typeRef, annotations, structFieldAnnotations);
            var presentation = ExtractPresentation(property, sp, mp, hasIdentityTitle);
            var runtime = ExtractRuntime(property);

            return new PropertyMetadata(schema, presentation, runtime);
        }

        /// <summary>
        ///     Builds a typed <see cref="PropertyMetadata" /> document with split sources:
        ///     <paramref name="schemaSource" /> supplies the schema-bearing attributes
        ///     (<c>[ServiceProperty]</c> / <c>[ServiceMeasuringPoint]</c>), while
        ///     <paramref name="presentationSource" /> supplies the UI-hint and runtime attributes
        ///     (<c>[Presentation]</c>, <c>[Persistent]</c>).
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
            var hasIdentityTitle = HasIdentityBearingTitle(typeRef);

            // Writability is governed by the implementing logic-block property — that's the actual
            // binding target when cloud calls SetPropertyValue. The interface only declares intent.
            var annotations = ExtractTypeAnnotations(sp, mp, HasPublicSetter(presentationSource), hasIdentityTitle);
            var schema = new TypeSchema(typeRef, annotations, structFieldAnnotations);

            // Per-field presentation merge: the class wins on any field it explicitly sets, and
            // inherits from the interface on fields it leaves null. This lets interfaces declare
            // shared UI semantics (Group, Importance) while classes override per-instance details
            // (DisplayName, Order).
            var interfacePresentation = ExtractPresentation(schemaSource, sp, mp, hasIdentityTitle);
            var classPresentation = ExtractPresentation(presentationSource, sp, mp, hasIdentityTitle);
            var presentation = MergePresentation(classPresentation, interfacePresentation);

            var runtime = ExtractRuntime(presentationSource);

            return new PropertyMetadata(schema, presentation, runtime);
        }

        /// <summary>
        ///     Per-field merge: class values win; class-null fields inherit from interface.
        /// </summary>
        private static Presentation MergePresentation(Presentation classP, Presentation interfaceP)
        {
            if (interfaceP.IsEmpty)
            {
                return classP;
            }

            if (classP.IsEmpty)
            {
                return interfaceP;
            }

            var merged = new Presentation
                         {
                             DisplayName = classP.DisplayName ?? interfaceP.DisplayName,
                             Group = classP.Group ?? interfaceP.Group,
                             Order = classP.Order ?? interfaceP.Order,
                             Category = classP.Category ?? interfaceP.Category,
                             Importance = classP.Importance ?? interfaceP.Importance,
                             UIHint = classP.UIHint ?? interfaceP.UIHint,
                             Decimals = classP.Decimals ?? interfaceP.Decimals,
                             Format = classP.Format ?? interfaceP.Format,
                             StatusMappings = classP.StatusMappings ?? interfaceP.StatusMappings,
                             EnumLabels = classP.EnumLabels ?? interfaceP.EnumLabels,
                         };
            return merged.IsEmpty ? Presentation.None : merged;
        }

        private static bool HasPublicSetter(PropertyInfo property) =>
            property.SetMethod is not null && property.SetMethod.IsPublic;

        /// <summary>
        ///     Returns true when the property's wire schema carries an identity-bearing
        ///     <c>title</c> (enum or struct, possibly wrapped in Nullable or Array). For those
        ///     types the property-level <c>Title</c> annotation must route to
        ///     <c>Presentation.DisplayName</c>; routing it to <see cref="TypeAnnotations.Title" />
        ///     would be silently dropped by the serializer because identity-set <c>schema.title</c>
        ///     wins on the wire.
        /// </summary>
        private static bool HasIdentityBearingTitle(TypeRef typeRef) => typeRef switch
        {
            EnumTypeRef => true,
            StructTypeRef => true,
            NullableTypeRef n => HasIdentityBearingTitle(n.Inner),
            ArrayTypeRef a => HasIdentityBearingTitle(a.Items),
            _ => false,
        };

        private static TypeAnnotations ExtractTypeAnnotations(ServicePropertyAttribute? sp,
                                                              ServiceMeasuringPointAttribute? mp,
                                                              bool hasPublicSetter,
                                                              bool hasIdentityTitle)
        {
            // Cross-fill: missing field on one side inherits from the other when both
            // [ServiceProperty] and [ServiceMeasuringPoint] are applied to the same property.

            // Title: for enum/struct-typed properties (incl. nullable/array of), schema.title is
            // identity-bearing (the CLR type name). The property-level Title goes to
            // Presentation.DisplayName instead — see ExtractPresentation below.
            var title = hasIdentityTitle ? null : (sp?.Title ?? mp?.Title);
            var description = sp?.Description ?? mp?.Description;
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

            // WriteOnly comes only from [ServiceProperty]; restricted to string / string? properties in v1
            // (DALE022 analyzer enforces).
            var writeOnly = sp?.WriteOnly ?? false;

            // Kind comes only from [ServiceMeasuringPoint]; null when the property isn't a measuring point.
            // The attribute now carries the SDK-Core mirror enum; cast to the canonical wire enum
            // at this boundary. Member values are identical, so the cast is total.
            Vion.Contracts.TypeRef.MeasuringPointKind? kind =
                mp is not null ? (Vion.Contracts.TypeRef.MeasuringPointKind)(int)mp.Kind : null;

            return new TypeAnnotations
                   {
                       Title = title,
                       Description = description,
                       Unit = unit,
                       Minimum = minimum,
                       Maximum = maximum,
                       ReadOnly = readOnly,
                       WriteOnly = writeOnly,
                       Kind = kind,
                   };
        }

        private static Presentation ExtractPresentation(PropertyInfo property,
                                                       ServicePropertyAttribute? sp,
                                                       ServiceMeasuringPointAttribute? mp,
                                                       bool hasIdentityTitle)
        {
            var presentationAttr = property.GetCustomAttribute<PresentationAttribute>();

            // DisplayName: prefer explicit [Presentation(DisplayName=...)].
            // For enum/struct-typed properties, fall back to [ServiceProperty(Title=...)] /
            // [ServiceMeasuringPoint(Title=...)] — schema.title for those types carries the
            // CLR identity (e.g. "AlarmState"), not the property's display label, so without
            // this fallback the property-level Title would be silently lost.
            var displayName = presentationAttr?.DisplayName
                           ?? (hasIdentityTitle ? (sp?.Title ?? mp?.Title) : null);

            var statusMappings = ExtractStatusMappings(property, presentationAttr?.StatusIndicator ?? false);
            var enumLabels = ExtractEnumLabels(property);

            // UiHint: explicit value wins; StatusIndicator = true auto-emits "statusIndicator"
            // so dashboards can detect status-indicator properties by an explicit hint rather
            // than inferring from StatusMappings presence (which is fragile — an enum can be a
            // status indicator without per-member severity tagging).
            var uiHint = presentationAttr?.UiHint
                      ?? (presentationAttr?.StatusIndicator == true ? UiHints.StatusIndicator : null);

            // int.MinValue is the "unset" sentinel for the attribute (attribute-parameter types
            // can't be nullable). Map back to null on the wire.
            int? order = presentationAttr is not null && presentationAttr.Order != int.MinValue
                             ? presentationAttr.Order
                             : null;
            int? decimals = presentationAttr is not null && presentationAttr.Decimals != int.MinValue
                                ? presentationAttr.Decimals
                                : null;

            // Emit Importance only when explicitly non-default. Treats Importance.Normal as the
            // implicit baseline that doesn't need to traverse the wire — keeps the json clean.
            string? importance = presentationAttr is not null && presentationAttr.Importance != Importance.Normal
                                     ? presentationAttr.Importance.ToString()
                                     : null;

            var presentation = new Presentation
                               {
                                   DisplayName = displayName,
                                   Group = presentationAttr?.Group,
                                   Order = order,
                                   // Category dropped — categories fold into Group (which is the same
                                   // dashboard-side concept). Field on the codec record kept for codec
                                   // compatibility but always null from this builder.
                                   Category = null,
                                   Importance = importance,
                                   UIHint = uiHint,
                                   Decimals = decimals,
                                   Format = presentationAttr?.Format,
                                   StatusMappings = statusMappings,
                                   EnumLabels = enumLabels,
                               };

            // If everything is null/empty, return the canonical None instance for cheap equality.
            return presentation.IsEmpty ? Presentation.None : presentation;
        }

        private static ImmutableDictionary<string, string>? ExtractStatusMappings(PropertyInfo property, bool isStatusIndicator)
        {
            if (!isStatusIndicator)
            {
                return null;
            }

            // Only meaningful on (nullable-)enum-typed properties; silently ignore otherwise
            // (DALE024 analyzer warns at compile time).
            var enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
            if (!enumType.IsEnum)
            {
                return null;
            }

            var builder = ImmutableDictionary.CreateBuilder<string, string>();
            foreach (var name in Enum.GetNames(enumType))
            {
                var memberInfo = enumType.GetField(name);
                var severity = memberInfo?.GetCustomAttribute<SeverityAttribute>();
                if (severity is not null)
                {
                    builder[name] = severity.Severity.ToString().ToLowerInvariant();
                }
            }

            return builder.Count > 0 ? builder.ToImmutable() : null;
        }

        /// <summary>
        ///     Reads <c>[EnumLabel("...")]</c> off each member of an enum-typed property and
        ///     returns a map of member-name → display label. Members without a label are omitted.
        ///     Returns null for non-enum properties or when no members carry a label (so
        ///     <see cref="Presentation.IsEmpty" /> stays true in the absent case).
        /// </summary>
        private static ImmutableDictionary<string, string>? ExtractEnumLabels(PropertyInfo property)
        {
            var enumType = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;

            // For array-of-enum properties, peek into the element type.
            if (!enumType.IsEnum && enumType.IsGenericType)
            {
                var def = enumType.GetGenericTypeDefinition();
                if (def == typeof(ImmutableArray<>))
                {
                    var elementType = enumType.GetGenericArguments()[0];
                    enumType = Nullable.GetUnderlyingType(elementType) ?? elementType;
                }
            }

            if (!enumType.IsEnum)
            {
                return null;
            }

            var builder = ImmutableDictionary.CreateBuilder<string, string>();
            foreach (var name in Enum.GetNames(enumType))
            {
                var memberInfo = enumType.GetField(name);
                var info = memberInfo?.GetCustomAttribute<EnumLabelAttribute>();
                if (info?.Label is { } label)
                {
                    builder[name] = label;
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
