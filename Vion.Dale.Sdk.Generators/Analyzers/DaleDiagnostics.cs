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
        ///     Runtime: MapToServiceElementType throws NotSupportedException.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE003_UnsupportedServicePropertyType = new("DALE003",
                                                                                                 "Unsupported service property type",
                                                                                                 "Property '{0}' has [{1}] but type '{2}' is not supported. Supported types: bool, string, int, long, short, float, double, decimal, DateTime, TimeSpan, or any enum.",
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

        /// <summary>
        ///     StatusIndicator should only be placed on enum-typed properties.
        ///     On non-enum properties, the StatusMappings annotation is silently absent.
        /// </summary>
        public static readonly DiagnosticDescriptor DALE006_StatusIndicatorRequiresEnum = new("DALE006",
                                                                                              "StatusIndicator requires an enum property",
                                                                                              "Property '{0}' has [StatusIndicator] but type '{1}' is not an enum. Status mappings will be ignored.",
                                                                                              Category,
                                                                                              DiagnosticSeverity.Warning,
                                                                                              true);

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
    }
}