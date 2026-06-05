using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;

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