using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.AnalogIo
{
    public class DependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<AnalogInputHandler>();
            serviceCollection.AddTransient<AnalogOutputHandler>();
        }
    }
}