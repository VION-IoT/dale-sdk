using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock;

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
            serviceCollection.AddSingleton<ILogicBlockModbusTcpServerFactory, LogicBlockModbusTcpServerFactory>();
            serviceCollection.AddTransient<ILogicBlockModbusTcpServer, LogicBlockModbusTcpServer>();
            serviceCollection.AddTransient<IModbusTcpServerProxy, ModbusTcpServerProxy>();

            // The server proxy timestamps client writes via TimeProvider. The full SDK registers it too
            // (AddDaleSdk); TryAdd keeps that and any test-supplied FakeTimeProvider authoritative.
            serviceCollection.TryAddSingleton(TimeProvider.System);

            return serviceCollection;
        }
    }
}