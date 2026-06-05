using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;
using Vion.Diagnostics.LogicBlocks;

namespace Vion.Diagnostics
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<DiagnosticsCollector>();
        }
    }
}
