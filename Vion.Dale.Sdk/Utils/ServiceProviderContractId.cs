using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Utils
{
    /// <summary>
    ///     Identifies a contract on a remote service provider.
    ///     Used as the key for contract handler routing.
    /// </summary>
    [InternalApi]
    public readonly record struct ServiceProviderContractId(string ServiceProviderIdentifier, string ServiceIdentifier, string ContractIdentifier)
    {
        public override string ToString()
        {
            return $"{ServiceProviderIdentifier}/{ServiceIdentifier}/{ContractIdentifier}";
        }
    }
}