using System;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.Sdk.Test.Diagnostics
{
    /// <summary>
    ///     The tumbling window behind four of the eleven vitals, over both of the value kinds it is
    ///     instantiated at. <b>Uncited by design</b>: this is an implementation premise, not a criterion —
    ///     what a caller observes is `AC-LIFE-018.4` through `RuntimeVitalsShould`, and the two are proved
    ///     there over the aggregate. The suite is kept because the generic is the one place the windowing
    ///     rule lives, and a change to it is easier to read against these four cases than against the
    ///     aggregate's.
    /// </summary>
    [TestClass]
    public sealed class WindowedMaxShould
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

        private readonly FakeTimeProvider _clock = new();

        [TestMethod]
        public void ReportGreatestSampleOfCurrentWindow()
        {
            // Arrange
            var max = new WindowedMax<TimeSpan>(_clock, Window);

            // Act
            max.Record(TimeSpan.FromMilliseconds(5));
            max.Record(TimeSpan.FromMilliseconds(12));
            max.Record(TimeSpan.FromMilliseconds(7));

            // Assert
            Assert.AreEqual(TimeSpan.FromMilliseconds(12), max.Read());
        }

        [TestMethod]
        public void StartFreshWindowOnFirstSampleAfterOneElapsed()
        {
            // Arrange
            var max = new WindowedMax<TimeSpan>(_clock, Window);
            max.Record(TimeSpan.FromMilliseconds(12));

            // Act
            _clock.Advance(Window + TimeSpan.FromSeconds(1));
            max.Record(TimeSpan.FromMilliseconds(4));

            // Assert
            Assert.AreEqual(TimeSpan.FromMilliseconds(4), max.Read());
        }

        [TestMethod]
        public void ReportDefaultOnceWindowElapsedWithNoSample()
        {
            // Arrange
            var max = new WindowedMax<TimeSpan>(_clock, Window);
            max.Record(TimeSpan.FromMilliseconds(12));

            // Act
            _clock.Advance(Window + TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(TimeSpan.Zero, max.Read());
        }

        [TestMethod]
        public void TrackCountsAsWellAsDurations()
        {
            // Arrange
            var max = new WindowedMax<int>(_clock, Window);

            // Act
            max.Record(3);
            max.Record(1);

            // Assert
            Assert.AreEqual(3, max.Read(), "Mailbox depth is the second instantiation, and it is a count rather than a span.");
        }
    }
}