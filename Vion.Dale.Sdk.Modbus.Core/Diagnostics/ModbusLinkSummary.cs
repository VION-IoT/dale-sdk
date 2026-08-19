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
    ///         be published as one <c>[ServiceProperty]</c>, and every field carries a title and a description so it
    ///         reads without a projection.
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
        [StructField(Title = "Link state", Description = "Verdict of the last transaction that reached the wire; locally decided outcomes leave it unchanged.")]
        ModbusLinkState State,
        [StructField(Title = "Last contact", Description = "When the device last answered, with data or with a Modbus exception code (UTC).")]
        DateTime? LastContactAt,
        [StructField(Title = "Last failure", Description = "When the last non-successful outcome was recorded, local ones included (UTC).")]
        DateTime? LastFailureAt,
        [StructField(Title = "Last failure outcome", Description = "How that last non-successful transaction ended.")]
        ModbusOutcome? LastFailureOutcome,
        [StructField(Title = "Successes", Description = "The device answered with the requested data.")]
        long SuccessCount,
        [StructField(Title = "Device errors", Description = "The device answered with a Modbus exception code — the link is up, the request was wrong.")]
        long DeviceErrorCount,
        [StructField(Title = "Timeouts", Description = "No answer arrived in time.")]
        long TimeoutCount,
        [StructField(Title = "Transport errors", Description = "The socket or serial stream failed.")]
        long TransportErrorCount,
        [StructField(Title = "Protocol errors", Description = "An answer arrived but was not a valid response for the request.")]
        long ProtocolErrorCount,
        [StructField(Title = "Backed off", Description = "Not attempted: the client was waiting out a connect backoff.")]
        long BackedOffCount,
        [StructField(Title = "Expired", Description = "Not attempted: the request aged out of the queue before dispatch.")]
        long ExpiredCount,
        [StructField(Title = "Dropped", Description = "Not attempted: the request was evicted because the queue was full.")]
        long DroppedCount,
        [StructField(Title = "Round trip (last)", Description = "Dispatch-to-response time of the last transaction that reached the wire.")]
        TimeSpan? LastRoundTrip,
        [StructField(Title = "Round trip (min)", Description = "The shortest dispatch-to-response time seen.")]
        TimeSpan? MinRoundTrip,
        [StructField(Title = "Round trip (max)", Description = "The longest dispatch-to-response time seen.")]
        TimeSpan? MaxRoundTrip,
        [StructField(Title = "Queued wait (last)", Description = "How long the last transaction waited locally before dispatch.")]
        TimeSpan? LastQueuedWait,
        [StructField(Title = "Queued wait (max)", Description = "The longest local wait before dispatch seen.")]
        TimeSpan? MaxQueuedWait,
        [StructField(Title = "Queue depth", Description = "Requests waiting to be dispatched right now. TCP: requests waiting; RTU: always 0.")]
        int QueueDepth);
}