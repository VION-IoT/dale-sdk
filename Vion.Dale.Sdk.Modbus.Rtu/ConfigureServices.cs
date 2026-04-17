using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Modbus.Rtu
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddDaleModbusCoreSdk();
            serviceCollection.AddSingleton<IModbusRtuRequestFactory, ModbusRtuRequestFactory>();
        }
    }
}