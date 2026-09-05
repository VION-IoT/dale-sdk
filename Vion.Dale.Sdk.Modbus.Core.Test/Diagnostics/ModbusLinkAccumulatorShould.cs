using System;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Core.Test.Diagnostics
{
    [TestClass]
    public class ModbusLinkAccumulatorShould
    {
        private static readonly DateTime ObservedAt = new(2026,
                                                          9,
                                                          5,
                                                          8,
                                                          0,
                                                          0,
                                                          DateTimeKind.Utc);

        private readonly ModbusLinkAccumulator _sut = new();

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.2")]
        [DataRow(ModbusOutcome.Success, ModbusLinkState.Online)]
        [DataRow(ModbusOutcome.DeviceError, ModbusLinkState.Online)]
        [DataRow(ModbusOutcome.Timeout, ModbusLinkState.Faulted)]
        [DataRow(ModbusOutcome.TransportError, ModbusLinkState.Faulted)]
        [DataRow(ModbusOutcome.ProtocolError, ModbusLinkState.Faulted)]
        public void MoveLinkStateOnOutcomeThatReachedWire(ModbusOutcome outcome, ModbusLinkState expectedState)
        {
            // Arrange

            // Act
            _sut.Record(Receipt(outcome));

            // Assert
            Assert.AreEqual(expectedState, _sut.Snapshot(0).State);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.2")]
        [DataRow(ModbusOutcome.BackedOff)]
        [DataRow(ModbusOutcome.Expired)]
        [DataRow(ModbusOutcome.Dropped)]
        [DataRow(ModbusOutcome.Invalid)]
        [DataRow(ModbusOutcome.Cancelled)]
        public void LeaveLinkStateOnLocallyDecidedOutcome(ModbusOutcome outcome)
        {
            // Arrange
            _sut.Record(Receipt(ModbusOutcome.Success));

            // Act
            _sut.Record(Receipt(outcome));

            // Assert
            Assert.AreEqual(ModbusLinkState.Online, _sut.Snapshot(0).State);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.4")]
        [DataRow(ModbusOutcome.BackedOff)]
        [DataRow(ModbusOutcome.Expired)]
        [DataRow(ModbusOutcome.Dropped)]
        [DataRow(ModbusOutcome.Invalid)]
        [DataRow(ModbusOutcome.Cancelled)]
        public void RecordLocallyDecidedOutcomeAsLastFailure(ModbusOutcome outcome)
        {
            // Arrange

            // Act
            _sut.Record(Receipt(outcome));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(outcome, summary.LastFailureOutcome);
            Assert.AreEqual(ObservedAt, summary.LastFailureAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.4")]
        public void CountOnlyOutcomesSummaryDeclaresCounterFor()
        {
            // Arrange
            foreach (var outcome in Enum.GetValues<ModbusOutcome>())
            {
                _sut.Record(Receipt(outcome));
            }

            // Act
            var summary = _sut.Snapshot(0);

            // Assert
            var counted = summary.SuccessCount + summary.DeviceErrorCount + summary.TimeoutCount + summary.TransportErrorCount + summary.ProtocolErrorCount +
                          summary.BackedOffCount + summary.ExpiredCount + summary.DroppedCount;
            Assert.AreEqual(8L, counted);

            // Ten outcomes, eight counters: Invalid and Cancelled are recorded as the last failure and nowhere else.
            Assert.HasCount(10, Enum.GetValues<ModbusOutcome>());
            Assert.AreEqual(ModbusOutcome.Cancelled, _sut.Snapshot(0).LastFailureOutcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.5")]
        [DataRow(ModbusOutcome.Success)]
        [DataRow(ModbusOutcome.Expired)]
        [DataRow(ModbusOutcome.Dropped)]
        [DataRow(ModbusOutcome.BackedOff)]
        [DataRow(ModbusOutcome.Cancelled)]
        public void RecordQueuedWaitOfRequestThatWasQueued(ModbusOutcome outcome)
        {
            // Arrange
            var queuedWait = TimeSpan.FromSeconds(4);

            // Act
            _sut.Record(Receipt(outcome, queuedWait: queuedWait));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(queuedWait, summary.LastQueuedWait);
            Assert.AreEqual(queuedWait, summary.MaxQueuedWait);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.5")]
        public void KeepQueuedWaitWhenRequestRefusedBeforeItQueued()
        {
            // Arrange
            var queuedWait = TimeSpan.FromSeconds(4);
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: queuedWait));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Invalid));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(queuedWait, summary.LastQueuedWait);
            Assert.AreEqual(queuedWait, summary.MaxQueuedWait);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.5")]
        public void KeepRoundTripExtremesOfOutcomesThatReachedWire()
        {
            // Arrange
            var roundTrip = TimeSpan.FromMilliseconds(120);
            _sut.Record(Receipt(ModbusOutcome.Success, roundTrip));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Dropped));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(roundTrip, summary.MinRoundTrip);
            Assert.AreEqual(roundTrip, summary.MaxRoundTrip);
            Assert.AreEqual(roundTrip, summary.LastRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-009.5")]
        public void ReportCallersQueueDepthInSnapshot()
        {
            // Arrange
            const int queueDepth = 7;

            // Act
            var summary = _sut.Snapshot(queueDepth);

            // Assert
            Assert.AreEqual(queueDepth, summary.QueueDepth);
        }

        private static ModbusReceipt Receipt(ModbusOutcome outcome, TimeSpan roundTrip = default, TimeSpan queuedWait = default)
        {
            return new ModbusReceipt(ObservedAt, 0, roundTrip, queuedWait, outcome);
        }
    }
}