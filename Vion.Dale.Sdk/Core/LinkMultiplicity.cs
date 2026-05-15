namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Multiplicity of a contract link. Consumer-side on a binding
    ///     (<see cref="LogicBlockInterfaceBindingAttribute" /> /
    ///     <see cref="ServiceProviderContractBindingAttribute" />) and provider-side on
    ///     <c>[ServiceProviderContractType]</c>. Declared by the SDK only; the SDK
    ///     neither validates nor enforces it — enforcement is downstream (cloud-api at
    ///     logic-configuration save/activate).
    /// </summary>
    [PublicApi]
    public enum LinkMultiplicity
    {
        /// <summary>
        ///     Required and single: exactly one counterpart must be linked (1..1).
        /// </summary>
        ExactlyOne,

        /// <summary>
        ///     Optional and single: at most one counterpart may be linked (0..1).
        ///     Provider-side, this expresses single-consumer exclusivity (e.g. a
        ///     digital output that accepts at most one writer).
        /// </summary>
        ZeroOrOne,

        /// <summary>
        ///     Required and many: at least one counterpart must be linked (1..n).
        /// </summary>
        OneOrMore,

        /// <summary>
        ///     Optional and many: any number of counterparts may be linked (0..n).
        ///     The unconstrained default — preserves the pre-multiplicity
        ///     no-enforcement behaviour, so it is omitted from the introspection
        ///     annotations rather than emitted.
        /// </summary>
        ZeroOrMore,
    }
}
