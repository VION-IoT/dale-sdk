namespace Vion.Dale.Sdk.Configuration.Contract
{
    /// <summary>
    ///     Introspection annotation keys for a logic block's service-provider contracts. Surfaced in the
    ///     contract's loose annotation bag so a consumer of the introspection (the DevHost's scenario routing —
    ///     RFC 0010) can act on them without a model change.
    /// </summary>
    public static class ServiceProviderContractAnnotations
    {
        /// <summary>
        ///     The class name of the handler actor that services this contract — the contract's
        ///     <see cref="LogicBlockContractBase.ContractHandlerActorName" />. The DevHost addresses the generic
        ///     stand-in registered under this name when a scenario drives the contract.
        /// </summary>
        public const string ContractHandlerActorName = "contractHandlerActorName";

        /// <summary>
        ///     Present and <c>true</c> when the contract type is declared
        ///     <see cref="ServiceProviderContractTypeAttribute.DevelopmentOnly" /> — development and bench
        ///     surface a simulator binds, which the production runtime refuses. Absent on an ordinary contract.
        /// </summary>
        public const string DevelopmentOnly = "developmentOnly";
    }
}