using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Examples.MBus.LogicBlocks;

namespace Vion.Examples.MBus
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<Em5xxElectricityMeter>();
            services.AddTransient<MBusDebugClient>();
        }
    }
}
