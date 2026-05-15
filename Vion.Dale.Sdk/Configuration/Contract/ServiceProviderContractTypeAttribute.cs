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

        public ServiceProviderContractTypeAttribute(string serviceProviderContractType)
        {
            ServiceProviderContractType = serviceProviderContractType;
        }
    }
}