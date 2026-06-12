using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock
{
    /// <summary>
    ///     Factory for creating instances of <see cref="ILogicBlockModbusTcpServer" />.
    /// </summary>
    [PublicApi]
    public interface ILogicBlockModbusTcpServerFactory
    {
        /// <summary>
        ///     Creates a new instance of <see cref="ILogicBlockModbusTcpServer" />.
        /// </summary>
        /// <returns>A new <see cref="ILogicBlockModbusTcpServer" /> instance.</returns>
        /// <remarks>
        ///     Each instance hosts one server on its own port. A logic block that serves several register maps
        ///     creates several instances — the multi-server analog of the client factory's multi-client story.
        /// </remarks>
        ILogicBlockModbusTcpServer Create();
    }
}