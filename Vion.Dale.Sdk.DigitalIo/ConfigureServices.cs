using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.DigitalIo
{
    /// <summary>
    ///     Registers the digital I/O handlers with the Dale runtime's service container.
    ///     Discovered by the runtime via reflection at plugin load time — consumers do not call this directly.
    /// </summary>
    public class DependencyInjection : IConfigureServices
    {
        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<DigitalInputHandler>();
            serviceCollection.AddTransient<DigitalOutputHandler>();
        }
    }
}