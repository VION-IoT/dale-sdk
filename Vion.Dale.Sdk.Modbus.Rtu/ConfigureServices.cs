using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Registers the Modbus RTU factory and its Modbus Core dependencies with the Dale runtime's service container.
    ///     Discovered by the runtime via reflection at plugin load time. A development host has no plugin
    ///     loader, so construct one and call it: this package ships no <c>AddDaleModbusRtuSdk</c> extension,
    ///     and nothing else registers the RTU request factory.
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