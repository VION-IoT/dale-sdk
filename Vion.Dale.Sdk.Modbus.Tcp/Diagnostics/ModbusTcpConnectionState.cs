using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Diagnostics
{
    /// <summary>
    ///     The state of a Modbus TCP client's socket.
    /// </summary>
    [PublicApi]
    public enum ModbusTcpConnectionState
    {
        /// <summary>No socket: never connected, closed, or closed again after a fault.</summary>
        Disconnected,

        /// <summary>A socket is open and no reconnect is pending.</summary>
        Connected,

        /// <summary>The client is waiting out a backoff before attempting to connect again.</summary>
        BackingOff,
    }
}