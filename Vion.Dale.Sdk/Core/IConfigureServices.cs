using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Plugin assemblies must contain an implementation of this interface. The host calls it at startup do add plugin
    ///     logic blocks and services to DI
    /// </summary>
    [PublicApi]
    public interface IConfigureServices
    {
        /// <summary>
        ///     Register all logic blocks and services to usable with dependency injection.
        ///     Logic blocks should be registered as transient.
        ///     Services that are injected into logic blocks should usually be registered as transient as well.
        /// </summary>
        void ConfigureServices(IServiceCollection serviceCollection);
    }
}