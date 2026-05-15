using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock
{
    /// <summary>
    ///     Factory for creating instances of <see cref="ILogicBlockModbusTcpClient" />.
    /// </summary>
    [PublicApi]
    public interface ILogicBlockModbusTcpClientFactory
    {
        /// <summary>
        ///     Creates a new instance of <see cref="ILogicBlockModbusTcpClient" />.
        /// </summary>
        /// <returns>A new <see cref="ILogicBlockModbusTcpClient" /> instance.</returns>
        /// <remarks>
        ///     This can be used when a single injected client instance is insufficient.
        ///     For example, connecting to multiple different Modbus servers/slaves within one logic block, or creating dedicated clients for specific operations.
        ///     Since each client uses one TCP connection and does not allow concurrent operations, a high-priority write could be blocked by low-priority reads or writes
        ///     that are being processed, as operations are executed sequentially.
        ///     To resolve this, one or more dedicated clients can be created for specific high-priority operations where immediate execution is critical.
        /// </remarks>
        ILogicBlockModbusTcpClient Create();
    }
}