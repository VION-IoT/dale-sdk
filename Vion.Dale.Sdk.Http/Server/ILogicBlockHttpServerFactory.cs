using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <summary>
    ///     Factory for creating instances of <see cref="ILogicBlockHttpServer" />.
    /// </summary>
    [PublicApi]
    public interface ILogicBlockHttpServerFactory
    {
        /// <summary>
        ///     Creates a new, disabled <see cref="ILogicBlockHttpServer" />. Each instance hosts one server on its own port.
        /// </summary>
        /// <returns>A new <see cref="ILogicBlockHttpServer" /> instance.</returns>
        ILogicBlockHttpServer Create();
    }
}