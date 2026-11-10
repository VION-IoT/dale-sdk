using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     A point-in-time summary of one M-Bus binding's link to its meter: the current verdict, when the meter was
    ///     last heard from, lifetime counts per outcome and of busy reads, and latencies over a recent window and since
    ///     the contract started.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read it whenever you want it: every read returns a consistent snapshot. Every field is a
    ///         service-property-legal type, so the whole summary can be published as one <c>[ServiceProperty]</c>; it
    ///         changes with every read, so a member publishing it whole sends at most once every 30 s by default.
    ///     </para>
    ///     <para>
    ///         <c>State</c> moves only on outcomes that say something about the meter: <c>Success</c> sets <c>Online</c>;
    ///         <c>NoResponse</c>, <c>Garbled</c>, <c>LineUnavailable</c>, <c>Timeout</c> and <c>ProtocolError</c> set
    ///         <c>Faulted</c>. <c>Expired</c>, <c>Dropped</c> and <c>Invalid</c> leave it alone. It does not decay with
    ///         time; build a freshness rule from <c>LastContactAt</c>.
    ///     </para>
    ///     <para>
    ///         A rising <c>BusyCount</c> means reads are issued faster than the line answers them: the binding's previous
    ///         read was still pending each time.
    ///     </para>
    ///     <para>
    ///         The <c>Recent</c> figures cover at least the last 15 minutes and less than 16 on the contract's clock, or
    ///         its whole life where that is shorter. Round trip is taken from reads the provider answered, queued wait
    ///         from reads that reached the shared handler; an empty window reads its mean and max as <c>null</c>.
    ///     </para>
    /// </remarks>
    /// <param name="State">The verdict of the last read that said something about the meter.</param>
    /// <param name="LastContactAt">When the meter last answered with telegrams that passed their checks.</param>
    /// <param name="LastFailureAt">When the last read that did not succeed completed.</param>
    /// <param name="LastFailureOutcome">The outcome recorded at <paramref name="LastFailureAt" />.</param>
    /// <param name="SuccessCount">Reads the meter answered with valid telegrams.</param>
    /// <param name="NoResponseCount">Reads the meter did not answer.</param>
    /// <param name="GarbledCount">Reads the provider saw garbled on the line.</param>
    /// <param name="LineUnavailableCount">Reads the provider could not put on its line.</param>
    /// <param name="TimeoutCount">Reads no answer arrived for in time.</param>
    /// <param name="ProtocolErrorCount">Reads whose telegrams failed their checks or could not be decoded.</param>
    /// <param name="ExpiredCount">Reads that aged past <see cref="IMBus.MaxQueuedAge" /> before they were published.</param>
    /// <param name="DroppedCount">Reads dropped at the runtime-wide limit of pending reads.</param>
    /// <param name="InvalidCount">Reads refused for their arguments or mapping, or not understood by the provider.</param>
    /// <param name="BusyCount">Reads dropped because the binding's previous read was still pending.</param>
    /// <param name="RecentRoundTripCount">Reads the provider answered in the last 15 minutes.</param>
    /// <param name="RecentMeanRoundTrip">The mean publish-to-answer time over the last 15 minutes.</param>
    /// <param name="RecentMaxRoundTrip">The longest publish-to-answer time in the last 15 minutes.</param>
    /// <param name="MaxRoundTrip">The longest publish-to-answer time since the contract started.</param>
    /// <param name="MaxRoundTripAt">When the read that set <paramref name="MaxRoundTrip" /> was observed.</param>
    /// <param name="RecentQueuedWaitCount">Reads that reached the shared handler in the last 15 minutes.</param>
    /// <param name="RecentMeanQueuedWait">The mean hop to the shared handler over the last 15 minutes.</param>
    /// <param name="RecentMaxQueuedWait">The longest hop to the shared handler in the last 15 minutes.</param>
    /// <param name="MaxQueuedWait">The longest hop to the shared handler since the contract started.</param>
    /// <param name="MaxQueuedWaitAt">When the read that set <paramref name="MaxQueuedWait" /> was observed.</param>
    [PublicApi]
    [DefaultMinInterval("30s")]
    public readonly record struct MBusLinkSummary(
        [StructField(Title = "Link state", Description = "Verdict of the last read that said something about the meter.")]
        MBusLinkState State,
        [StructField(Title = "Last contact", Description = "When the meter last answered with telegrams that passed their checks (UTC).")]
        DateTime? LastContactAt,
        [StructField(Title = "Last failure", Description = "When the last read that did not succeed completed (UTC).")]
        DateTime? LastFailureAt,
        [StructField(Title = "Last failure outcome", Description = "How that last read ended.")]
        MBusOutcome? LastFailureOutcome,
        [StructField(Title = "Successes", Description = "The meter answered with telegrams that passed their checks.")]
        long SuccessCount,
        [StructField(Title = "No responses", Description = "The meter stayed silent through the provider's repetitions.")]
        long NoResponseCount,
        [StructField(Title = "Garbled", Description = "The provider saw a checksum, framing, length or collision fault on the line.")]
        long GarbledCount,
        [StructField(Title = "Line unavailable", Description = "The provider's line or terminal was not ready, or its queue was full.")]
        long LineUnavailableCount,
        [StructField(Title = "Timeouts", Description = "No answer arrived within the operation timeout.")]
        long TimeoutCount,
        [StructField(Title = "Protocol errors", Description = "A telegram failed its checks or could not be decoded.")]
        long ProtocolErrorCount,
        [StructField(Title = "Expired", Description = "Not attempted: the read aged out before the handler published it.")]
        long ExpiredCount,
        [StructField(Title = "Dropped", Description = "Not attempted: the runtime-wide limit of pending reads was reached.")]
        long DroppedCount,
        [StructField(Title = "Invalid", Description = "Refused for its arguments or mapping, or not understood by the provider.")]
        long InvalidCount,
        [StructField(Title = "Busy", Description = "Dropped because the binding's previous read was still pending.")]
        long BusyCount,
        [StructField(Title = "Round trips (15 min)", Description = "Reads the provider answered in the last 15 minutes, which the two round-trip figures beside it rest on.")]
        long RecentRoundTripCount,
        [StructField(Title = "Round trip (mean, 15 min)", Description = "Mean publish-to-answer time over the last 15 minutes, the provider's line queue included.")]
        TimeSpan? RecentMeanRoundTrip,
        [StructField(Title = "Round trip (max, 15 min)", Description = "Longest publish-to-answer time in the last 15 minutes.")]
        TimeSpan? RecentMaxRoundTrip,
        [StructField(Title = "Round trip (max since start)", Description = "The longest publish-to-answer time since the contract started.")]
        TimeSpan? MaxRoundTrip,
        [StructField(Title = "Round trip (max since start) at", Description = "When the longest round trip since the contract started was observed (UTC).")]
        DateTime? MaxRoundTripAt,
        [StructField(Title = "Queued waits (15 min)",
                     Description = "Reads that reached the shared handler in the last 15 minutes, which the two queued-wait figures beside it rest on.")]
        long RecentQueuedWaitCount,
        [StructField(Title = "Queued wait (mean, 15 min)", Description = "Mean hop to the shared handler over the last 15 minutes.")]
        TimeSpan? RecentMeanQueuedWait,
        [StructField(Title = "Queued wait (max, 15 min)", Description = "Longest hop to the shared handler in the last 15 minutes.")]
        TimeSpan? RecentMaxQueuedWait,
        [StructField(Title = "Queued wait (max since start)", Description = "The longest hop to the shared handler since the contract started.")]
        TimeSpan? MaxQueuedWait,
        [StructField(Title = "Queued wait (max since start) at", Description = "When the longest queued wait since the contract started was observed (UTC).")]
        DateTime? MaxQueuedWaitAt);
}