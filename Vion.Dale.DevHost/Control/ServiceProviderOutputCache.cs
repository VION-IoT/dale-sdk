using System.Collections.Concurrent;
using System.Text.Json;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     The last value a block wrote on each service-provider <em>output</em> contract, captured generically
    ///     by <c>ServiceProviderContractHandler</c> (RFC 0010) and read by <c>serviceProviderExpect</c> — the
    ///     generic complement of the typed digital/analog output caches, working for any value contract (the four
    ///     HAL outputs and third-party scalar ones). A DevHost singleton, so it lives for one host generation and
    ///     resets on recycle. Decoded value (the contract's wire JSON); the consumer projects it to a comparable
    ///     scalar.
    /// </summary>
    internal sealed class ServiceProviderOutputCache
    {
        private readonly ConcurrentDictionary<ServiceProviderContractId, JsonElement> _values = new();

        public void Record(ServiceProviderContractId contract, JsonElement value)
        {
            _values[contract] = value;
        }

        public bool TryGet(ServiceProviderContractId contract, out JsonElement value)
        {
            return _values.TryGetValue(contract, out value);
        }
    }
}
