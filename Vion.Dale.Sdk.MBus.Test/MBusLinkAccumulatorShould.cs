using System;

namespace Vion.Dale.Sdk.MBus.Test
{
    /// <summary>
    ///     The accumulator on a clock whose timestamps count nanoseconds, as the system clock's do on Linux, so a window
    ///     computed from raw timestamps as if they were <see cref="TimeSpan" /> ticks falls apart here.
    /// </summary>
    [TestClass]
    public class MBusLinkAccumulatorShould
    {
        private static readonly DateTime ObservedAt = new(2026, 11, 10, 8, 0, 0, DateTimeKind.Utc);

        private readonly NanosecondClock _clock = new();

        private readonly MBusLinkAccumulator _sut;

        public MBusLinkAccumulatorShould()
        {
            _sut = new MBusLinkAccumulator(_clock);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-008.2")]
        [DataRow(MBusOutcome.Success, MBusLinkState.Online)]
        [DataRow(MBusOutcome.NoResponse, MBusLinkState.Faulted)]
        [DataRow(MBusOutcome.Garbled, MBusLinkState.Faulted)]
        [DataRow(MBusOutcome.LineUnavailable, MBusLinkState.Faulted)]
        [DataRow(MBusOutcome.Timeout, MBusLinkState.Faulted)]
        [DataRow(MBusOutcome.ProtocolError, MBusLinkState.Faulted)]
        public void MoveLinkStateOnOutcomeAboutMeter(MBusOutcome outcome, MBusLinkState expected)
        {
            // Arrange
            // Start from the opposite verdict, so an outcome that left the state alone fails the row.
            _sut.Record(Receipt(expected == MBusLinkState.Online ? MBusOutcome.Timeout : MBusOutcome.Success));

            // Act
            _sut.Record(Receipt(outcome));

            // Assert
            Assert.AreEqual(expected, _sut.Snapshot().State);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-008.2")]
        [DataRow(MBusOutcome.Expired)]
        [DataRow(MBusOutcome.Dropped)]
        [DataRow(MBusOutcome.Invalid)]
        public void LeaveLinkStateOnOutcomeDecidedWithoutMeter(MBusOutcome outcome)
        {
            // Arrange
            _sut.Record(Receipt(MBusOutcome.Success));

            // Act
            _sut.Record(Receipt(outcome));

            // Assert
            Assert.AreEqual(MBusLinkState.Online, _sut.Snapshot().State);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-008.3")]
        public void CountEveryOutcomeAndBusyRead()
        {
            // Arrange
            foreach (MBusOutcome outcome in Enum.GetValues(typeof(MBusOutcome)))
            {
                for (var index = 0; index <= (int)outcome; index++)
                {
                    _sut.Record(Receipt(outcome));
                }
            }

            // Act
            _sut.RecordBusy();
            _sut.RecordBusy();
            var summary = _sut.Snapshot();

            // Assert
            Assert.AreEqual("1,2,3,4,5,6,7,8,9,2",
                            string.Join(",",
                                        summary.SuccessCount,
                                        summary.NoResponseCount,
                                        summary.GarbledCount,
                                        summary.LineUnavailableCount,
                                        summary.TimeoutCount,
                                        summary.ProtocolErrorCount,
                                        summary.ExpiredCount,
                                        summary.DroppedCount,
                                        summary.InvalidCount,
                                        summary.BusyCount));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-008.3")]
        [DataRow(MBusOutcome.Success, 1L)]
        [DataRow(MBusOutcome.NoResponse, 1L)]
        [DataRow(MBusOutcome.Garbled, 1L)]
        [DataRow(MBusOutcome.LineUnavailable, 1L)]
        [DataRow(MBusOutcome.ProtocolError, 1L)]
        [DataRow(MBusOutcome.Timeout, 0L)]
        [DataRow(MBusOutcome.Expired, 0L)]
        [DataRow(MBusOutcome.Dropped, 0L)]
        [DataRow(MBusOutcome.Invalid, 0L)]
        public void FeedRoundTripOnlyFromAnsweredRead(MBusOutcome outcome, long expected)
        {
            // Arrange

            // Act
            _sut.Record(Receipt(outcome, TimeSpan.FromSeconds(3)));

            // Assert
            var summary = _sut.Snapshot();
            Assert.AreEqual(expected, summary.RecentRoundTripCount);
            Assert.AreEqual(expected == 1 ? TimeSpan.FromSeconds(3) : null, summary.MaxRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-008.3")]
        [DataRow(MBusOutcome.Success, 1L)]
        [DataRow(MBusOutcome.Timeout, 1L)]
        [DataRow(MBusOutcome.Expired, 1L)]
        [DataRow(MBusOutcome.Dropped, 1L)]
        [DataRow(MBusOutcome.Invalid, 0L)]
        public void FeedQueuedWaitOnlyFromReadThatReachedHandler(MBusOutcome outcome, long expected)
        {
            // Arrange

            // Act
            _sut.Record(Receipt(outcome, queuedWait: TimeSpan.FromSeconds(2)));

            // Assert
            var summary = _sut.Snapshot();
            Assert.AreEqual(expected, summary.RecentQueuedWaitCount);
            Assert.AreEqual(expected == 1 ? TimeSpan.FromSeconds(2) : null, summary.MaxQueuedWait);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-008.4")]
        [DataRow(0, 0, 1L, DisplayName = "read at once")]
        [DataRow(0, 959_999, 1L, DisplayName = "under sixteen minutes old")]
        [DataRow(0, 960_000, 0L, DisplayName = "sixteen minutes old")]
        [DataRow(59_999, 959_999, 1L, DisplayName = "exactly fifteen minutes old")]
        [DataRow(59_999, 960_000, 0L, DisplayName = "just over fifteen minutes old")]
        public void CountReadOnlyWhileWithinWindow(int recordedAtMilliseconds, int readAtMilliseconds, long expected)
        {
            // Arrange
            _clock.Advance(TimeSpan.FromMilliseconds(recordedAtMilliseconds));
            _sut.Record(Receipt(MBusOutcome.Success));
            _clock.Advance(TimeSpan.FromMilliseconds(readAtMilliseconds - recordedAtMilliseconds));

            // Act
            var summary = _sut.Snapshot();

            // Assert
            Assert.AreEqual(expected, summary.RecentRoundTripCount);
            Assert.AreEqual(expected, summary.RecentQueuedWaitCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MBUS-008.4")]
        public void AverageAndMaximumOverWindow()
        {
            // Arrange
            // The longest is neither the first nor the last recorded, and one read has aged out of the window.
            _sut.Record(Receipt(MBusOutcome.Success, TimeSpan.FromSeconds(100), TimeSpan.FromSeconds(100)));
            _clock.Advance(TimeSpan.FromMinutes(16));
            _sut.Record(Receipt(MBusOutcome.Success, TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(1)));
            _clock.Advance(TimeSpan.FromMinutes(3));
            _sut.Record(Receipt(MBusOutcome.Success, TimeSpan.FromSeconds(50), TimeSpan.FromSeconds(5)));

            // Act
            _sut.Record(Receipt(MBusOutcome.Success, TimeSpan.FromSeconds(30), TimeSpan.FromSeconds(3)));
            var summary = _sut.Snapshot();

            // Assert
            Assert.AreEqual(3L, summary.RecentRoundTripCount);
            Assert.AreEqual(TimeSpan.FromSeconds(30), summary.RecentMeanRoundTrip);
            Assert.AreEqual(TimeSpan.FromSeconds(50), summary.RecentMaxRoundTrip);
            Assert.AreEqual(3L, summary.RecentQueuedWaitCount);
            Assert.AreEqual(TimeSpan.FromSeconds(3), summary.RecentMeanQueuedWait);
            Assert.AreEqual(TimeSpan.FromSeconds(5), summary.RecentMaxQueuedWait);
        }

        private static MBusReceipt Receipt(MBusOutcome outcome, TimeSpan? roundTrip = null, TimeSpan? queuedWait = null)
        {
            return new MBusReceipt(ObservedAt, 0, roundTrip ?? TimeSpan.FromMilliseconds(500), queuedWait ?? TimeSpan.FromMilliseconds(5), outcome);
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
