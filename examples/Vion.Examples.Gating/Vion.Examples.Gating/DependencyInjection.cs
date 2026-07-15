using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Examples.Gating.LogicBlocks;

namespace Vion.Examples.Gating
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<ChargingStationBlock>();
            services.AddTransient<ChargeController>();
        }
    }
}