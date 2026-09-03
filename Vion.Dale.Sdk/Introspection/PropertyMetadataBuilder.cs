using System;
using System.Collections.Immutable;
using System.Reflection;
using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Emission;
using MeasuringPointKind = Vion.Contracts.TypeRef.MeasuringPointKind;

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
        // The attribute default both emission attributes declare for MinInterval.
        private const string DefaultMinInterval = "250ms";

        /// <summary>
        ///     Builds a typed <see cref="PropertyMetadata" /> document for the given property.
        ///     The <paramref name="typeRef" /> is supplied by the caller (built from the property's CLR type
        ///     by the introspection pipeline).
        ///     The <paramref name="structFieldAnnotations" /> map carries per-struct-field
        ///     <c>[StructField]</c> data when the property is a struct or array-of-struct;
        ///     pass <see cref="ImmutableDictionary{TKey,TValue}.Empty" /> when not applicable.
        ///     The <paramref name="stream" /> names which of the member's two publication streams this
        ///     document describes, so the emission knobs come from that stream's own attribute.
        /// </summary>
        public static PropertyMetadata Build(PropertyInfo property,
                                             TypeRef typeRef,
                                             ImmutableDictionary<string, TypeAnnotations> structFieldAnnotations,
                                             ServiceElementStream stream)
        {
            var sp = property.GetCustomAttribute<ServicePropertyAttribute>();
            var mp = property.GetCustomAttribute<ServiceMeasuringPointAttribute>();
            var hasIdentityTitle = HasIdentityBearingTitle(typeRef);

            var isInstantiationParameter = property.GetCustomAttribute<InstantiationParameterAttribute>() is not null;
            var annotations = ExtractTypeAnnotations(sp,
                                                     mp,
                                                     HasPublicSetter(property),
                                                     hasIdentityTitle,
                                                     isInstantiationParameter,
                                                     stream);
            var schema = new TypeSchema(typeRef, annotations, structFieldAnnotations);
            var presentation = ExtractPresentation(property, sp, mp, hasIdentityTitle);
            var runtime = ExtractRuntime(property, stream);

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
                                                  ImmutableDictionary<string, TypeAnnotations> structFieldAnnotations,
                                                  ServiceElementStream stream)
        {
            var sp = schemaSource.GetCustomAttribute<ServicePropertyAttribute>();
            var mp = schemaSource.GetCustomAttribute<ServiceMeasuringPointAttribute>();
            var hasIdentityTitle = HasIdentityBearingTitle(typeRef);

            // Writability is governed by the implementing logic-block property — that's the actual
            // binding target when cloud calls SetPropertyValue. The interface only declares intent.
            var isInstantiationParameter = presentationSource.GetCustomAttribute<InstantiationParameterAttribute>() is not null;
            var annotations = ExtractTypeAnnotations(sp,
                                                     mp,
                                                     HasPublicSetter(presentationSource),
                                                     hasIdentityTitle,
                                                     isInstantiationParameter,
                                                     stream);
            var schema = new TypeSchema(typeRef, annotations, structFieldAnnotations);

            // Per-field presentation merge: the class wins on any field it explicitly sets, and
            // inherits from the interface on fields it leaves null. This lets interfaces declare
            // shared UI semantics (Group, Importance) while classes override per-instance details
            // (DisplayName, Order).
            var interfacePresentation = ExtractPresentation(schemaSource, sp, mp, hasIdentityTitle);
            var classPresentation = ExtractPresentation(presentationSource, sp, mp, hasIdentityTitle);
            var presentation = MergePresentation(classPresentation, interfacePresentation);

            var runtime = ExtractRuntimeSplit(presentationSource, schemaSource, stream);

            return new PropertyMetadata(schema, presentation, runtime);
        }

        /// <summary>
        ///     Returns true when the property's wire schema carries an identity-bearing
        ///     <c>title</c> (enum or struct, possibly wrapped in Nullable or Array). For those
        ///     types the property-level <c>Title</c> annotation must route to
        ///     <c>Presentation.DisplayName</c>; routing it to <see cref="TypeAnnotations.Title" />
        ///     would be silently dropped by the serializer because identity-set <c>schema.title</c>
        ///     wins on the wire.
        ///     <see cref="StructFieldPresentationBuilder" /> applies the same predicate one level down,
        ///     to a struct field's own <see cref="TypeRef" /> — same rule, same reason.
        /// </summary>
        internal static bool HasIdentityBearingTitle(TypeRef typeRef)
        {
            return typeRef switch
            {
                EnumTypeRef => true,
                StructTypeRef => true,
                NullableTypeRef n => HasIdentityBearingTitle(n.Inner),
                ArrayTypeRef a => HasIdentityBearingTitle(a.Items),
                _ => false,
            };
        }

        /// <summary>
        ///     Reads <c>[Severity]</c> off each member of an enum type (peeling <c>Nullable&lt;T&gt;</c>)
        ///     and returns a map of member-name → lowercase severity. Members without the attribute are
        ///     omitted; returns null for a non-enum type or when no member carries one.
        /// </summary>
        internal static ImmutableDictionary<string, string>? ExtractStatusMappings(Type type)
        {
            // Only meaningful on (nullable-)enum types; silently ignore otherwise
            // (DALE024 analyzer warns at compile time).
            var enumType = Nullable.GetUnderlyingType(type) ?? type;
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
        ///     Reads <c>[EnumLabel("...")]</c> off each member of an enum type (peeling
        ///     <c>Nullable&lt;T&gt;</c> and <c>ImmutableArray&lt;T&gt;</c>) and returns a map of
        ///     member-name → display label. Members without a label are omitted. Returns null for a
        ///     non-enum type or when no member carries a label (so <see cref="Presentation.IsEmpty" />
        ///     stays true in the absent case).
        /// </summary>
        internal static ImmutableDictionary<string, string>? ExtractEnumLabels(Type type)
        {
            var enumType = Nullable.GetUnderlyingType(type) ?? type;

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
                             VisibleWhen = classP.VisibleWhen ?? interfaceP.VisibleWhen,
                             StatusMappings = classP.StatusMappings ?? interfaceP.StatusMappings,
                             EnumLabels = classP.EnumLabels ?? interfaceP.EnumLabels,
                         };
            return merged.IsEmpty ? Presentation.None : merged;
        }

        private static bool HasPublicSetter(PropertyInfo property)
        {
            return property.SetMethod is not null && property.SetMethod.IsPublic;
        }

        private static TypeAnnotations ExtractTypeAnnotations(ServicePropertyAttribute? sp,
                                                              ServiceMeasuringPointAttribute? mp,
                                                              bool hasPublicSetter,
                                                              bool hasIdentityTitle,
                                                              bool isInstantiationParameter,
                                                              ServiceElementStream stream)
        {
            // Cross-fill: missing field on one side inherits from the other when both
            // [ServiceProperty] and [ServiceMeasuringPoint] are applied to the same property.

            // Title: for enum/struct-typed properties (incl. nullable/array of), schema.title is
            // identity-bearing (the CLR type name). The property-level Title goes to
            // Presentation.DisplayName instead — see ExtractPresentation below.
            var title = hasIdentityTitle ? null : sp?.Title ?? mp?.Title;
            var description = sp?.Description ?? mp?.Description;
            var unit = sp?.Unit ?? mp?.Unit;
            var stringFormat = sp?.StringFormat ?? mp?.StringFormat;

            var minimum = FiniteBound(sp?.Minimum) ?? FiniteBound(mp?.Minimum);
            var maximum = FiniteBound(sp?.Maximum) ?? FiniteBound(mp?.Maximum);

            // ReadOnly on the wire when ANY of:
            //   - measuring point alone, without a service-property attribute (canonical metric — read-only)
            //   - no public C# setter (legacy implicit rule; e.g. `[ServiceProperty] public int Foo { get; private set; }`
            //     exposes a value the gateway publishes but the cloud cannot SetPropertyValue back to)
            //   - [ServiceProperty(ReadOnly = true)] explicitly opts in — needed when a cross-assembly helper
            //     requires the public setter but the cloud must not write the value.
            //   - [InstantiationParameter] — config-time value, wire-read-only + immutable at runtime;
            //     it deliberately has a public setter (the SDK applies the value pre-Configure by reflection),
            //     so this forced flag is what makes the dashboard render it read-only and the cloud reject SETs.
            var readOnly = (mp is not null && sp is null) || !hasPublicSetter || (sp?.ReadOnly ?? false) || isInstantiationParameter;

            // WriteOnly comes only from [ServiceProperty]; restricted to string / string? properties in v1
            // (DALE022 analyzer enforces).
            var writeOnly = sp?.WriteOnly ?? false;

            // Kind describes the measuring point's series, so it rides that stream's document and not the
            // service property's — the same per-stream rule the emission knobs follow. A member declaring both
            // attributes would otherwise report a measuring-point kind on its property document too, and a
            // client badges what it finds there. The attribute carries the SDK-Core mirror enum; cast to the
            // canonical wire enum at this boundary. Member values are identical, so the cast is total.
            MeasuringPointKind? kind = mp is not null && stream == ServiceElementStream.MeasuringPoint ? (MeasuringPointKind)(int)mp.Kind : null;

            return new TypeAnnotations
                   {
                       Title = title,
                       Description = description,
                       Unit = unit,
                       Format = stringFormat,
                       Minimum = minimum,
                       Maximum = maximum,
                       ReadOnly = readOnly,
                       WriteOnly = writeOnly,
                       Kind = kind,
                   };
        }

        /// <summary>
        ///     A declared bound, or <c>null</c> where it is not a number the wire can carry. The attribute's
        ///     defaults are the two infinities — one per bound — so the finiteness test doubles as the
        ///     absent-sentinel test and closes the two cases the one-sided test let through: the other
        ///     infinity, and <c>NaN</c>. Both are values the compiler accepts and no analyzer judges, and
        ///     <c>System.Text.Json</c> refuses to write either, so one such bound aborted the whole
        ///     introspection document with an exception naming neither the member nor the block.
        /// </summary>
        private static double? FiniteBound(double? declared)
        {
            return declared is { } value && !double.IsNaN(value) && !double.IsInfinity(value) ? value : null;
        }

        private static Presentation ExtractPresentation(PropertyInfo property, ServicePropertyAttribute? sp, ServiceMeasuringPointAttribute? mp, bool hasIdentityTitle)
        {
            var presentationAttr = property.GetCustomAttribute<PresentationAttribute>();

            // DisplayName: prefer explicit [Presentation(DisplayName=...)].
            // For enum/struct-typed properties, fall back to [ServiceProperty(Title=...)] /
            // [ServiceMeasuringPoint(Title=...)] — schema.title for those types carries the
            // CLR identity (e.g. "AlarmState"), not the property's display label, so without
            // this fallback the property-level Title would be silently lost.
            var displayName = presentationAttr?.DisplayName ?? (hasIdentityTitle ? sp?.Title ?? mp?.Title : null);

            var statusMappings = ExtractStatusMappings(property, presentationAttr?.StatusIndicator ?? false);
            var enumLabels = ExtractEnumLabels(property.PropertyType);

            // UiHint: explicit value wins; StatusIndicator = true auto-emits "statusIndicator"
            // so dashboards can detect status-indicator properties by an explicit hint rather
            // than inferring from StatusMappings presence (which is fragile — an enum can be a
            // status indicator without per-member severity tagging).
            var uiHint = presentationAttr?.UiHint ?? (presentationAttr?.StatusIndicator == true ? UiHints.StatusIndicator : null);

            // int.MinValue is the "unset" sentinel for the attribute (attribute-parameter types
            // can't be nullable). Map back to null on the wire.
            int? order = presentationAttr is not null && presentationAttr.Order != int.MinValue ? presentationAttr.Order : null;
            int? decimals = presentationAttr is not null && presentationAttr.Decimals != int.MinValue ? presentationAttr.Decimals : null;

            // Emit Importance only when explicitly non-default. Treats Importance.Normal as the
            // implicit baseline that doesn't need to traverse the wire — keeps the json clean.
            var importance = presentationAttr is not null && presentationAttr.Importance != Importance.Normal ? presentationAttr.Importance.ToString() : null;

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

                                   // Conditional-visibility predicate. Emitted verbatim into
                                   // presentation.visibleWhen; parse/type discipline is enforced by the
                                   // DALE041/DALE042 analyzers, not here. Rides both sibling docs
                                   // automatically for a dual-annotated [ServiceProperty]+[ServiceMeasuringPoint]
                                   // member, since the same presentation node feeds both.
                                   VisibleWhen = presentationAttr?.VisibleWhen,
                                   StatusMappings = statusMappings,
                                   EnumLabels = enumLabels,
                               };

            // If everything is null/empty, return the canonical None instance for cheap equality.
            return presentation.IsEmpty ? Presentation.None : presentation;
        }

        private static ImmutableDictionary<string, string>? ExtractStatusMappings(PropertyInfo property, bool isStatusIndicator)
        {
            // At property level the map is gated on [Presentation(StatusIndicator = true)], because that
            // same flag is what routes the property to a status tile. A struct field has no tile, so
            // StructFieldPresentationBuilder calls the ungated core below directly.
            return isStatusIndicator ? ExtractStatusMappings(property.PropertyType) : null;
        }

        private static RuntimeMetadata ExtractRuntime(PropertyInfo property, ServiceElementStream stream)
        {
            // Persistent: presence of [Persistent] without Exclude=true => Persistent=true in output.
            // [Persistent(Exclude = true)] records as Persistent=false (treat opt-out as not persistent).
            var persistentAttr = property.GetCustomAttribute<PersistentAttribute>();
            var persistent = persistentAttr is not null && !persistentAttr.Exclude;

            var runtime = new RuntimeMetadata { Persistent = persistent, Throttle = ExtractThrottle(property, stream) };
            return runtime.IsEmpty ? RuntimeMetadata.None : runtime;
        }

        // Runtime metadata for an interface-bound property. Persistence stays an impl concern (it is
        // declared on the logic-block property), but the emission throttle is surfaced from whichever
        // property the runtime gate actually reads it from: the impl wins when it declares its own
        // [ServiceProperty]/[ServiceMeasuringPoint], otherwise the knobs are inherited from the
        // [ServiceInterface]. This mirrors LogicBlockBase.ResolveThrottleConfigured exactly,
        // so the UI throttle chip matches the policy the gate enforces — including the §8.12 DRY pattern
        // where the impl carries only presentation and the knobs live on the interface.
        private static RuntimeMetadata ExtractRuntimeSplit(PropertyInfo presentationSource, PropertyInfo schemaSource, ServiceElementStream stream)
        {
            var persistentAttr = presentationSource.GetCustomAttribute<PersistentAttribute>();
            var persistent = persistentAttr is not null && !persistentAttr.Exclude;

            var throttleSource = HasEmissionAttribute(presentationSource, stream) ? presentationSource : schemaSource;
            var runtime = new RuntimeMetadata { Persistent = persistent, Throttle = ExtractThrottle(throttleSource, stream) };
            return runtime.IsEmpty ? RuntimeMetadata.None : runtime;
        }

        private static bool HasEmissionAttribute(PropertyInfo property, ServiceElementStream stream)
        {
            return EmissionAttribute(property, stream) is not null;
        }

        // The attribute carrying the knobs for the stream being described. Never the sibling's: a member
        // declaring both attributes declares two independent policies, and reporting the property's on the
        // measuring point would show a badge that the gate does not enforce.
        private static IThrottleConfigured? EmissionAttribute(PropertyInfo property, ServiceElementStream stream)
        {
            return stream == ServiceElementStream.Property ? property.GetCustomAttribute<ServicePropertyAttribute>() :
                       property.GetCustomAttribute<ServiceMeasuringPointAttribute>();
        }

        // The effective emission policy (throttle / deadband / immediate), read from the
        // [ServiceProperty] / [ServiceMeasuringPoint] knobs. Surfaced only when it deviates from the
        // default (MinInterval 250ms, no deadband, not immediate) to keep introspection lean; when
        // surfaced the *effective* MinInterval is carried, so a consumer needs no knowledge of the default.
        private static ThrottleMetadata? ExtractThrottle(PropertyInfo property, ServiceElementStream stream)
        {
            var cfg = EmissionAttribute(property, stream);
            if (cfg is null)
            {
                return null;
            }

            // An empty MinChange is unset, the way ThrottlePolicy.FromConfigured reads it — otherwise a
            // member would be reported as carrying a deadband the gate does not apply.
            var minChange = string.IsNullOrEmpty(cfg.MinChange) ? null : cfg.MinChange;

            if (IsDefaultInterval(cfg.MinInterval) && minChange is null && !cfg.Immediate)
            {
                return null;
            }

            return new ThrottleMetadata
                   {
                       MinInterval = cfg.MinInterval,
                       MinChange = minChange,
                       Immediate = cfg.Immediate,
                   };
        }

        // Compares the declared interval as a DURATION, not as a spelling: "250" and "250ms" configure the
        // same gate, so reporting one as a deviation and the other as the default would badge two identical
        // declarations differently. A token the grammar rejects counts as a deviation, so the offending
        // value reaches the consumer instead of being hidden behind the default.
        private static bool IsDefaultInterval(string minInterval)
        {
            return DurationParser.TryParse(minInterval, out var declared) && DurationParser.TryParse(DefaultMinInterval, out var standard) && declared == standard;
        }
    }
}