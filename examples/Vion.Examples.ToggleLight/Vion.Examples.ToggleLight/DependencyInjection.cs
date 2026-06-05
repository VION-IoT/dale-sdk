using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Examples.ToggleLight.LogicBlocks;

namespace Vion.Examples.ToggleLight
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<Toggle>();
            services.AddTransient<Light>();
        }
    }
}