using System;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.Sdk.Test.Diagnostics
{
    [TestClass]
    public class WindowedMaxShould
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

        [TestMethod]
        public void ReportTheMaxWithinTheCurrentWindow()
        {
            var max = new WindowedMax<TimeSpan>(new FakeTimeProvider(), Window);

            max.Record(TimeSpan.FromMilliseconds(5));
            max.Record(TimeSpan.FromMilliseconds(12));
            max.Record(TimeSpan.FromMilliseconds(7));

            Assert.AreEqual(TimeSpan.FromMilliseconds(12), max.Read());
        }

        [TestMethod]
        public void DropTheOldMaxAfterTheWindowElapses()
        {
            var clock = new FakeTimeProvider();
            var max = new WindowedMax<TimeSpan>(clock, Window);

            max.Record(TimeSpan.FromMilliseconds(12));
            clock.Advance(Window + TimeSpan.FromSeconds(1));
            max.Record(TimeSpan.FromMilliseconds(4));

            Assert.AreEqual(TimeSpan.FromMilliseconds(4), max.Read());
        }

        [TestMethod]
        public void ReportDefaultWhenIdlePastTheWindow()
        {
            var clock = new FakeTimeProvider();
            var max = new WindowedMax<TimeSpan>(clock, Window);

            max.Record(TimeSpan.FromMilliseconds(12));
            clock.Advance(Window + TimeSpan.FromSeconds(1));

            Assert.AreEqual(TimeSpan.Zero, max.Read());
        }

        [TestMethod]
        public void TrackIntegerValuesToo()
        {
            var max = new WindowedMax<int>(new FakeTimeProvider(), Window);

            max.Record(3);
            max.Record(1);

            Assert.AreEqual(3, max.Read());
        }
    }
}
