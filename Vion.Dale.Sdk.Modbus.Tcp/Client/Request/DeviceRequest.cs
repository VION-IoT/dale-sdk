using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     A request that talks to a device: it carries a <see cref="ModbusReceipt" /> to both callbacks, feeds the
    ///     client's link accumulator, and is subject to the queued-age check.
    /// </summary>
    /// <remarks>
    ///     The receipt's instants are taken here, on the queue consumer, the moment the operation returns or throws —
    ///     before the callback is handed to the block's mailbox — so a block that is slow to drain its mailbox does not
    ///     move the timestamps of the values it eventually reads.
    /// </remarks>
    internal abstract class DeviceRequest : Request
    {
        private readonly ModbusLinkAccumulator _accumulator;

        private readonly long _enqueuedAt;

        private readonly Action<Exception, ModbusReceipt>? _errorCallback;

        private readonly TimeProvider _timeProvider;

        protected DeviceRequest(string requestName,
                                IActorDispatcher dispatcher,
                                Action<Exception, ModbusReceipt>? errorCallback,
                                TimeProvider timeProvider,
                                ModbusLinkAccumulator accumulator,
                                ILogger logger) : base(requestName, dispatcher, logger)
        {
            _errorCallback = errorCallback;
            _timeProvider = timeProvider;
            _accumulator = accumulator;
            _enqueuedAt = timeProvider.GetTimestamp();
        }

        /// <inheritdoc />
        public sealed override Task ExecuteAsync(CancellationToken cancellationToken, TimeSpan? maxQueuedAge)
        {
            var queuedWait = _timeProvider.GetElapsedTime(_enqueuedAt);
            if (maxQueuedAge is { } limit && queuedWait > limit)
            {
                Complete(BuildReceipt(TimeSpan.Zero, queuedWait, ModbusOutcome.Expired), new RequestExpiredException(Name, queuedWait, limit));

                return Task.CompletedTask;
            }

            return ExecuteCoreAsync(cancellationToken, queuedWait);
        }

        /// <inheritdoc />
        public sealed override void HandleRequestFailed(Exception exception)
        {
            // Reached from the queue's drop callback on the enqueueing thread: nothing was dispatched, so the
            // round trip is zero and the queued wait is however long the request sat before it was evicted.
            var queuedWait = _timeProvider.GetElapsedTime(_enqueuedAt);
            Complete(BuildReceipt(TimeSpan.Zero, queuedWait, ModbusOutcomeClassifier.Classify(exception)), exception);
        }

        protected abstract Task ExecuteCoreAsync(CancellationToken cancellationToken, TimeSpan queuedWait);

        protected long StartOperation()
        {
            return _timeProvider.GetTimestamp();
        }

        protected ModbusReceipt CompleteSuccessfully(long startedAt, TimeSpan queuedWait)
        {
            var receipt = BuildReceipt(_timeProvider.GetElapsedTime(startedAt), queuedWait, ModbusOutcome.Success);
            _accumulator.Record(receipt);
            LogRequestSucceeded(Name, Id);

            return receipt;
        }

        protected void HandleOperationFailed(Exception exception, long startedAt, TimeSpan queuedWait)
        {
            Complete(BuildReceipt(_timeProvider.GetElapsedTime(startedAt), queuedWait, ModbusOutcomeClassifier.Classify(exception)), exception);
        }

        private ModbusReceipt BuildReceipt(TimeSpan roundTrip, TimeSpan queuedWait, ModbusOutcome outcome)
        {
            return new ModbusReceipt(_timeProvider.GetUtcNow().UtcDateTime, _timeProvider.GetTimestamp(), roundTrip, queuedWait, outcome);
        }

        private void Complete(ModbusReceipt receipt, Exception exception)
        {
            if (receipt.Outcome is ModbusOutcome.BackedOff or ModbusOutcome.Expired or ModbusOutcome.Dropped)
            {
                LogRequestNotExecuted(Name, receipt.Outcome, Id, exception);
            }
            else
            {
                LogRequestFailed(Name, Id, exception);
            }

            _accumulator.Record(receipt);
            if (_errorCallback == null)
            {
                return;
            }

            TryInvokeCallback(() => _errorCallback(exception, receipt));
        }
    }
}