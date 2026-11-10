using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.MBus.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.MBus.Test
{
    /// <summary>
    ///     The contract a logic block binds, with the shared handler stood in for: reads are captured where the contract
    ///     sends them, and answers are handed back through the contract message the handler sends. Callbacks run when the
    ///     test flushes the dispatcher, as the block's actor would run them.
    /// </summary>
    [TestClass]
    public class MBusContractShould
    {
        private const string ContractIdentifier = "mbus0";

        private const int PrimaryAddress = 1;

        private static readonly DateTime Now = new(2026,
                                                   1,
                                                   1,
                                                   0,
                                                   0,
                                                   0,
                                                   DateTimeKind.Utc);

        private readonly Mock<IActorContext> _actorContextMock = new();

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private readonly List<MBusException> _errors = [];

        private readonly Mock<IActorReference> _handlerRefMock = new();

        private readonly List<Action> _pendingActions = [];

        private readonly List<MBusReadout> _readouts = [];

        private readonly List<MBusReceipt> _receipts = [];

        private readonly FakeTimeProvider _timeProvider = new(Now);

        private MBusContract _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(_pendingActions.Add);
            _sut = new MBusContract(ContractIdentifier, _actorContextMock.Object, _timeProvider, new Mock<ILogger<MBusContract>>().Object);
            _sut.SetLogicBlockContractId(new LogicBlockContractId(new LogicBlockId("lb0"), ContractIdentifier));
            _sut.SetLinkedContractHandler(_handlerRefMock.Object);
            _sut.IsEnabled = true;
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.7")]
        public void NameSharedHandler()
        {
            // Arrange
            var other = new MBusContract("mbus1", _actorContextMock.Object, _timeProvider, new Mock<ILogger<MBusContract>>().Object);

            // Act
            var names = new[] { _sut.ContractHandlerActorName, other.ContractHandlerActorName };

            // Assert
            Assert.AreEqual("MBusHandler,MBusHandler", string.Join(",", names));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-002.1")]
        public void StartDisabled()
        {
            // Arrange
            var fresh = new MBusContract("mbus1", _actorContextMock.Object, _timeProvider, new Mock<ILogger<MBusContract>>().Object);

            // Act
            var isEnabled = fresh.IsEnabled;

            // Assert
            Assert.IsFalse(isEnabled);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-004.1")]
        public void DefaultTimeouts()
        {
            // Arrange
            var fresh = new MBusContract("mbus1", _actorContextMock.Object, _timeProvider, new Mock<ILogger<MBusContract>>().Object);

            // Act
            fresh.MaxQueuedAge = null;

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(300), fresh.DefaultOperationTimeout);
            Assert.IsNull(fresh.MaxQueuedAge);
            Assert.AreEqual(TimeSpan.FromSeconds(30), _sut.MaxQueuedAge);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-004.2")]
        [DataRow(0.0, DisplayName = "zero")]
        [DataRow(-1.0, DisplayName = "negative")]
        [DataRow(2147483648.0, DisplayName = "past int.MaxValue ms")]
        public void ThrowForTimeoutOutsideLimits(double milliseconds)
        {
            // Arrange
            var value = TimeSpan.FromMilliseconds(milliseconds);

            // Act / Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _sut.DefaultOperationTimeout = value);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => _sut.MaxQueuedAge = value);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Read(operationTimeout: value));
            Assert.AreEqual(TimeSpan.FromSeconds(300), _sut.DefaultOperationTimeout);
            Assert.AreEqual(TimeSpan.FromSeconds(30), _sut.MaxQueuedAge);
            Assert.IsEmpty(SentRequests());
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-004.2")]
        public void AcceptTimeoutAtLimit()
        {
            // Arrange
            var limit = TimeSpan.FromMilliseconds(int.MaxValue);

            // Act
            _sut.DefaultOperationTimeout = limit;
            _sut.MaxQueuedAge = limit;
            Read(operationTimeout: limit);

            // Assert
            Assert.AreEqual(limit, SentRequests().Single().OperationTimeout);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-001.1")]
        public void ReturnBeforeDeliveringReadoutThroughDispatcher()
        {
            // Arrange
            Read();
            var request = SentRequests().Single();
            Respond(request, MBusOutcome.Success, MBusVectors.Em530);

            // Act
            var readoutsBeforeFlush = _readouts.Count;
            Flush();

            // Assert
            Assert.AreEqual(0, readoutsBeforeFlush);
            Assert.HasCount(1, _readouts);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.2")]
        public void DeliverReadoutOfTelegramsInOrder()
        {
            // Arrange
            Read();

            // Act
            Respond(SentRequests().Single(), MBusOutcome.Success, MBusVectors.Em530);
            Flush();

            // Assert
            Assert.AreEqual("D7,D8,D9,DA,DB", string.Join(",", _readouts.Single().Telegrams.Select(telegram => telegram.Header.AccessNumber.ToString("X2"))));
            Assert.AreEqual(MBusOutcome.Success, _receipts.Single().Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-001.2")]
        [DataRow(MBusOutcome.Success, DisplayName = "success callback")]
        [DataRow(MBusOutcome.NoResponse, DisplayName = "error callback")]
        public void HandReceiptToEachCallback(MBusOutcome outcome)
        {
            // Arrange
            Read();
            var request = SentRequests().Single();
            var publishedAt = Now.AddSeconds(2);
            _timeProvider.Advance(TimeSpan.FromSeconds(9));

            // Act
            Respond(request, outcome, outcome == MBusOutcome.Success ? MBusVectors.Em530 : null, publishedAt);
            Flush();

            // Assert
            var receipt = _receipts.Single();
            Assert.AreEqual(Now.AddSeconds(9), receipt.ReceivedAt);
            Assert.AreEqual(_timeProvider.GetTimestamp(), receipt.ReceivedTimestamp);
            Assert.AreEqual(TimeSpan.FromSeconds(2), receipt.QueuedWait);
            Assert.AreEqual(TimeSpan.FromSeconds(7), receipt.RoundTrip);
            Assert.AreEqual(outcome, receipt.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-001.3")]
        [DataRow(-1, 5, DisplayName = "address below 0")]
        [DataRow(251, 5, DisplayName = "address above 250")]
        [DataRow(1, 0, DisplayName = "no telegram")]
        public void RefuseArgumentOutOfRangeThroughErrorCallback(int primaryAddress, int maxTelegrams)
        {
            // Arrange

            // Act
            Read(primaryAddress, maxTelegrams);
            var errorsBeforeFlush = _errors.Count;
            Flush();

            // Assert
            Assert.AreEqual(0, errorsBeforeFlush);
            Assert.AreEqual(MBusOutcome.Invalid, _receipts.Single().Outcome);
            Assert.IsEmpty(SentRequests());
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-001.3")]
        [DataRow(0, 1, DisplayName = "address 0, one telegram")]
        [DataRow(250, 10, DisplayName = "address 250")]
        public void SendReadWithArgumentsInRange(int primaryAddress, int maxTelegrams)
        {
            // Arrange

            // Act
            Read(primaryAddress, maxTelegrams);

            // Assert
            var request = SentRequests().Single();
            Assert.AreEqual((byte)primaryAddress, request.PrimaryAddress);
            Assert.AreEqual(maxTelegrams, request.MaxTelegrams);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-001.4")]
        [DataRow(MBusOutcome.NoResponse, DisplayName = "the handler's failure")]
        [DataRow(MBusOutcome.Timeout, DisplayName = "the handler's timeout")]
        [DataRow(MBusOutcome.ProtocolError, DisplayName = "a telegram failing its checks")]
        [DataRow(MBusOutcome.Invalid, DisplayName = "an argument refused")]
        public void FailWithExceptionCarryingReceiptOutcome(MBusOutcome outcome)
        {
            // Arrange
            if (outcome == MBusOutcome.Invalid)
            {
                Read(251);
            }
            else
            {
                Read();
                var request = SentRequests().Single();
                if (outcome == MBusOutcome.ProtocolError)
                {
                    Respond(request, MBusOutcome.Success, [MBusVectors.Em530Telegram1.Replace("D1 16", "D2 16")]);
                }
                else
                {
                    Respond(request, outcome);
                }
            }

            // Act
            Flush();

            // Assert
            Assert.AreEqual(outcome, _receipts.Single().Outcome);
            Assert.AreEqual(outcome, _errors.Single().Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.1")]
        [DataRow(new string[0], DisplayName = "no telegram")]
        [DataRow(new[]
                 {
                     MBusVectors.Em530Telegram1, MBusVectors.Em530Telegram2, MBusVectors.Em530Telegram3, MBusVectors.Em530Telegram4, MBusVectors.Em530Telegram5,
                     MBusVectors.Em530Telegram5,
                 },
                 DisplayName = "more telegrams than asked")]
        [DataRow(new[] { "10 2C 2C 68 08 01 72 13 66 00 00 36 1C DD 02 DB 20 00 00 84 80 C0 40 05 00 00 00 00 84 C0 C0 40 05 00 00 00 00 84 80 80 40 FD 59 E8 03 00 00 0F 86 16" },
                 DisplayName = "wrong start byte")]
        [DataRow(new[] { "68 2C 2C 68 08 01 72 13 66 00 00 36 1C DD 02 DB 20 00 00 84 80 C0 40 05 00 00 00 00 84 C0 C0 40 05 00 00 00 00 84 80 80 40 FD 59 E8 03 00 00 0F 86" },
                 DisplayName = "wrong length")]
        [DataRow(new[] { "68 2C 2C 68 08 01 72 13 66 00 00 36 1C DD 02 DB 20 00 00 84 80 C0 40 05 00 00 00 00 84 C0 C0 40 05 00 00 00 00 84 80 80 40 FD 59 E8 03 00 00 0F 87 16" },
                 DisplayName = "wrong checksum")]
        [DataRow(new[] { "68 2C 2C 68 08 01 72 13 66 00 00 36 1C DD 02 DB 20 00 00 84 80 C0 40 05 00 00 00 00 84 C0 C0 40 05 00 00 00 00 84 80 80 40 FD 59 E8 03 00 00 0F 86 17" },
                 DisplayName = "wrong stop byte")]
        [DataRow(new[] { "68 2C 2C 68 08 01 78 13 66 00 00 36 1C DD 02 DB 20 00 00 84 80 C0 40 05 00 00 00 00 84 C0 C0 40 05 00 00 00 00 84 80 80 40 FD 59 E8 03 00 00 0F 8C 16" },
                 DisplayName = "CI other than 72h")]
        [DataRow(new[] { "68 2C 2C 68 08 02 72 13 66 00 00 36 1C DD 02 DB 20 00 00 84 80 C0 40 05 00 00 00 00 84 C0 C0 40 05 00 00 00 00 84 80 80 40 FD 59 E8 03 00 00 0F 87 16" },
                 DisplayName = "another address")]
        [DataRow(new[]
                 {
                     MBusVectors.Em530Telegram1,
                     "68 2C 2C 68 08 01 72 14 66 00 00 36 1C DD 02 DB 20 00 00 84 80 C0 40 05 00 00 00 00 84 C0 C0 40 05 00 00 00 00 84 80 80 40 FD 59 E8 03 00 00 0F 87 16",
                 },
                 DisplayName = "identification changes")]
        [DataRow(new[] { "68 1A 1A 68 08 01 72 13 66 00 00 36 1C DD 02 DB 20 00 00 84 80 C0 40 05 00 00 00 FC 16" }, DisplayName = "record runs past the end")]
        public void FailCheckedFramesAsProtocolErrorWithTelegramsAsReceived(string[] telegrams)
        {
            // Arrange
            Read();

            // Act
            Respond(SentRequests().Single(), MBusOutcome.Success, telegrams);
            Flush();

            // Assert
            Assert.IsEmpty(_readouts);
            Assert.AreEqual(MBusOutcome.ProtocolError, _receipts.Single().Outcome);
            Assert.AreEqual(string.Join("|", telegrams.Select(hex => MBusFrames.Hex(MBusFrames.Bytes(hex)))),
                            string.Join("|", _errors.Single().Telegrams.Select(bytes => MBusFrames.Hex(bytes.ToArray()))));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-002.2")]
        public void ExecuteNothingWhileDisabled()
        {
            // Arrange
            _sut.IsEnabled = false;

            // Act
            Read(251);
            Flush();

            // Assert
            Assert.IsEmpty(SentRequests());
            Assert.IsEmpty(_receipts);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-002.3")]
        public void CompleteAcceptedReadAfterDisabling()
        {
            // Arrange
            Read();
            var request = SentRequests().Single();
            _sut.IsEnabled = false;

            // Act
            Respond(request, MBusOutcome.Success, MBusVectors.Em530);
            Flush();

            // Assert
            Assert.HasCount(1, _readouts);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-003.1")]
        [DataRow(1, 5, DisplayName = "valid arguments")]
        [DataRow(251, 0, DisplayName = "invalid arguments")]
        public void DropReadWhilePreviousPending(int primaryAddress, int maxTelegrams)
        {
            // Arrange
            Read();
            var pending = SentRequests().Single();

            // Act
            Read(primaryAddress, maxTelegrams);
            Flush();
            var receiptsOfBusyRead = _receipts.Count;
            Respond(pending, MBusOutcome.Success, MBusVectors.Em530);
            Flush();

            // Assert
            Assert.HasCount(1, SentRequests());
            Assert.AreEqual(0, receiptsOfBusyRead);
            Assert.HasCount(1, _readouts);
            Assert.AreEqual(1L, _sut.Link.BusyCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-008.1")]
        public void AccumulateEveryCompletedReadIntoLink()
        {
            // Arrange
            Read();
            Respond(SentRequests().Single(), MBusOutcome.Success, MBusVectors.Em530);
            Read();
            Respond(SentRequests()[1], MBusOutcome.Success, [MBusVectors.Em530Telegram1.Replace("D1 16", "D2 16")]);
            Read(251);

            // Act
            var link = _sut.Link;

            // Assert
            Assert.AreEqual(1L, link.SuccessCount);
            Assert.AreEqual(1L, link.ProtocolErrorCount);
            Assert.AreEqual(1L, link.InvalidCount);
            Assert.AreEqual(MBusLinkState.Faulted, link.State);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-005.8")]
        public void LeaveLinkStateWhenProviderAnswersCodeOfNewerVersion()
        {
            // Arrange
            Read();
            Respond(SentRequests().Single(), MBusOutcome.Success, MBusVectors.Em530);
            Read();

            // Act
            Respond(SentRequests()[1], MBusOutcome.Invalid);

            // Assert
            Assert.AreEqual(MBusLinkState.Online, _sut.Link.State);
            Assert.AreEqual(1L, _sut.Link.InvalidCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-003.2")]
        [DataRow(MBusOutcome.Success, DisplayName = "succeeds")]
        [DataRow(MBusOutcome.NoResponse, DisplayName = "fails")]
        [DataRow(MBusOutcome.Timeout, DisplayName = "times out")]
        [DataRow(MBusOutcome.Expired, DisplayName = "expires")]
        public void AcceptNextReadOnceSlotFrees(MBusOutcome outcome)
        {
            // Arrange
            Read();
            Respond(SentRequests().Single(), outcome, outcome == MBusOutcome.Success ? MBusVectors.Em530 : null);

            // Act
            Read();

            // Assert
            Assert.HasCount(2, SentRequests());
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-003.2")]
        public void KeepSlotWhenEarlierReadAnsweredAgain()
        {
            // Arrange
            // The first read's answer frees the slot, the second read takes it, then the first answer arrives a second time.
            Read();
            var first = SentRequests().Single();
            Respond(first, MBusOutcome.NoResponse);
            Read();
            Respond(first, MBusOutcome.NoResponse);

            // Act
            Read();

            // Assert
            Assert.HasCount(2, SentRequests());
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-003.2")]
        public void LeaveSlotFreeAfterRefusedRead()
        {
            // Arrange
            Read(251);

            // Act
            Read();

            // Assert
            Assert.HasCount(1, SentRequests());
        }

        private void Read(int primaryAddress = PrimaryAddress, int maxTelegrams = 5, TimeSpan? operationTimeout = null)
        {
            _sut.Read(primaryAddress,
                      maxTelegrams,
                      _dispatcherMock.Object,
                      (readout, receipt) =>
                      {
                          _readouts.Add(readout);
                          _receipts.Add(receipt);
                      },
                      (exception, receipt) =>
                      {
                          _errors.Add((MBusException)exception);
                          _receipts.Add(receipt);
                      },
                      operationTimeout);
        }

        private List<ReadMBusRequest> SentRequests()
        {
            return _actorContextMock.Invocations
                                    .Where(invocation => invocation.Method.Name == nameof(IActorContext.SendTo) && ReferenceEquals(invocation.Arguments[0], _handlerRefMock.Object))
                                    .Select(invocation => invocation.Arguments[1])
                                    .OfType<ContractMessage<ReadMBusRequest>>()
                                    .Select(message => message.Data)
                                    .ToList();
        }

        private void Respond(ReadMBusRequest request, MBusOutcome outcome, string[]? telegrams = null, DateTime? publishedAt = null)
        {
            var exception = outcome == MBusOutcome.Success ? null : new MBusException(outcome, outcome.ToString());
            var response = new ReadMBusResponse(telegrams?.Select(MBusFrames.Bytes).ToArray(),
                                                exception,
                                                request.Callback,
                                                request.CorrelationId,
                                                request.CreatedAt,
                                                publishedAt ?? request.CreatedAt,
                                                _timeProvider.GetUtcNow().UtcDateTime,
                                                _timeProvider.GetTimestamp(),
                                                outcome);
            _sut.HandleContractMessage(new ContractMessage<ReadMBusResponse>(new LogicBlockContractId(new LogicBlockId("lb0"), ContractIdentifier), response));
        }

        private void Flush()
        {
            var actions = _pendingActions.ToList();
            _pendingActions.Clear();
            foreach (var action in actions)
            {
                action();
            }
        }
    }
}