using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Registers the M-Bus handler with the Dale runtime's service container.
    ///     Discovered by the runtime via reflection at plugin load time — consumers do not call this directly.
    /// </summary>
    internal sealed class DependencyInjection : IConfigureServices
    {
        /// <inheritdoc />
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<MBusHandler>();
        }
    }
}