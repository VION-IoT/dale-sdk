using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Http;
using Vion.Examples.Http.LogicBlocks;

namespace Vion.Examples.Http
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            // The HTTP SDK belongs here rather than only in the DevHost, because both blocks take their client and
            // server factory through the constructor and every host resolves them from this one composition root —
            // including the LogicBlockParser, which activates each block to emit the introspection JSON at pack time.
            // Registering it only in the DevHost builds, runs and tests clean, then fails `dale pack` and `dale upload`.
            // It also registers the system clock the debug client measures latency on, unless a host registered one.
            services.AddDaleHttpSdk();

            services.AddTransient<HttpDebugClient>();
            services.AddTransient<HttpSimServer>();
        }
    }
}