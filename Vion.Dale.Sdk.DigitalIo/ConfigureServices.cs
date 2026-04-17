using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.DigitalIo
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<DigitalInputHandler>();
            serviceCollection.AddTransient<DigitalOutputHandler>();
        }
    }
}