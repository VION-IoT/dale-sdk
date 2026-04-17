using System;

namespace Vion.Dale.Sdk.Configuration.Contract
{
    [AttributeUsage(AttributeTargets.Interface)]
    public class ServiceProviderContractTypeAttribute : Attribute
    {
        public string ServiceProviderContractType { get; }

        public ServiceProviderContractTypeAttribute(string serviceProviderContractType)
        {
            ServiceProviderContractType = serviceProviderContractType;
        }
    }
}