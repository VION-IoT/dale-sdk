using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Diagnostics
{
    /// <summary>
    ///     A point-in-time summary of one Modbus client's link to its device: the current verdict, when the device was
    ///     last heard from, and lifetime counts and latencies per outcome.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read it whenever you want it — every read returns a consistent snapshot, taken without blocking the
    ///         transaction that is updating it. Every field is a service-property-legal type, so the whole summary can
    ///         be published as one <c>[ServiceProperty]</c>.
    ///     </para>
    ///     <para>
    ///         <c>State</c> moves only on outcomes that reached the wire: <c>Success</c> and <c>DeviceError</c> set
    ///         <c>Online</c>; <c>Timeout</c>, <c>TransportError</c> and <c>ProtocolError</c> set <c>Faulted</c>. Locally
    ///         decided outcomes — <c>Expired</c>, <c>Dropped</c>, <c>BackedOff</c>, <c>Invalid</c>, <c>Cancelled</c> —
    ///         leave it alone: a full queue or a bad unit id is not evidence about the device. They are still counted,
    ///         and still set <c>LastFailureAt</c> / <c>LastFailureOutcome</c>.
    ///     </para>
    ///     <para>
    ///         <c>State</c> does not decay with time. A client that is polled once an hour and answered an hour ago is
    ///         still <c>Online</c> here, because only the caller knows its own poll cadence. Build a freshness rule from
    ///         <c>LastContactAt</c>, or from the monotonic stamp on the receipt of the value you care about.
    ///     </para>
    ///     <para>
    ///         Counts and extremes are for the lifetime of the client instance and are never reset.
    ///     </para>
    /// </remarks>
    /// <param name="State">The verdict of the last transaction that reached the wire.</param>
    /// <param name="LastContactAt">When the device last answered, with data or with a Modbus exception code.</param>
    /// <param name="LastFailureAt">When the last non-successful outcome was recorded, local ones included.</param>
    /// <param name="LastFailureOutcome">The outcome recorded at <paramref name="LastFailureAt" />.</param>
    /// <param name="SuccessCount">Transactions the device answered with data.</param>
    /// <param name="DeviceErrorCount">Transactions the device answered with a Modbus exception code.</param>
    /// <param name="TimeoutCount">Transactions that got no answer in time.</param>
    /// <param name="TransportErrorCount">Transactions that failed on the socket or serial stream.</param>
    /// <param name="ProtocolErrorCount">Transactions whose answer was not a valid response for the request.</param>
    /// <param name="BackedOffCount">Transactions not attempted because the client was in connect backoff.</param>
    /// <param name="ExpiredCount">
    ///     Transactions that aged past <see cref="Client.IModbusClient.MaxQueuedAge" /> before
    ///     dispatch.
    /// </param>
    /// <param name="DroppedCount">Transactions evicted because the queue was full.</param>
    /// <param name="LastRoundTrip">The dispatch-to-response time of the last transaction that reached the wire.</param>
    /// <param name="MinRoundTrip">The shortest dispatch-to-response time seen.</param>
    /// <param name="MaxRoundTrip">The longest dispatch-to-response time seen.</param>
    /// <param name="LastQueuedWait">How long the last transaction waited locally before dispatch.</param>
    /// <param name="MaxQueuedWait">The longest local wait before dispatch seen.</param>
    /// <param name="QueueDepth">Requests waiting to be dispatched right now.</param>
    [PublicApi]
    public readonly record struct ModbusLinkSummary(
        ModbusLinkState State,
        DateTime? LastContactAt,
        DateTime? LastFailureAt,
        ModbusOutcome? LastFailureOutcome,
        long SuccessCount,
        long DeviceErrorCount,
        long TimeoutCount,
        long TransportErrorCount,
        long ProtocolErrorCount,
        long BackedOffCount,
        long ExpiredCount,
        long DroppedCount,
        TimeSpan? LastRoundTrip,
        TimeSpan? MinRoundTrip,
        TimeSpan? MaxRoundTrip,
        TimeSpan? LastQueuedWait,
        TimeSpan? MaxQueuedWait,
        int QueueDepth);
}