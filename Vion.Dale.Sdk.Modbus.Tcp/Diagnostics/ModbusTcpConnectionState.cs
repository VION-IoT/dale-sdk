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
        [EnumLabel("Disconnected")]
        [Severity(StatusSeverity.Neutral)]
        Disconnected,

        /// <summary>A socket is open and no reconnect is pending.</summary>
        [EnumLabel("Connected")]
        [Severity(StatusSeverity.Success)]
        Connected,

        /// <summary>The client is waiting out a backoff before attempting to connect again.</summary>
        [EnumLabel("Backing off")]
        [Severity(StatusSeverity.Warning)]
        BackingOff,
    }
}