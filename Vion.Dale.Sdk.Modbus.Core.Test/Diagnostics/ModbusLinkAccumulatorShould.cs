using System;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Core.Test.Diagnostics
{
    /// <summary>
    ///     The accumulator on a clock whose timestamps count nanoseconds, as the system clock's do on Linux, so a window
    ///     computed from raw timestamps as if they were <see cref="TimeSpan" /> ticks falls apart here.
    ///     <see cref="RecordWithoutAllocating" /> is a premise test and cites no id: that recording allocates nothing is
    ///     a property of the implementation, not an observable of the summary.
    /// </summary>
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

        private readonly NanosecondClock _clock = new();

        private readonly ModbusLinkAccumulator _sut;

        public ModbusLinkAccumulatorShould()
        {
            _sut = new ModbusLinkAccumulator(_clock);
        }

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
        [DataRow(ModbusOutcome.DeviceError)]
        [DataRow(ModbusOutcome.Timeout)]
        [DataRow(ModbusOutcome.TransportError)]
        [DataRow(ModbusOutcome.ProtocolError)]
        public void FeedRoundTripFiguresFromOutcomeThatReachedWire(ModbusOutcome outcome)
        {
            // Arrange
            var roundTrip = TimeSpan.FromMilliseconds(300);

            // Act
            _sut.Record(Receipt(outcome, roundTrip));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(1L, summary.RecentRoundTripCount);
            Assert.AreEqual(roundTrip, summary.RecentMeanRoundTrip);
            Assert.AreEqual(roundTrip, summary.RecentMaxRoundTrip);
            Assert.AreEqual(roundTrip, summary.MaxRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.5")]
        [DataRow(ModbusOutcome.BackedOff)]
        [DataRow(ModbusOutcome.Expired)]
        [DataRow(ModbusOutcome.Dropped)]
        [DataRow(ModbusOutcome.Cancelled)]
        [DataRow(ModbusOutcome.Invalid)]
        public void LeaveRoundTripFiguresOnLocallyDecidedOutcome(ModbusOutcome outcome)
        {
            // Arrange — the local outcome carries a round trip longer than the real one, so it would move every figure.
            var roundTrip = TimeSpan.FromMilliseconds(120);
            _sut.Record(Receipt(ModbusOutcome.Success, roundTrip));

            // Act
            _sut.Record(Receipt(outcome, TimeSpan.FromSeconds(5)));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(1L, summary.RecentRoundTripCount);
            Assert.AreEqual(roundTrip, summary.RecentMeanRoundTrip);
            Assert.AreEqual(roundTrip, summary.RecentMaxRoundTrip);
            Assert.AreEqual(roundTrip, summary.MaxRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.5")]
        [DataRow(ModbusOutcome.Success)]
        [DataRow(ModbusOutcome.DeviceError)]
        [DataRow(ModbusOutcome.Timeout)]
        [DataRow(ModbusOutcome.TransportError)]
        [DataRow(ModbusOutcome.ProtocolError)]
        [DataRow(ModbusOutcome.BackedOff)]
        [DataRow(ModbusOutcome.Expired)]
        [DataRow(ModbusOutcome.Dropped)]
        [DataRow(ModbusOutcome.Cancelled)]
        public void FeedQueuedWaitFiguresFromQueuedRequest(ModbusOutcome outcome)
        {
            // Arrange
            var queuedWait = TimeSpan.FromSeconds(4);

            // Act
            _sut.Record(Receipt(outcome, queuedWait: queuedWait));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(1L, summary.RecentQueuedWaitCount);
            Assert.AreEqual(queuedWait, summary.RecentMeanQueuedWait);
            Assert.AreEqual(queuedWait, summary.RecentMaxQueuedWait);
            Assert.AreEqual(queuedWait, summary.MaxQueuedWait);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.5")]
        public void LeaveQueuedWaitFiguresWhenRequestRefusedBeforeItQueued()
        {
            // Arrange
            var queuedWait = TimeSpan.FromSeconds(4);
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: queuedWait));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Invalid, queuedWait: TimeSpan.FromSeconds(9)));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(1L, summary.RecentQueuedWaitCount);
            Assert.AreEqual(queuedWait, summary.RecentMeanQueuedWait);
            Assert.AreEqual(queuedWait, summary.RecentMaxQueuedWait);
            Assert.AreEqual(queuedWait, summary.MaxQueuedWait);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.7")]
        public void AverageRoundTripsInWindow()
        {
            // Arrange
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(10)));
            _clock.Advance(TimeSpan.FromMinutes(3));
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(20)));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(90)));

            // Assert
            Assert.AreEqual(TimeSpan.FromMilliseconds(40), _sut.Snapshot(0).RecentMeanRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.7")]
        public void KeepLongestRoundTripInWindow()
        {
            // Arrange — the longest is neither the last in its minute nor in the last minute recorded.
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(10)));
            _clock.Advance(TimeSpan.FromMinutes(3));
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(90)));
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(20)));
            _clock.Advance(TimeSpan.FromMinutes(2));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(30)));

            // Assert
            Assert.AreEqual(TimeSpan.FromMilliseconds(90), _sut.Snapshot(0).RecentMaxRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.7")]
        public void AverageQueuedWaitsInWindow()
        {
            // Arrange
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: TimeSpan.FromSeconds(1)));
            _clock.Advance(TimeSpan.FromMinutes(3));
            _sut.Record(Receipt(ModbusOutcome.Expired, queuedWait: TimeSpan.FromSeconds(2)));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: TimeSpan.FromSeconds(9)));

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(4), _sut.Snapshot(0).RecentMeanQueuedWait);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.7")]
        public void KeepLongestQueuedWaitInWindow()
        {
            // Arrange — the longest is neither the last in its minute nor in the last minute recorded.
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: TimeSpan.FromSeconds(1)));
            _clock.Advance(TimeSpan.FromMinutes(3));
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: TimeSpan.FromSeconds(9)));
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: TimeSpan.FromSeconds(2)));
            _clock.Advance(TimeSpan.FromMinutes(2));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: TimeSpan.FromSeconds(3)));

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(9), _sut.Snapshot(0).RecentMaxQueuedWait);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.7")]
        [DataRow(0, 0, 1L, DisplayName = "read at once")]
        [DataRow(0, 959_999, 1L, DisplayName = "under sixteen minutes old")]
        [DataRow(0, 960_000, 0L, DisplayName = "sixteen minutes old")]
        [DataRow(59_999, 959_999, 1L, DisplayName = "exactly fifteen minutes old")]
        [DataRow(59_999, 960_000, 0L, DisplayName = "just over fifteen minutes old")]
        public void CountTransactionOnlyWhileWithinWindow(int recordedAtMilliseconds, int readAtMilliseconds, long expectedCount)
        {
            // Arrange
            _clock.Advance(TimeSpan.FromMilliseconds(recordedAtMilliseconds));
            _sut.Record(Receipt(ModbusOutcome.Success));
            _clock.Advance(TimeSpan.FromMilliseconds(readAtMilliseconds - recordedAtMilliseconds));

            // Act
            var summary = _sut.Snapshot(0);

            // Assert
            Assert.AreEqual(expectedCount, summary.RecentRoundTripCount);
            Assert.AreEqual(expectedCount, summary.RecentQueuedWaitCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.7")]
        public void LeaveAgedTransactionOutOfMinuteRecordedLater()
        {
            // Arrange — sixteen minutes on, the new minute is kept where the aged one was.
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(700), TimeSpan.FromSeconds(7)));
            _clock.Advance(TimeSpan.FromMinutes(16));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(1)));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(1L, summary.RecentRoundTripCount);
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), summary.RecentMeanRoundTrip);
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), summary.RecentMaxRoundTrip);
            Assert.AreEqual(1L, summary.RecentQueuedWaitCount);
            Assert.AreEqual(TimeSpan.FromSeconds(1), summary.RecentMaxQueuedWait);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.8")]
        public void EmptyWindowOnceItsTransactionsAgeOut()
        {
            // Arrange
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(50), TimeSpan.FromSeconds(1)));
            _clock.Advance(TimeSpan.FromMinutes(16));

            // Act
            var summary = _sut.Snapshot(0);

            // Assert
            Assert.AreEqual(0L, summary.RecentRoundTripCount);
            Assert.IsNull(summary.RecentMeanRoundTrip);
            Assert.IsNull(summary.RecentMaxRoundTrip);
            Assert.AreEqual(0L, summary.RecentQueuedWaitCount);
            Assert.IsNull(summary.RecentMeanQueuedWait);
            Assert.IsNull(summary.RecentMaxQueuedWait);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.8")]
        public void EmptyRoundTripWindowWhileOnlyLocalOutcomesRecorded()
        {
            // Arrange

            // Act
            _sut.Record(Receipt(ModbusOutcome.Expired, queuedWait: TimeSpan.FromSeconds(3)));

            // Assert — the queued-wait window holds the request, so only the round-trip one reads empty.
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(0L, summary.RecentRoundTripCount);
            Assert.IsNull(summary.RecentMeanRoundTrip);
            Assert.IsNull(summary.RecentMaxRoundTrip);
            Assert.AreEqual(1L, summary.RecentQueuedWaitCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.9")]
        [DataRow(200, 200, true, DisplayName = "a larger spike moves it")]
        [DataRow(50, 100, false, DisplayName = "a smaller one does not")]
        [DataRow(100, 100, false, DisplayName = "an equal one does not")]
        public void MoveMaxRoundTripInstantOnlyOnLargerValue(int laterMilliseconds, int expectedMilliseconds, bool expectLaterInstant)
        {
            // Arrange
            var laterAt = ObservedAt.AddMinutes(1);
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(100)));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(laterMilliseconds), receivedAt: laterAt));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(TimeSpan.FromMilliseconds(expectedMilliseconds), summary.MaxRoundTrip);
            Assert.AreEqual(expectLaterInstant ? laterAt : ObservedAt, summary.MaxRoundTripAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.9")]
        [DataRow(8, 8, true, DisplayName = "a larger wait moves it")]
        [DataRow(2, 4, false, DisplayName = "a smaller one does not")]
        [DataRow(4, 4, false, DisplayName = "an equal one does not")]
        public void MoveMaxQueuedWaitInstantOnlyOnLargerValue(int laterSeconds, int expectedSeconds, bool expectLaterInstant)
        {
            // Arrange
            var laterAt = ObservedAt.AddMinutes(1);
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: TimeSpan.FromSeconds(4)));

            // Act
            _sut.Record(Receipt(ModbusOutcome.Success, queuedWait: TimeSpan.FromSeconds(laterSeconds), receivedAt: laterAt));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.AreEqual(TimeSpan.FromSeconds(expectedSeconds), summary.MaxQueuedWait);
            Assert.AreEqual(expectLaterInstant ? laterAt : ObservedAt, summary.MaxQueuedWaitAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-016.9")]
        public void KeepSinceStartMaximaAfterWindowEmpties()
        {
            // Arrange
            _sut.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(900), TimeSpan.FromSeconds(6)));

            // Act
            _clock.Advance(TimeSpan.FromMinutes(16));

            // Assert
            var summary = _sut.Snapshot(0);
            Assert.IsNull(summary.RecentMaxRoundTrip);
            Assert.AreEqual(TimeSpan.FromMilliseconds(900), summary.MaxRoundTrip);
            Assert.AreEqual(ObservedAt, summary.MaxRoundTripAt);
            Assert.AreEqual(TimeSpan.FromSeconds(6), summary.MaxQueuedWait);
            Assert.AreEqual(ObservedAt, summary.MaxQueuedWaitAt);
        }

        [TestMethod]
        public void RecordWithoutAllocating()
        {
            // Arrange — the JIT warms up on another accumulator, so a ring allocated on first use still shows below.
            var warmUp = new ModbusLinkAccumulator(_clock);
            RecordAcrossSlots(warmUp);
            warmUp.Snapshot(0);
            var fresh = new ModbusLinkAccumulator(_clock);

            // Act
            var before = GC.GetAllocatedBytesForCurrentThread();
            RecordAcrossSlots(fresh);
            var allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // Assert
            Assert.AreEqual(0L, allocated);
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

        private static ModbusReceipt Receipt(ModbusOutcome outcome, TimeSpan roundTrip = default, TimeSpan queuedWait = default, DateTime? receivedAt = null)
        {
            return new ModbusReceipt(receivedAt ?? ObservedAt, 0, roundTrip, queuedWait, outcome);
        }

        // Crosses more slots than the ring holds, so every slot is reset for a new minute at least once.
        private void RecordAcrossSlots(ModbusLinkAccumulator accumulator)
        {
            for (var minute = 0; minute < 20; minute++)
            {
                accumulator.Record(Receipt(ModbusOutcome.Success, TimeSpan.FromMilliseconds(minute), TimeSpan.FromMilliseconds(minute)));
                _clock.Advance(TimeSpan.FromMinutes(1));
            }
        }

        private sealed class NanosecondClock : TimeProvider
        {
            // Not zero, so a window that forgot to subtract its start would not coincide with one that did.
            private long _timestamp = 7_000_000_000_000;

            public override long TimestampFrequency
            {
                get => 1_000_000_000;
            }

            public override long GetTimestamp()
            {
                return _timestamp;
            }

            public void Advance(TimeSpan by)
            {
                _timestamp += by.Ticks * 100;
            }
        }
    }
}