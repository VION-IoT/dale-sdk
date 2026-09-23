using System;
using System.Net;

namespace Vion.Dale.Sdk.Http.Test
{
    /// <summary>
    ///     The client summary's window and lifetime maximum, fed receipts directly on a clock whose timestamps count
    ///     nanoseconds, as the system clock's do on Linux, so a window computed from raw timestamps as if they were
    ///     <see cref="TimeSpan" /> ticks falls apart here. Which outcomes feed the round trip, and what is counted and
    ///     recorded, are proven through the executor in <see cref="HttpRequestExecutorShould" />, where the receipts are
    ///     built.
    /// </summary>
    [TestClass]
    public class HttpClientSummaryAccumulatorShould
    {
        private static readonly DateTime ObservedAt = new(2026,
                                                          9,
                                                          23,
                                                          8,
                                                          0,
                                                          0,
                                                          DateTimeKind.Utc);

        private readonly NanosecondClock _clock = new();

        private readonly HttpClientSummaryAccumulator _sut;

        public HttpClientSummaryAccumulatorShould()
        {
            _sut = new HttpClientSummaryAccumulator(_clock);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.5")]
        public void AverageRoundTripsInWindow()
        {
            // Arrange
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(10)));
            _clock.Advance(TimeSpan.FromMinutes(3));
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(20)));

            // Act
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(90)));

            // Assert
            Assert.AreEqual(TimeSpan.FromMilliseconds(40), _sut.Snapshot().RecentMeanRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.5")]
        public void KeepLongestRoundTripInWindow()
        {
            // Arrange — the longest is neither the last in its minute nor in the last minute recorded.
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(10)));
            _clock.Advance(TimeSpan.FromMinutes(3));
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(90)));
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(20)));
            _clock.Advance(TimeSpan.FromMinutes(2));

            // Act
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(30)));

            // Assert
            Assert.AreEqual(TimeSpan.FromMilliseconds(90), _sut.Snapshot().RecentMaxRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.5")]
        [DataRow(0, 0, 1L, DisplayName = "read at once")]
        [DataRow(0, 959_999, 1L, DisplayName = "under sixteen minutes old")]
        [DataRow(0, 960_000, 0L, DisplayName = "sixteen minutes old")]
        [DataRow(59_999, 959_999, 1L, DisplayName = "exactly fifteen minutes old")]
        [DataRow(59_999, 960_000, 0L, DisplayName = "just over fifteen minutes old")]
        public void CountRequestOnlyWhileWithinWindow(int recordedAtMilliseconds, int readAtMilliseconds, long expectedCount)
        {
            // Arrange
            _clock.Advance(TimeSpan.FromMilliseconds(recordedAtMilliseconds));
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(5)));
            _clock.Advance(TimeSpan.FromMilliseconds(readAtMilliseconds - recordedAtMilliseconds));

            // Act
            var summary = _sut.Snapshot();

            // Assert
            Assert.AreEqual(expectedCount, summary.RecentRoundTripCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.5")]
        public void LeaveAgedRequestOutOfMinuteRecordedLater()
        {
            // Arrange — sixteen minutes on, the new minute is kept where the aged one was.
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(700)));
            _clock.Advance(TimeSpan.FromMinutes(16));

            // Act
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(100)));

            // Assert
            var summary = _sut.Snapshot();
            Assert.AreEqual(1L, summary.RecentRoundTripCount);
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), summary.RecentMeanRoundTrip);
            Assert.AreEqual(TimeSpan.FromMilliseconds(100), summary.RecentMaxRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.5")]
        public void EmptyWindowOnceItsRequestsAgeOut()
        {
            // Arrange
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(50)));
            _clock.Advance(TimeSpan.FromMinutes(16));

            // Act
            var summary = _sut.Snapshot();

            // Assert
            Assert.AreEqual(0L, summary.RecentRoundTripCount);
            Assert.IsNull(summary.RecentMeanRoundTrip);
            Assert.IsNull(summary.RecentMaxRoundTrip);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.6")]
        [DataRow(200, 200, true, DisplayName = "larger round trip moves it")]
        [DataRow(50, 100, false, DisplayName = "smaller one does not")]
        [DataRow(100, 100, false, DisplayName = "equal one does not")]
        public void MoveMaxRoundTripInstantOnlyOnLargerValue(int laterMilliseconds, int expectedMilliseconds, bool expectLaterInstant)
        {
            // Arrange
            var laterAt = ObservedAt.AddMinutes(1);
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(100)));

            // Act
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(laterMilliseconds), laterAt));

            // Assert
            var summary = _sut.Snapshot();
            Assert.AreEqual(TimeSpan.FromMilliseconds(expectedMilliseconds), summary.MaxRoundTrip);
            Assert.AreEqual(expectLaterInstant ? laterAt : ObservedAt, summary.MaxRoundTripAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.6")]
        public void KeepSinceStartMaximumAfterWindowEmpties()
        {
            // Arrange
            _sut.Record(Receipt(TimeSpan.FromMilliseconds(900)));

            // Act
            _clock.Advance(TimeSpan.FromMinutes(16));

            // Assert
            var summary = _sut.Snapshot();
            Assert.IsNull(summary.RecentMaxRoundTrip);
            Assert.AreEqual(TimeSpan.FromMilliseconds(900), summary.MaxRoundTrip);
            Assert.AreEqual(ObservedAt, summary.MaxRoundTripAt);
        }

        private static HttpReceipt Receipt(TimeSpan roundTrip, DateTime? receivedAt = null)
        {
            return new HttpReceipt(receivedAt ?? ObservedAt, 0, roundTrip, HttpOutcome.Success, HttpStatusCode.OK);
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
