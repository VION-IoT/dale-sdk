using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Tcp;
using Vion.Examples.ModbusTcp.LogicBlocks;

namespace Vion.Examples.ModbusTcp
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // The Modbus TCP SDK belongs here rather than only in the DevHost, because both blocks take
            // their client and server through the constructor and every host resolves them from this one
            // composition root — including the LogicBlockParser, which activates each block to emit the
            // introspection JSON at pack time. Registering it only in the DevHost builds, runs and tests
            // clean, then fails `dale pack` and `dale upload`. Unlike Modbus.Rtu, the TCP SDK ships no
            // IConfigureServices of its own, so nothing registers it implicitly.
            services.AddDaleModbusTcpSdk();

            services.AddTransient<ModbusTcpDebugClient>();
            services.AddTransient<ModbusTcpSimServer>();
        }
    }
}