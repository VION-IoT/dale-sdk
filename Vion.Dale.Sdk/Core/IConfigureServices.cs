using Microsoft.Extensions.DependencyInjection;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Plugin assemblies must contain an implementation of this interface. The host calls it at startup to add
    ///     plugin logic blocks and services to DI.
    /// </summary>
    /// <remarks>
    ///     Register each logic block by its own concrete type — that registration is what makes the block
    ///     discoverable; its <em>lifetime</em> is not consulted, because a block is constructed for its
    ///     actor rather than resolved, so every actor gets its own instance whatever lifetime is declared.
    ///     The lifetime that does matter is that of the services a block injects: each block resolves them
    ///     from a scope of its own that is disposed when its actor stops, so a transient or scoped
    ///     registration yields one instance per block and reclaims it, while a singleton is shared by every
    ///     block and outlives all of them.
    /// </remarks>
    [PublicApi]
    public interface IConfigureServices
    {
        /// <summary>Registers this plugin's logic blocks and the services they inject.</summary>
        void ConfigureServices(IServiceCollection serviceCollection);
    }
}