using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Examples.Emission.LogicBlocks;

namespace Vion.Examples.Emission
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<SensorBlock>();
        }
    }
}