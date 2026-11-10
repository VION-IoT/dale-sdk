using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     A read, sent by an M-Bus contract to the shared handler.
    /// </summary>
    /// <param name="PrimaryAddress">The meter's primary address.</param>
    /// <param name="MaxTelegrams">How many telegrams the meter sends.</param>
    /// <param name="CreatedAt">The UTC time the logic block issued the read.</param>
    /// <param name="OperationTimeout">How long the provider has to answer once the handler has published the read.</param>
    /// <param name="MaxQueuedAge">How stale the read may be when the handler takes it, or <c>null</c> for no limit.</param>
    /// <param name="CorrelationId">Matches the provider's answer to this read.</param>
    /// <param name="Callback">The contract's completion, run on the logic block's actor with the answer.</param>
    [InternalApi]
    public readonly record struct ReadMBusRequest(
        byte PrimaryAddress,
        int MaxTelegrams,
        DateTime CreatedAt,
        TimeSpan OperationTimeout,
        TimeSpan? MaxQueuedAge,
        Guid CorrelationId,
        Action<byte[][]?, MBusException?, MBusReceipt> Callback);

    /// <summary>
    ///     The shared handler's answer to a <see cref="ReadMBusRequest" />: the telegrams as received, or the failure.
    /// </summary>
    /// <param name="Telegrams">The frames the provider returned, or <c>null</c> on a failure.</param>
    /// <param name="Exception">The failure, or <c>null</c> when the provider answered <c>Ok</c>.</param>
    /// <param name="Callback">The contract's completion, carried back from the request.</param>
    /// <param name="CorrelationId">The read's correlation identifier.</param>
    /// <param name="CreatedAt">The UTC time the logic block issued the read.</param>
    /// <param name="PublishedAt">The UTC time the handler published the read, or <c>null</c> if it never did.</param>
    /// <param name="ReceivedAt">The UTC time the handler observed the answer or failure.</param>
    /// <param name="ReceivedTimestamp">The same instant on the monotonic timestamp scale.</param>
    /// <param name="Outcome">The handler's classification; the contract corrects <c>Success</c> when a telegram fails its checks.</param>
    [InternalApi]
    public readonly record struct ReadMBusResponse(
        byte[][]? Telegrams,
        MBusException? Exception,
        Action<byte[][]?, MBusException?, MBusReceipt> Callback,
        Guid CorrelationId,
        DateTime CreatedAt,
        DateTime? PublishedAt,
        DateTime ReceivedAt,
        long ReceivedTimestamp,
        MBusOutcome Outcome)
    {
        /// <summary>Builds the receipt handed to the logic block's callback.</summary>
        public MBusReceipt ToReceipt()
        {
            // A published read splits at the publish: the hop to the handler is the queued wait, the rest the round
            // trip. A read that never reached MQTT spent all of its time before the publish.
            var queuedWait = Clamp((PublishedAt ?? ReceivedAt) - CreatedAt);
            var roundTrip = PublishedAt is { } published ? Clamp(ReceivedAt - published) : TimeSpan.Zero;

            return new MBusReceipt(ReceivedAt, ReceivedTimestamp, roundTrip, queuedWait, Outcome);
        }

        private static TimeSpan Clamp(TimeSpan value)
        {
            return value < TimeSpan.Zero ? TimeSpan.Zero : value;
        }
    }
}
