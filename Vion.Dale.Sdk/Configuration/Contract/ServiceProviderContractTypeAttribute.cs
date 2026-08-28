using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Configuration.Contract
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class ServiceProviderContractTypeAttribute : Attribute
    {
        public string ServiceProviderContractType { get; }

        /// <summary>
        ///     Provider-side acceptance: how many consumers this provided contract
        ///     role accepts. Default <see cref="LinkMultiplicity.ZeroOrMore" />
        ///     (unconstrained). E.g. a digital output is single-writer
        ///     (<see cref="LinkMultiplicity.ZeroOrOne" />). Declared only; enforced
        ///     downstream (cloud-api at logic-configuration save/activate).
        /// </summary>
        public LinkMultiplicity Consumers { get; init; } = LinkMultiplicity.ZeroOrMore;

        /// <summary>
        ///     Marks the contract as development and bench surface — a simulator binds it to stand in for
        ///     equipment that is not there. Default <c>false</c>. A block bound to such a contract is refused
        ///     by the production runtime, so declare it only where running against real hardware would be
        ///     wrong. Surfaced in the introspection metadata so tooling can tell the two apart.
        /// </summary>
        public bool DevelopmentOnly { get; init; }

        public ServiceProviderContractTypeAttribute(string serviceProviderContractType)
        {
            ServiceProviderContractType = serviceProviderContractType;
        }
    }
}