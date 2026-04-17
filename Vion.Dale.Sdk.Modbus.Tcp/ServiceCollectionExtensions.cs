using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Modbus.Tcp
{
    /// <summary>
    ///     Extension methods for setting up Modbus TCP services in an <see cref="IServiceCollection" />.
    /// </summary>
    [PublicApi]
    public static class ServiceCollectionExtensions
    {
        /// <summary>
        ///     Adds Modbus TCP services to the specified <see cref="IServiceCollection" />.
        /// </summary>
        /// <param name="serviceCollection">The <see cref="IServiceCollection" /> to add services to.</param>
        /// <returns>The <see cref="IServiceCollection" /> so that additional calls can be chained.</returns>
        public static IServiceCollection AddDaleModbusTcpSdk(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddDaleModbusCoreSdk();
            serviceCollection.AddSingleton<ILogicBlockModbusTcpClientFactory, LogicBlockModbusTcpClientFactory>();
            serviceCollection.AddTransient<ILogicBlockModbusTcpClient, LogicBlockModbusTcpClient>();
            serviceCollection.AddTransient<IRequestFactory, RequestFactory>();
            serviceCollection.AddTransient<IRequestQueue, RequestQueue>();
            serviceCollection.AddTransient<IModbusTcpClientWrapper, ModbusTcpClientWrapper>();
            serviceCollection.AddTransient<IModbusTcpClientProxy, ModbusTcpClientProxy>();

            return serviceCollection;
        }
    }
}