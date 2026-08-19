using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Diagnostics
{
    /// <summary>
    ///     How a single Modbus read or write ended.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The first five values are <b>link verdicts</b>: the request reached the wire (or should have), so the
    ///         outcome says something about the device and the connection to it. The remaining values are decided
    ///         locally — the request never reached the device — and therefore say nothing about link health.
    ///     </para>
    ///     <para>
    ///         This split is what lets a block distinguish a backed-up local queue from a broken device or network:
    ///         only the link verdicts move <see cref="ModbusLinkSummary.State" />.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public enum ModbusOutcome
    {
        /// <summary>The device answered with the requested data.</summary>
        [EnumLabel("Success")]
        [Severity(StatusSeverity.Success)]
        Success,

        /// <summary>The device answered with a Modbus exception code — the link is up, the request was wrong.</summary>
        [EnumLabel("Device error")]
        [Severity(StatusSeverity.Warning)]
        DeviceError,

        /// <summary>No answer arrived in time: an operation timeout, a connect timeout, or an expiry sweep.</summary>
        [EnumLabel("Timeout")]
        [Severity(StatusSeverity.Error)]
        Timeout,

        /// <summary>The socket or serial stream failed.</summary>
        [EnumLabel("Transport error")]
        [Severity(StatusSeverity.Error)]
        TransportError,

        /// <summary>An answer arrived but was not a valid response for the request.</summary>
        [EnumLabel("Protocol error")]
        [Severity(StatusSeverity.Error)]
        ProtocolError,

        /// <summary>Not attempted: the client is waiting out a connect backoff.</summary>
        [EnumLabel("Backed off")]
        [Severity(StatusSeverity.Warning)]
        BackedOff,

        /// <summary>Not attempted: the request aged past <see cref="Client.IModbusClient.MaxQueuedAge" /> before dispatch.</summary>
        [EnumLabel("Expired")]
        [Severity(StatusSeverity.Warning)]
        Expired,

        /// <summary>Not attempted: the request was evicted because the queue was full.</summary>
        [EnumLabel("Dropped")]
        [Severity(StatusSeverity.Warning)]
        Dropped,

        /// <summary>Not attempted: the call itself was invalid — a bad unit id, a missing address, an unsupported conversion.</summary>
        [EnumLabel("Invalid")]
        [Severity(StatusSeverity.Warning)]
        Invalid,

        /// <summary>Not attempted, or abandoned: the client was disposed or the queue was cancelled.</summary>
        [EnumLabel("Cancelled")]
        [Severity(StatusSeverity.Neutral)]
        Cancelled,
    }
}