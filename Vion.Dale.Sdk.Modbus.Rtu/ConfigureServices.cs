using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    /// <summary>
    ///     Registers the Modbus RTU factory and its Modbus Core dependencies with the Dale runtime's service container.
    ///     Discovered by the runtime via reflection at plugin load time — consumers do not call this directly.
    /// </summary>
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