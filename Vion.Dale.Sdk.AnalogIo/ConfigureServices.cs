using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.AnalogIo
{
    /// <summary>
    ///     Registers the analog I/O handlers with the Dale runtime's service container.
    ///     Discovered by the runtime via reflection at plugin load time — consumers do not call this directly.
    /// </summary>
    public class DependencyInjection : IConfigureServices
    {
        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<AnalogInputHandler>();
            serviceCollection.AddTransient<AnalogOutputHandler>();
        }
    }
}