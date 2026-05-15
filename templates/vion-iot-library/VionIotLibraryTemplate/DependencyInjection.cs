using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.DependencyInjection;

namespace VionIotLibraryTemplate
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection services)
        {
            services.AddTransient<HelloWorld>();
            services.AddTransient<SmartLedController>();
        }
    }
}