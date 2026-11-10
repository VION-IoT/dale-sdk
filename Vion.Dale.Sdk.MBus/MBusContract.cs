using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     The M-Bus contract a logic block binds: it sends reads to the shared handler, and checks and decodes the
    ///     telegrams that come back.
    /// </summary>
    [InternalApi]
    public partial class MBusContract : LogicBlockContractBase, IMBus
    {
        private const int MaxPrimaryAddress = 250;

        private static readonly TimeSpan MaxTimeout = TimeSpan.FromMilliseconds(int.MaxValue);

        private static readonly TimeSpan BackstopMargin = TimeSpan.FromSeconds(10);

        private readonly MBusLinkAccumulator _linkAccumulator;

        private readonly HashSet<MBusSelector> _loggedMisses = [];

        private readonly ILogger<MBusContract> _logger;

        private readonly TimeProvider _timeProvider;

        /// <inheritdoc />
        public override string ContractHandlerActorName { get; protected set; } = nameof(MBusHandler);

        /// <summary>
        ///     The correlation identifier of the read holding the slot, which the test kit answers; <c>null</c> when none is
        ///     pending.
        /// </summary>
        internal Guid? PendingCorrelationId { get; private set; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="MBusContract" /> class.
        /// </summary>
        /// <param name="identifier">The contract identifier as declared on the logic block.</param>
        /// <param name="actorContext">The actor context used to reach the shared handler.</param>
        /// <param name="timeProvider">Provides an abstraction for date and time operations.</param>
        /// <param name="logger">The logger for logging.</param>
        public MBusContract(string identifier, IActorContext actorContext, TimeProvider timeProvider, ILogger<MBusContract> logger) : base(identifier, actorContext)
        {
            _timeProvider = timeProvider;
            _linkAccumulator = new MBusLinkAccumulator(timeProvider);
            _logger = logger;
        }

        /// <inheritdoc />
        public bool IsEnabled
        {
            get;

            set
            {
                field = value;
                if (value)
                {
                    LogEnabled(LogicBlockContractId);
                }
                else
                {
                    LogDisabled(LogicBlockContractId);
                }
            }
        }

        /// <inheritdoc />
        public TimeSpan DefaultOperationTimeout
        {
            get;

            set
            {
                Validate(value, nameof(DefaultOperationTimeout));
                field = value;
            }
        } = TimeSpan.FromSeconds(300);

        /// <inheritdoc />
        public TimeSpan? MaxQueuedAge
        {
            get;

            set
            {
                if (value is { } age)
                {
                    Validate(age, nameof(MaxQueuedAge));
                }

                field = value;
            }
        } = TimeSpan.FromSeconds(30);

        /// <inheritdoc />
        public MBusLinkSummary Link
        {
            get => _linkAccumulator.Snapshot();
        }

        /// <inheritdoc />
        public void Read(int primaryAddress,
                         int maxTelegrams,
                         IActorDispatcher dispatcher,
                         Action<MBusReadout, MBusReceipt> successCallback,
                         Action<Exception, MBusReceipt>? errorCallback = null,
                         TimeSpan? operationTimeout = null)
        {
            if (!IsEnabled)
            {
                LogReadSkipped(LogicBlockContractId, primaryAddress);
                return;
            }

            // Checked before anything else, the arguments included: a busy read does nothing at all.
            if (PendingCorrelationId != null)
            {
                _linkAccumulator.RecordBusy();
                LogReadBusy(LogicBlockContractId, primaryAddress);
                return;
            }

            if (dispatcher == null)
            {
                throw new ArgumentNullException(nameof(dispatcher));
            }

            if (successCallback == null)
            {
                throw new ArgumentNullException(nameof(successCallback));
            }

            var timeout = operationTimeout ?? DefaultOperationTimeout;
            if (operationTimeout.HasValue)
            {
                Validate(timeout, nameof(operationTimeout));
            }

            if (primaryAddress is < 0 or > MaxPrimaryAddress || maxTelegrams < 1)
            {
                var reason = string.Format(CultureInfo.InvariantCulture,
                                           "An M-Bus read takes a primary address from 0 to {0} and at least one telegram; it was given address {1} and {2} telegrams.",
                                           MaxPrimaryAddress,
                                           primaryAddress,
                                           maxTelegrams);
                Refuse(new MBusException(MBusOutcome.Invalid, reason), primaryAddress, dispatcher, errorCallback);
                return;
            }

            var correlationId = Guid.NewGuid();
            var read = new PendingRead(correlationId,
                                       primaryAddress,
                                       maxTelegrams,
                                       dispatcher,
                                       successCallback,
                                       errorCallback);
            var request = new ReadMBusRequest((byte)primaryAddress,
                                              maxTelegrams,
                                              _timeProvider.GetUtcNow().UtcDateTime,
                                              timeout,
                                              MaxQueuedAge,
                                              correlationId,
                                              (telegrams, exception, receipt) => Complete(read, telegrams, exception, receipt));
            PendingCorrelationId = correlationId;
            try
            {
                LogSendingRead(LogicBlockContractId, primaryAddress, maxTelegrams, correlationId);
                SendToContractHandler(new ContractMessage<ReadMBusRequest>(LogicBlockContractId, request));
            }
            catch (Exception exception)
            {
                PendingCorrelationId = null;
                Refuse(new MBusException(MBusOutcome.Invalid, exception.Message), primaryAddress, dispatcher, errorCallback);
                return;
            }

            /*
             * The handler answers every read it receives, but an answer can still be lost: a contract the link map
             * never named has no actor the handler can reach, and an unbound contract's send goes nowhere. Without
             * this backstop such a read would hold the slot for good, and every later read would be dropped as busy.
             * It fires only after the handler's own timeout has had its chance: the hop's limit, the timeout, and
             * the margin of the handler's sweep.
             */
            var backstop = timeout + (MaxQueuedAge ?? TimeSpan.Zero) + BackstopMargin;
            var delay = backstop > MaxTimeout ? MaxTimeout : backstop;
            var startedAt = _timeProvider.GetTimestamp();
            dispatcher.InvokeSynchronizedAfter(() => Abandon(read, timeout, startedAt, delay), delay);
        }

        /// <inheritdoc />
        public override void HandleContractMessage(IContractMessage contractMessage)
        {
            if (contractMessage is ContractMessage<ReadMBusResponse> m)
            {
                m.Data.Callback(m.Data.Telegrams, m.Data.Exception, m.Data.ToReceipt());
            }
        }

        private void Complete(PendingRead read, byte[][]? telegrams, MBusException? exception, MBusReceipt receipt)
        {
            // A read completes once: an answer arriving after the backstop gave up on it is ignored.
            if (!TryFinish(read))
            {
                LogLateAnswerIgnored(LogicBlockContractId, read.CorrelationId);
                return;
            }

            if (exception != null)
            {
                Fail(exception, receipt, read.PrimaryAddress, read.Dispatcher, read.ErrorCallback);
                return;
            }

            MBusReadout readout;
            try
            {
                readout = MBusReadout.Create(telegrams ?? [], read.MaxTelegrams, read.PrimaryAddress);
            }
            catch (MBusDecodeException decodeException)
            {
                // The provider answered Ok, but the telegrams are not this meter's valid answer: the handler's Success
                // is corrected before it reaches the logic block.
                var protocolError = new MBusException(MBusOutcome.ProtocolError, decodeException.Message, "Ok", telegrams);
                Fail(protocolError, receipt with { Outcome = MBusOutcome.ProtocolError }, read.PrimaryAddress, read.Dispatcher, read.ErrorCallback);
                return;
            }

            _linkAccumulator.Record(receipt);
            readout.OnMiss = selector => LogMissOnce(selector, readout, read.PrimaryAddress);
            LogReadSucceeded(LogicBlockContractId, read.PrimaryAddress, readout.Telegrams.Count, readout.IsComplete);
            read.Dispatcher.InvokeSynchronized(() => read.SuccessCallback(readout, receipt));
        }

        private void Abandon(PendingRead read, TimeSpan timeout, long startedAt, TimeSpan delay)
        {
            if (read.Finished)
            {
                return;
            }

            // A dispatcher may run a delayed action early — a test kit draining its queue does — so the deadline is
            // read off this contract's own clock, and an early run waits out the rest.
            var remaining = delay - _timeProvider.GetElapsedTime(startedAt);
            if (remaining > TimeSpan.Zero)
            {
                read.Dispatcher.InvokeSynchronizedAfter(() => Abandon(read, timeout, startedAt, delay), remaining);
                return;
            }

            TryFinish(read);

            var exception = new MBusException(MBusOutcome.Timeout,
                                              string.Format(CultureInfo.InvariantCulture,
                                                            "No answer to the read reached the contract within its operation timeout of {0:0.###} s and the time " +
                                                            "it may take to reach the shared handler, so the read was abandoned.",
                                                            timeout.TotalSeconds));
            var receipt = new MBusReceipt(_timeProvider.GetUtcNow().UtcDateTime, _timeProvider.GetTimestamp(), TimeSpan.Zero, TimeSpan.Zero, MBusOutcome.Timeout);

            // Whether the read ever reached the handler is unknown, so its zero latencies feed no figure.
            Fail(exception,
                 receipt,
                 read.PrimaryAddress,
                 read.Dispatcher,
                 read.ErrorCallback,
                 false);
        }

        /// <summary>Marks the read finished and frees the slot it holds; <c>false</c> when it was already finished.</summary>
        private bool TryFinish(PendingRead read)
        {
            if (read.Finished)
            {
                return false;
            }

            read.Finished = true;

            // Only the read holding the slot frees it.
            if (PendingCorrelationId == read.CorrelationId)
            {
                PendingCorrelationId = null;
            }

            return true;
        }

        /// <summary>Completes a read that never left the block, through the path a response takes.</summary>
        private void Refuse(MBusException exception, int primaryAddress, IActorDispatcher dispatcher, Action<Exception, MBusReceipt>? errorCallback)
        {
            var receipt = new MBusReceipt(_timeProvider.GetUtcNow().UtcDateTime, _timeProvider.GetTimestamp(), TimeSpan.Zero, TimeSpan.Zero, MBusOutcome.Invalid);
            Fail(exception, receipt, primaryAddress, dispatcher, errorCallback);
        }

        private void Fail(MBusException exception,
                          MBusReceipt receipt,
                          int primaryAddress,
                          IActorDispatcher dispatcher,
                          Action<Exception, MBusReceipt>? errorCallback,
                          bool feedsFigures = true)
        {
            _linkAccumulator.Record(receipt, feedsFigures);
            if (receipt.Outcome is MBusOutcome.Expired or MBusOutcome.Dropped)
            {
                LogReadNotExecuted(LogicBlockContractId, primaryAddress, receipt.Outcome, exception);
            }
            else
            {
                LogReadFailed(LogicBlockContractId, primaryAddress, receipt.Outcome, exception);
            }

            if (errorCallback != null)
            {
                dispatcher.InvokeSynchronized(() => errorCallback(exception, receipt));
            }
        }

        private void LogMissOnce(MBusSelector selector, MBusReadout readout, int primaryAddress)
        {
            if (_loggedMisses.Add(selector))
            {
                LogSelectorMissed(LogicBlockContractId, selector.ToString(), readout.Header.Manufacturer, readout.Header.Identification, primaryAddress);
            }
        }

        private static void Validate(TimeSpan timeout, string subject)
        {
            if (timeout <= TimeSpan.Zero || timeout > MaxTimeout)
            {
                throw new ArgumentOutOfRangeException(subject,
                                                      timeout,
                                                      string.Format(CultureInfo.InvariantCulture, "{0} must be greater than zero and at most {1} ms.", subject, int.MaxValue));
            }
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "M-Bus contract enabled (LogicBlockContractId={LogicBlockContractId})")]
        private partial void LogEnabled(LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Information, Message = "M-Bus contract disabled (LogicBlockContractId={LogicBlockContractId})")]
        private partial void LogDisabled(LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Read skipped because the contract is disabled (LogicBlockContractId={LogicBlockContractId}, PrimaryAddress={PrimaryAddress})")]
        private partial void LogReadSkipped(LogicBlockContractId logicBlockContractId, int primaryAddress);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Read dropped because this binding's previous read is still pending (LogicBlockContractId={LogicBlockContractId}, PrimaryAddress={PrimaryAddress})")]
        private partial void LogReadBusy(LogicBlockContractId logicBlockContractId, int primaryAddress);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message =
                           "Sending read (LogicBlockContractId={LogicBlockContractId}, PrimaryAddress={PrimaryAddress}, MaxTelegrams={MaxTelegrams}, CorrelationId={CorrelationId})")]
        private partial void LogSendingRead(LogicBlockContractId logicBlockContractId, int primaryAddress, int maxTelegrams, Guid correlationId);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Read succeeded (LogicBlockContractId={LogicBlockContractId}, PrimaryAddress={PrimaryAddress}, Telegrams={Telegrams}, Complete={Complete})")]
        private partial void LogReadSucceeded(LogicBlockContractId logicBlockContractId, int primaryAddress, int telegrams, bool complete);

        [LoggerMessage(Level = LogLevel.Error, Message = "Read failed (LogicBlockContractId={LogicBlockContractId}, PrimaryAddress={PrimaryAddress}, Outcome={Outcome})")]
        private partial void LogReadFailed(LogicBlockContractId logicBlockContractId, int primaryAddress, MBusOutcome outcome, Exception exception);

        // Congestion is expected under load, and at Error one overload becomes a flood in the gateway's log
        // pipeline. The outcome on the receipt is what a logic block reacts to.
        [LoggerMessage(Level = LogLevel.Debug, Message = "Read was not executed (LogicBlockContractId={LogicBlockContractId}, PrimaryAddress={PrimaryAddress}, Outcome={Outcome})")]
        private partial void LogReadNotExecuted(LogicBlockContractId logicBlockContractId, int primaryAddress, MBusOutcome outcome, Exception exception);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message = "A selector found no record in a complete readout; its value stays empty until the meter sends it " +
                                 "(LogicBlockContractId={LogicBlockContractId}, Selector={Selector}, Manufacturer={Manufacturer}, Identification={Identification}, PrimaryAddress={PrimaryAddress})")]
        private partial void LogSelectorMissed(LogicBlockContractId logicBlockContractId, string selector, string manufacturer, string identification, int primaryAddress);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Answer ignored: the read was already abandoned (LogicBlockContractId={LogicBlockContractId}, CorrelationId={CorrelationId})")]
        private partial void LogLateAnswerIgnored(LogicBlockContractId logicBlockContractId, Guid correlationId);

        /// <summary>One read's completion state, captured by its callback and its backstop.</summary>
        private sealed class PendingRead
        {
            public Guid CorrelationId { get; }

            public int PrimaryAddress { get; }

            public int MaxTelegrams { get; }

            public IActorDispatcher Dispatcher { get; }

            public Action<MBusReadout, MBusReceipt> SuccessCallback { get; }

            public Action<Exception, MBusReceipt>? ErrorCallback { get; }

            public bool Finished { get; set; }

            public PendingRead(Guid correlationId,
                               int primaryAddress,
                               int maxTelegrams,
                               IActorDispatcher dispatcher,
                               Action<MBusReadout, MBusReceipt> successCallback,
                               Action<Exception, MBusReceipt>? errorCallback)
            {
                CorrelationId = correlationId;
                PrimaryAddress = primaryAddress;
                MaxTelegrams = maxTelegrams;
                Dispatcher = dispatcher;
                SuccessCallback = successCallback;
                ErrorCallback = errorCallback;
            }
        }
    }
}