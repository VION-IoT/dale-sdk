using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     The per-read facts of one M-Bus read, handed to the success and the error callback.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         The instants are taken in the shared handler the moment the answer or failure was observed, before the
    ///         callback reaches the logic block's mailbox, so a receipt is usable as the age of the values it accompanies.
    ///         Publish <c>ReceivedAt</c>; age a value with <c>TimeProvider.GetElapsedTime(receipt.ReceivedTimestamp)</c>,
    ///         which a corrected wall clock cannot move.
    ///     </para>
    ///     <para>
    ///         <c>QueuedWait</c> is the hop from the logic block to the shared handler. <c>RoundTrip</c> runs from the
    ///         handler's publish to the answer, so it includes the time the read waited in the service provider's line
    ///         queue behind other meters.
    ///     </para>
    /// </remarks>
    /// <param name="ReceivedAt">The UTC wall clock instant the answer or failure was observed.</param>
    /// <param name="ReceivedTimestamp">The same instant on the monotonic timestamp scale, for ageing the values.</param>
    /// <param name="RoundTrip">The time from publishing the read to observing the answer, the provider's line queue included.</param>
    /// <param name="QueuedWait">The time the read took to reach the shared handler.</param>
    /// <param name="Outcome">How the read ended.</param>
    [PublicApi]
    public readonly record struct MBusReceipt(DateTime ReceivedAt, long ReceivedTimestamp, TimeSpan RoundTrip, TimeSpan QueuedWait, MBusOutcome Outcome);
}
