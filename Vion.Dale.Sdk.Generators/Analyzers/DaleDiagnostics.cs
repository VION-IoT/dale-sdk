using Microsoft.CodeAnalysis;

namespace Vion.Dale.Sdk.Generators.Analyzers
{
    /// <summary>
    ///     Central registry of all Dale diagnostic descriptors.
    ///     IDs are sequential and must never be reused once retired.
    /// </summary>
    internal static class DaleDiagnostics
    {
        private const string Category = "Vion.Dale.Usage";

        // --- Errors (runtime/pack-time exceptions) ---

        /// <summary>
        ///     Contract property (typed as a service provider contract interface) must have at least a private setter.
        ///     Runtime: DeclarativeContractBinder throws InvalidOperationException.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE001_ContractPropertyMustHaveSetter = new("DALE001",
                                                                                                 "ServiceProviderContract property must have a setter",
                                                                                                 "Property '{0}' is typed as a service provider contract but has no setter. Add at least a private setter: {{ get; private set; }}.",
                                                                                                 Category,
                                                                                                 DiagnosticSeverity.Error,
                                                                                                 true);

        /// <summary>
        ///     Timer method must be void and parameterless.
        ///     Runtime: DeclarativeTimerBinder throws InvalidOperationException.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE002_TimerMethodSignature = new("DALE002",
                                                                                       "Timer method must be void and parameterless",
                                                                                       "Method '{0}' has [Timer] but {1}. Timer methods must be void and parameterless.",
                                                                                       Category,
                                                                                       DiagnosticSeverity.Error,
                                                                                       true);

        /// <summary>
        ///     Service property or measuring point type must be in the supported set.
        ///     Unsupported types cannot be represented as a TypeRef by BuildTypeRef.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE003_UnsupportedServicePropertyType = new("DALE003",
                                                                                                 "Unsupported service property type",
                                                                                                 "Property '{0}' has [{1}] but type '{2}' is not supported. Supported: bool, string, byte, short, ushort, int, uint, long, float, double, DateTime, TimeSpan, any enum, any flat readonly record struct, ImmutableArray<T> where T is one of the above, or T? for value types and string.",
                                                                                                 Category,
                                                                                                 DiagnosticSeverity.Error,
                                                                                                 true);

        /// <summary>
        ///     Timer interval must be greater than zero.
        ///     Runtime: TimerAttribute constructor throws ArgumentException.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE005_TimerIntervalMustBePositive = new("DALE005",
                                                                                              "Timer interval must be greater than zero",
                                                                                              "Method '{0}' has [Timer({1})] but the interval must be greater than zero",
                                                                                              Category,
                                                                                              DiagnosticSeverity.Error,
                                                                                              true);

        /// <summary>
        ///     Contract interface names (BetweenInterface, AndInterface) must start with 'I'.
        ///     The source generator silently skips non-conforming names.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE009_ContractInterfaceNamePrefix = new("DALE009",
                                                                                              "Contract interface name must start with 'I'",
                                                                                              "Contract '{0}' has {1}=\"{2}\" which does not start with 'I'",
                                                                                              Category,
                                                                                              DiagnosticSeverity.Error,
                                                                                              true);

        /// <summary>
        ///     Command/StateUpdate From/To values must match the parent contract's BetweenInterface or AndInterface.
        ///     The source generator silently skips mismatched values.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE010_MessageFromToMismatch = new("DALE010",
                                                                                        "Message From/To must match contract interface names",
                                                                                        "Message '{0}' has {1}=\"{2}\" which does not match BetweenInterface=\"{3}\" or AndInterface=\"{4}\"",
                                                                                        Category,
                                                                                        DiagnosticSeverity.Error,
                                                                                        true);

        /// <summary>
        ///     RequestResponse ResponseType must be a struct nested in the same contract class.
        ///     Violating this produces broken generated code.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE011_ResponseTypeMustBeNestedStruct = new("DALE011",
                                                                                                 "ResponseType must be a struct nested in the same contract class",
                                                                                                 "Message '{0}' has ResponseType '{1}' which is not a struct nested in contract class '{2}'",
                                                                                                 Category,
                                                                                                 DiagnosticSeverity.Error,
                                                                                                 true);

        // --- Warnings (silent failures) ---

        /// <summary>
        ///     ServiceMeasuringPoint should not have a public setter.
        ///     Private setter is fine (needed for Metalama INPC weaving).
        ///     A public setter is semantically wrong — measuring points are read-only metrics.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE004_MeasuringPointPublicSetter = new("DALE004",
                                                                                             "ServiceMeasuringPoint should not have a public setter",
                                                                                             "Property '{0}' has [ServiceMeasuringPoint] with a public setter. Measuring points are read-only metrics. Use {{ get; private set; }} instead.",
                                                                                             Category,
                                                                                             DiagnosticSeverity.Warning,
                                                                                             true);

        // DALE006 retired: [StatusIndicator] attribute deleted in declarative-presentation rollout.
        // DALE024 (below) replaces it for [Presentation(StatusIndicator = true)].

        /// <summary>
        ///     Persistent on a read-only property has no effect — PersistentData silently skips it.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE007_PersistentRequiresSetter = new("DALE007",
                                                                                           "Persistent property has no setter",
                                                                                           "Property '{0}' has [Persistent] but no setter. The attribute has no effect on read-only properties.",
                                                                                           Category,
                                                                                           DiagnosticSeverity.Warning,
                                                                                           true);

        /// <summary>
        ///     Two or more Timer methods resolve to the same identifier.
        ///     Runtime: last-write-wins in _timerCallbacks dictionary (silent bug).
        /// </summary>
        public static readonly DiagnosticDescriptor DALE012_DuplicateTimerIdentifier = new("DALE012",
                                                                                           "Duplicate timer identifier",
                                                                                           "Methods '{0}' and '{1}' both resolve to timer identifier '{2}'. Only the last one will be registered.",
                                                                                           Category,
                                                                                           DiagnosticSeverity.Warning,
                                                                                           true);

        // --- Rich-type guards ---

        /// <summary>
        ///     Service-element property typed as a non-ImmutableArray collection.
        ///     Replace with ImmutableArray&lt;T&gt; — Dale's wire format and value semantics rely on it.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE008_ArrayMustBeImmutableArray = new("DALE008",
                                                                                            "Array-valued service element must be ImmutableArray<T>",
                                                                                            "Property '{0}' has [{1}] but type '{2}' is not an ImmutableArray<T>. Use ImmutableArray<T> instead — Dale value semantics require immutability and the wire format relies on it.",
                                                                                            Category,
                                                                                            DiagnosticSeverity.Error,
                                                                                            true);

        /// <summary>
        ///     User-defined struct used as a service-element value must be a readonly record struct
        ///     with flat fields (primitive / enum / nullable-of-primitive-or-enum / string only).
        /// </summary>
        public static readonly DiagnosticDescriptor DALE016_StructMustBeFlatReadonlyRecord = new("DALE016",
                                                                                                 "Struct used as service element must be readonly record struct with flat fields",
                                                                                                 "Property '{0}' has [{1}] but its struct type '{2}' is not a readonly record struct with flat fields. Define it as 'public readonly record struct {2}(...)' with primitive, enum, string, or nullable-of-those parameters only.",
                                                                                                 Category,
                                                                                                 DiagnosticSeverity.Error,
                                                                                                 true);

        /// <summary>
        ///     `string` on a service-element property in a nullable-disabled context is ambiguous.
        ///     Enable nullable annotations or use `string?` explicitly when null is intended.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE017_StringMustBeExplicitlyNullable = new("DALE017",
                                                                                                 "string on a service element must be explicitly nullable",
                                                                                                 "Property '{0}' has [{1}] and type 'string' but the file is in a nullable-disabled context. Enable #nullable or annotate the type as 'string?' if null is intended.",
                                                                                                 Category,
                                                                                                 DiagnosticSeverity.Error,
                                                                                                 true);

        /// <summary>
        ///     ImmutableArray&lt;T&gt; service-element property without an initializer defaults to
        ///     `IsDefault == true` and throws on access. Initialise to `ImmutableArray&lt;T&gt;.Empty` or similar.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE018_ImmutableArrayMustBeInitialised = new("DALE018",
                                                                                                  "ImmutableArray<T> service element should be initialised",
                                                                                                  "Property '{0}' has [{1}] and type ImmutableArray<T> but no initializer. Default ImmutableArray<T> is uninitialised and throws at access. Initialise to 'ImmutableArray<T>.Empty' or similar.",
                                                                                                  Category,
                                                                                                  DiagnosticSeverity.Warning,
                                                                                                  true);

        // --- Public API documentation ---

        /// <summary>
        ///     [PublicApi] type without XML summary documentation.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE013_PublicApiMissingDocs = new("DALE013",
                                                                                       "[PublicApi] type missing XML documentation",
                                                                                       "Type '{0}' is marked [PublicApi] but has no <summary> documentation",
                                                                                       Category,
                                                                                       DiagnosticSeverity.Warning,
                                                                                       true,
                                                                                       customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        /// <summary>
        ///     Public type in a [PublicApiNamespace] without [PublicApi] or [InternalApi].
        /// </summary>
        public static readonly DiagnosticDescriptor DALE014_UnmarkedPublicType = new("DALE014",
                                                                                     "Public type in API namespace not marked [PublicApi] or [InternalApi]",
                                                                                     "Public type '{0}' in namespace '{1}' is not marked [PublicApi] or [InternalApi]",
                                                                                     Category,
                                                                                     DiagnosticSeverity.Warning,
                                                                                     true,
                                                                                     customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        /// <summary>
        ///     [PublicApiNamespace] references a namespace with no public types.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE015_StalePublicApiNamespace = new("DALE015",
                                                                                          "[PublicApiNamespace] references namespace with no public types",
                                                                                          "Assembly attribute [PublicApiNamespace(\"{0}\")] does not match any public types",
                                                                                          Category,
                                                                                          DiagnosticSeverity.Warning,
                                                                                          true,
                                                                                          customTags: new[] { WellKnownDiagnosticTags.CompilationEnd });

        // --- Declarative-presentation analyzers ---

        /// <summary>
        ///     [Presentation(Decimals = N)] only meaningful on numeric properties.
        ///     The decimals hint is silently ignored on other types.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE021_DecimalsOnNonNumeric = new("DALE021",
                                                                                       "[Presentation(Decimals)] only applies to numeric properties",
                                                                                       "Property '{0}' sets Presentation.Decimals but type '{1}' is not numeric. The decimals hint will be ignored.",
                                                                                       Category,
                                                                                       DiagnosticSeverity.Warning,
                                                                                       true);

        /// <summary>
        ///     [ServiceProperty(WriteOnly = true)] restricted to <c>string</c> / <c>string?</c> in v1.
        ///     The wire-side redaction sentinel ("***") is a string literal — other types would
        ///     break the codec round-trip.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE022_WriteOnlyTypeRestriction = new("DALE022",
                                                                                           "[ServiceProperty(WriteOnly)] only supported on string / string?",
                                                                                           "Property '{0}' sets WriteOnly = true but type '{1}' is not string. WriteOnly is restricted to string / string? in v1.",
                                                                                           Category,
                                                                                           DiagnosticSeverity.Error,
                                                                                           true);

        /// <summary>
        ///     [Presentation(UiHint = UiHints.Trigger)] requires a writable bool property.
        ///     The trigger renderer commits <c>true</c> on click; non-bool types break the contract.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE023_TriggerHintRequiresBool = new("DALE023",
                                                                                          "[Presentation(UiHint = \"trigger\")] requires a writable bool property",
                                                                                          "Property '{0}' uses UiHint \"trigger\" but {1}. Triggers require a writable bool property; the dashboard commits 'true' on click.",
                                                                                          Category,
                                                                                          DiagnosticSeverity.Error,
                                                                                          true);

        /// <summary>
        ///     [Presentation(StatusIndicator = true)] requires an enum (or nullable-enum) property.
        ///     On non-enum properties the StatusMappings annotation is silently absent.
        ///     Replaces the retired DALE006 (which targeted the now-deleted [StatusIndicator]).
        /// </summary>
        public static readonly DiagnosticDescriptor DALE024_StatusIndicatorRequiresEnum = new("DALE024",
                                                                                              "[Presentation(StatusIndicator = true)] requires an enum property",
                                                                                              "Property '{0}' sets StatusIndicator = true but type '{1}' is not an enum. Status mappings will be ignored.",
                                                                                              Category,
                                                                                              DiagnosticSeverity.Warning,
                                                                                              true);

        /// <summary>
        ///     Two or more attributes deriving from the same platform base attribute appear on a
        ///     single property — e.g. <c>[Kilowatts][Volts]</c> where both inherit from
        ///     <c>ServicePropertyAttribute</c>. Author must pick one. Distinct platform bases on
        ///     the same property (e.g. <c>[ServiceProperty][ServiceMeasuringPoint]</c>) are
        ///     allowed and drive the cross-fill rule.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE019_MultipleAttributesFromSameBase = new("DALE019",
                                                                                                 "Multiple attributes derive from the same platform base attribute",
                                                                                                 "Property '{0}' has multiple attributes deriving from '{1}': {2}. Pick one — preset-attribute inheritance does not support stacking.",
                                                                                                 Category,
                                                                                                 DiagnosticSeverity.Error,
                                                                                                 true);

        /// <summary>
        ///     The same property name is declared in two or more implemented interfaces with
        ///     incompatible attribute metadata (different <c>Unit</c>). The cascade rule has no
        ///     way to pick a winner; author must override with an explicit attribute on the
        ///     class or align the interface declarations.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE020_MultiInterfaceConflict = new("DALE020",
                                                                                         "Multi-interface attribute conflict",
                                                                                         "Class '{0}' implements multiple interfaces declaring property '{1}' with conflicting Unit values ({2}). Override with an explicit attribute on the class or align the interface declarations.",
                                                                                         Category,
                                                                                         DiagnosticSeverity.Error,
                                                                                         true);

        /// <summary>
        ///     A property declares both <c>[ServiceProperty]</c> and <c>[ServiceMeasuringPoint]</c>
        ///     and both set the same field (Title / Description / Unit / Minimum / Maximum) to
        ///     conflicting non-empty values. The cross-fill rule needs one source of truth per
        ///     field — pick one attribute to carry the value.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE025_CrossFillConflict = new("DALE025",
                                                                                    "[ServiceProperty] / [ServiceMeasuringPoint] cross-fill conflict",
                                                                                    "Property '{0}' has [ServiceProperty({1} = {2})] and [ServiceMeasuringPoint({1} = {3})] with conflicting values. Pick one and let the cross-fill rule do its job.",
                                                                                    Category,
                                                                                    DiagnosticSeverity.Warning,
                                                                                    true);

        /// <summary>
        ///     A literal string passed as <c>[Presentation(Group = "...")]</c> doesn't match any
        ///     constant declared in a <c>PropertyGroup</c>-named static class anywhere in the
        ///     compilation. Likely a typo — recommended fix: use the constant. Suppressible via
        ///     <c>#pragma warning disable</c> for one-off custom keys without a constant.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE026_LiteralGroupKey = new("DALE026",
                                                                                  "Literal Group key doesn't match any PropertyGroup constant",
                                                                                  "Literal Group key \"{0}\" doesn't match any constant in a PropertyGroup-named static class. Recommended: use a constant. Suppress this warning if the literal is intentional.",
                                                                                  Category,
                                                                                  DiagnosticSeverity.Warning,
                                                                                  true);

        // DALE029 retired: the Metalama [Observable] field-keyword setter-body drop it guarded
        // (metalama/Metalama#1644) was fixed upstream in Metalama.Patterns.Observability 2026.1.18.
        // MetalamaFieldKeywordReproShould is the regression guard. ID not reused.

        /// <summary>
        ///     <c>[ServiceProperty(ReadOnly = true, WriteOnly = true)]</c> is incoherent: <c>ReadOnly</c>
        ///     blocks cloud writes while <c>WriteOnly</c> redacts the publish-state value for clients.
        ///     The two flags hide opposite directions of the value flow and cannot meaningfully combine —
        ///     pick one.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE030_ReadOnlyAndWriteOnlyMutuallyExclusive = new("DALE030",
                                                                                                        "[ServiceProperty(ReadOnly, WriteOnly)] are mutually exclusive",
                                                                                                        "Property '{0}' sets both ReadOnly = true and WriteOnly = true on [ServiceProperty]. These flags hide opposite directions of the value flow — ReadOnly blocks cloud writes; WriteOnly redacts the publish-state value. Pick one.",
                                                                                                        Category,
                                                                                                        DiagnosticSeverity.Error,
                                                                                                        true);

        /// <summary>
        ///     A computed observable property (an explicit/expression-bodied getter carrying [ServiceProperty]
        ///     or [ServiceMeasuringPoint]) derives its value from a MEMBER of a struct-typed observable property
        ///     — e.g. <c>Bands.Capacity</c> where <c>Bands</c> is a struct [ServiceProperty]. The
        ///     Metalama.Patterns.Observability aspect tracks whole-property changes and method calls on the
        ///     struct, but NOT direct struct-member reads, so the computed property is woven without a dependency
        ///     on the struct property and never re-publishes when it changes — a silent, permanently-stale value
        ///     with no other compile-time signal.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE031_ObservableStructMemberDependencyNotTracked = new("DALE031",
                                                                                                             "Computed observable property reads an untracked struct member",
                                                                                                             "Property '{0}' reads '{1}.{2}', a member of the struct-typed observable property '{1}'. The Observability aspect does not track struct-member reads, so '{0}' will not re-publish when '{1}' changes. Derive '{0}' from scalar observable properties, recompute it in '{1}'s setter, or call a method on '{1}' (method calls are tracked).",
                                                                                                             Category,
                                                                                                             DiagnosticSeverity.Warning,
                                                                                                             true);

        /// <summary>
        ///     <c>[Presentation(Format = "...")]</c> is consumed by the renderer only for
        ///     <c>DateTime</c> / <c>TimeSpan</c> properties (and nullable variants). On other
        ///     types the format hint is silently ignored.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE027_FormatOnNonTemporal = new("DALE027",
                                                                                      "[Presentation(Format)] only applies to DateTime / TimeSpan properties",
                                                                                      "Property '{0}' sets Presentation.Format but type '{1}' is not DateTime or TimeSpan. The format hint will be ignored.",
                                                                                      Category,
                                                                                      DiagnosticSeverity.Warning,
                                                                                      true);

        /// <summary>
        ///     The sentinel <c>Formats.Relative</c> requires a <c>DateTime</c> property; the
        ///     sentinel <c>Formats.Humanize</c> requires a <c>TimeSpan</c> property. On a
        ///     mismatched property type the renderer falls back to the default formatter.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE028_FormatSentinelTypeMismatch = new("DALE028",
                                                                                             "Format sentinel doesn't match property type",
                                                                                             "Property '{0}' uses Format = \"{1}\" which requires {2}, but the property type is '{3}'. The renderer will fall back to the default formatter.",
                                                                                             Category,
                                                                                             DiagnosticSeverity.Warning,
                                                                                             true);

        /// <summary>
        ///     <c>[Presentation(Importance = Primary | Secondary)]</c> on a property whose type is composite
        ///     (a flat record struct or <c>ImmutableArray&lt;T&gt;</c>). The auto-generated LogicBlock dashboard
        ///     tile renders Primary/Secondary metrics as a single scalar value via number/list/battery widgets —
        ///     a struct value stringifies to <c>"[object Object]"</c> and an array to a raw comma blob. Scalars
        ///     (numeric / bool / string / enum / DateTime / TimeSpan) render fine. Use <c>Importance.Normal</c>
        ///     (detail view only) for composite values, or surface a scalar member as its own tile property.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE032_ImportanceRequiresScalarType = new("DALE032",
                                                                                               "[Presentation(Importance = Primary/Secondary)] requires a scalar property type",
                                                                                               "Property '{0}' has [Presentation(Importance = {1})] but type '{2}' is a composite (struct or array) type. Dashboard tiles render Primary/Secondary metrics as a single scalar value — structs show as '[object Object]', arrays as a raw blob. Use Importance.Normal for detail views, or surface a scalar member as its own tile property.",
                                                                                               Category,
                                                                                               DiagnosticSeverity.Warning,
                                                                                               true);

        /// <summary>
        ///     <c>StringFormat</c> on <c>[ServiceProperty]</c> / <c>[ServiceMeasuringPoint]</c> is honored
        ///     only for <c>string</c> / <c>string?</c> members, and its value must not be a reserved
        ///     type-kind format (<c>date-time</c> / <c>duration</c> / <c>uuid</c>) — those have dedicated
        ///     CLR types (<c>DateTime</c> / <c>TimeSpan</c> / <c>Guid</c>). Otherwise the hint is misplaced.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE033_StringFormatOnNonString = new("DALE033",
                                                                                          "StringFormat only applies to string properties",
                                                                                          "Property '{0}' (type '{1}') sets StringFormat, which is honored only on string/string? members. Use the matching CLR type for DateTime/TimeSpan/Guid, or [Presentation(Format/Decimals)] for display; a reserved type-kind format (date-time/duration/uuid) is not allowed.",
                                                                                          Category,
                                                                                          DiagnosticSeverity.Warning,
                                                                                          true);

        // --- Emission policy (RFC 0004) ---

        /// <summary>
        ///     <c>MinChange</c> (the deadband) is set on a <c>[ServiceProperty]</c> /
        ///     <c>[ServiceMeasuringPoint]</c> whose value type has no resolvable
        ///     <c>IChangeThreshold&lt;T&gt;</c>. The runtime ships built-ins for double, float, decimal,
        ///     int, long, and <c>TimeSpan</c>; any other type needs an
        ///     <c>IChangeThreshold&lt;ThatType&gt;</c> implementation visible in the compilation, else the
        ///     deadband can never be resolved at start-up. <c>bool</c> is the clearest case — no magnitude
        ///     to threshold — and is always an error.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE034_MinChangeWithoutChangeThreshold = new("DALE034",
                                                                                                  "MinChange has no resolvable IChangeThreshold<T>",
                                                                                                  "Property '{0}' sets MinChange but type '{1}' has no built-in or registered IChangeThreshold<{1}>. Built-ins exist for double, float, decimal, int, long, TimeSpan; for any other type implement IChangeThreshold<{1}> (bool has no magnitude and is never valid).",
                                                                                                  Category,
                                                                                                  DiagnosticSeverity.Error,
                                                                                                  true);

        /// <summary>
        ///     <c>MinChange</c> on a built-in numeric type or <c>TimeSpan</c> does not parse with that
        ///     type's known format: numeric types need an invariant-culture number; a <c>TimeSpan</c>
        ///     needs the duration grammar (<c>us</c>/<c>ms</c>/<c>s</c>/<c>m</c>/<c>h</c> suffix or a bare
        ///     number = milliseconds). Custom-threshold types are not parse-checked — their format is opaque.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE035_MinChangeUnparseable = new("DALE035",
                                                                                       "MinChange is not parseable for the member's type",
                                                                                       "Property '{0}' has MinChange = \"{1}\" which is not valid for type '{2}'. {3} is expected.",
                                                                                       Category,
                                                                                       DiagnosticSeverity.Error,
                                                                                       true);

        /// <summary>
        ///     <c>MinInterval</c> does not parse with the duration grammar (error) or parses to a positive
        ///     value below the 1 ms floor the emission gate can honour (warning). The sentinel
        ///     <c>"0"</c> / <c>"0ms"</c> (throttle disabled) is valid and never reported.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE036_MinIntervalInvalid = new("DALE036",
                                                                                     "MinInterval is invalid",
                                                                                     "Property '{0}' has MinInterval = \"{1}\" which is not a valid duration. Use a number with an optional us/ms/s/m/h suffix (bare number = milliseconds), or \"0\" to disable throttling.",
                                                                                     Category,
                                                                                     DiagnosticSeverity.Error,
                                                                                     true);

        /// <summary>
        ///     <c>MinInterval</c> parses to a positive value below the 1 ms floor. The emission gate's
        ///     trailing-edge flush rides the actor scheduler, which cannot meaningfully honour a sub-1ms
        ///     interval — the value is effectively rounded up to the gate's resolution.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE037_MinIntervalBelowFloor = new("DALE037",
                                                                                        "MinInterval is below the 1 ms floor",
                                                                                        "Property '{0}' has MinInterval = \"{1}\" which is below the 1 ms floor the emission gate can honour. Use a value >= 1 ms, or \"0\" to disable throttling.",
                                                                                        Category,
                                                                                        DiagnosticSeverity.Warning,
                                                                                        true);

        /// <summary>
        ///     <c>Immediate = true</c> bypasses the throttle and the deadband, so a non-default
        ///     <c>MinInterval</c> or any <c>MinChange</c> declared alongside it is silently ignored. Drop
        ///     the ignored knob, or drop <c>Immediate</c> if throttling/deadband is intended.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE038_ImmediateIgnoresThrottleKnobs = new("DALE038",
                                                                                                "Immediate ignores MinInterval / MinChange",
                                                                                                "Property '{0}' sets Immediate = true together with {1}. Immediate bypasses the throttle and the deadband, so that knob is ignored. Drop it, or drop Immediate if throttling/deadband is intended.",
                                                                                                Category,
                                                                                                DiagnosticSeverity.Warning,
                                                                                                true);

        /// <summary>
        ///     <c>MinChange</c> (a deadband) is set while <c>MinInterval</c> is the disabling sentinel
        ///     <c>"0"</c> / <c>"0ms"</c> — a valid deadband-only configuration (no time throttle, change
        ///     gate still applies). Surfaced as information so the intent is explicit.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE039_DeadbandWithoutThrottle = new("DALE039",
                                                                                          "MinChange with throttling disabled (deadband only)",
                                                                                          "Property '{0}' sets MinChange while MinInterval = \"{1}\" disables time-throttling. This is a valid deadband-only configuration — emission is gated by change magnitude alone.",
                                                                                          Category,
                                                                                          DiagnosticSeverity.Info,
                                                                                          true);

        /// <summary>
        ///     [StructField(WriteOnly = true)] restricted to <c>string</c> / <c>string?</c> in v1 — the
        ///     per-member analogue of DALE022. The redaction sentinel ("***") is a string literal, so a
        ///     non-string member would break the codec round-trip and be rejected by the contracts schema
        ///     parser (InvalidSchemaException) when the introspection is uploaded / activated.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE040_WriteOnlyStructFieldTypeRestriction = new("DALE040",
                                                                                                      "[StructField(WriteOnly)] only supported on string / string?",
                                                                                                      "Struct field '{0}' sets WriteOnly = true but type '{1}' is not string. WriteOnly is restricted to string / string? in v1.",
                                                                                                      Category,
                                                                                                      DiagnosticSeverity.Error,
                                                                                                      true);
    }
}