using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Reads wired M-Bus meters through the service provider that owns the M-Bus master.
    /// </summary>
    [PublicApi]
    [ServiceProviderContractType("MBus")]
    public interface IMBus
    {
    }
}
