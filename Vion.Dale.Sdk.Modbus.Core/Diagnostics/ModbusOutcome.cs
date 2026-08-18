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
        Success,

        /// <summary>The device answered with a Modbus exception code — the link is up, the request was wrong.</summary>
        DeviceError,

        /// <summary>No answer arrived in time: an operation timeout, a connect timeout, or an expiry sweep.</summary>
        Timeout,

        /// <summary>The socket or serial stream failed.</summary>
        TransportError,

        /// <summary>An answer arrived but was not a valid response for the request.</summary>
        ProtocolError,

        /// <summary>Not attempted: the client is waiting out a connect backoff.</summary>
        BackedOff,

        /// <summary>Not attempted: the request aged past <see cref="Client.IModbusClient.MaxQueuedAge" /> before dispatch.</summary>
        Expired,

        /// <summary>Not attempted: the request was evicted because the queue was full.</summary>
        Dropped,

        /// <summary>Not attempted: the call itself was invalid — a bad unit id, a missing address, an unsupported conversion.</summary>
        Invalid,

        /// <summary>Not attempted, or abandoned: the client was disposed or the queue was cancelled.</summary>
        Cancelled,
    }
}