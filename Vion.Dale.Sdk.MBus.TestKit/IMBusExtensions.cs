using System;
using System.Linq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.MBus.TestKit
{
    /// <summary>
    ///     Extension methods on <see cref="IMBus" /> for answering a logic block's pending M-Bus read in a test.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         A simulation answers the read the block has pending and completes it, because a binding has one read in
    ///         flight at a time; a second simulation with no read pending is refused. Every answer is stamped from the
    ///         test context's virtual clock, and the callbacks are dispatched the way production dispatches them —
    ///         call <c>LogicBlockTestContext.FlushPendingActions()</c> to run them.
    ///     </para>
    ///     <para>
    ///         A capture from a log or a datasheet is replayed as hexadecimal text, one string per telegram, and runs
    ///         through the frame checks and the decoder a gateway runs.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public static class IMBusExtensions
    {
        /// <summary>
        ///     Answers the pending read with the given telegrams, through the frame checks and the decoder.
        /// </summary>
        /// <param name="mBus">The M-Bus contract instance.</param>
        /// <param name="testContext">The test context the read was recorded in.</param>
        /// <param name="telegramsHex">Each telegram as hexadecimal text, from its start byte to its stop byte.</param>
        /// <exception cref="InvalidOperationException">The block has no read pending.</exception>
        /// <exception cref="FormatException">A telegram is not hexadecimal text.</exception>
        public static void SimulateReadResponse<T>(this IMBus mBus, LogicBlockTestContext<T> testContext, params string[] telegramsHex)
            where T : LogicBlockBase
        {
            var contract = CastToImplementation(mBus);
            var request = FindPendingRead(testContext, contract, nameof(SimulateReadResponse));
            var telegrams = telegramsHex.Select(MBusHex.Parse).ToArray();
            Answer(testContext,
                   contract,
                   request,
                   telegrams,
                   null,
                   MBusOutcome.Success);
        }

        /// <summary>
        ///     Completes the pending read through its error callback with <paramref name="outcome" />.
        /// </summary>
        /// <param name="mBus">The M-Bus contract instance.</param>
        /// <param name="testContext">The test context the read was recorded in.</param>
        /// <param name="outcome">How the read failed; any outcome but <see cref="MBusOutcome.Success" />.</param>
        /// <exception cref="ArgumentException"><paramref name="outcome" /> is <see cref="MBusOutcome.Success" />.</exception>
        /// <exception cref="InvalidOperationException">The block has no read pending.</exception>
        public static void SimulateReadError<T>(this IMBus mBus, LogicBlockTestContext<T> testContext, MBusOutcome outcome)
            where T : LogicBlockBase
        {
            if (outcome == MBusOutcome.Success)
            {
                throw new ArgumentException($"A read succeeds with telegrams; use {nameof(SimulateReadResponse)} to answer it.", nameof(outcome));
            }

            var contract = CastToImplementation(mBus);
            var request = FindPendingRead(testContext, contract, nameof(SimulateReadError));
            var exception = new MBusException(outcome, $"The read failed with {outcome}, as the test simulated.");
            Answer(testContext,
                   contract,
                   request,
                   null,
                   exception,
                   outcome);
        }

        private static void Answer<T>(LogicBlockTestContext<T> testContext,
                                      MBusContract contract,
                                      ReadMBusRequest request,
                                      byte[][]? telegrams,
                                      MBusException? exception,
                                      MBusOutcome outcome)
            where T : LogicBlockBase
        {
            // A read the handler decides on without publishing carries no publish instant, so its whole elapsed time is
            // queued wait, as the handler stamps it.
            var publishedAt = outcome is MBusOutcome.Expired or MBusOutcome.Dropped or MBusOutcome.Invalid ? (DateTime?)null : request.CreatedAt;
            var response = new ReadMBusResponse(telegrams,
                                                exception,
                                                request.Callback,
                                                request.CorrelationId,
                                                request.CreatedAt,
                                                publishedAt,
                                                testContext.TimeProvider.GetUtcNow().UtcDateTime,
                                                testContext.TimeProvider.GetTimestamp(),
                                                outcome);
            contract.HandleContractMessage(new ContractMessage<ReadMBusResponse>(new LogicBlockContractId(Constants.LogicBlockId, contract.Identifier), response));
        }

        private static MBusContract CastToImplementation(IMBus mBus)
        {
            return mBus as MBusContract ?? throw new InvalidOperationException("Unable to simulate an answer on this IMBus instance: it is not the SDK's M-Bus contract.");
        }

        private static ReadMBusRequest FindPendingRead<T>(LogicBlockTestContext<T> testContext, MBusContract contract, string simulation)
            where T : LogicBlockBase
        {
            // Only the read holding the slot is answerable: an earlier one has completed, and answering it again would
            // run its callbacks twice.
            var pending = contract.PendingCorrelationId;
            var request = testContext.GetContractMessages<ReadMBusRequest>(contract.Identifier)
                                     .Select(message => (ReadMBusRequest?)message.Data)
                                     .LastOrDefault(data => pending != null && data!.Value.CorrelationId == pending);

            return request ??
                   throw new InvalidOperationException($"{simulation} found no pending M-Bus read on '{contract.Identifier}'. Enable the contract and have the logic block call " +
                                                       "Read before simulating its answer; a simulation completes the read, so each read is answered once.");
        }
    }
}