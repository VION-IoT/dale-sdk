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
    }
}
