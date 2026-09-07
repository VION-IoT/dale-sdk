using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Registers the Modbus RTU factory and its Modbus Core dependencies with the Dale runtime's service container.
    ///     Discovered by the runtime via reflection at plugin load time. A development host has no plugin loader to
    ///     discover it, so it is constructed and called by hand there — which is what the SDK's own Modbus RTU
    ///     example does, and why this type is published surface rather than plumbing.
    /// </summary>
    [PublicApi]
    public class DependencyInjection : IConfigureServices
    {
        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddDaleModbusCoreSdk();
            serviceCollection.AddSingleton<IModbusRtuRequestFactory, ModbusRtuRequestFactory>();
        }
    }
}