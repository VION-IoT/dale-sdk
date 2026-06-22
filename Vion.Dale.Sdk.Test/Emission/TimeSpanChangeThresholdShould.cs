using System;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class TimeSpanChangeThresholdShould
    {
        [TestMethod]
        public void ExceedWhenDurationDeltaIsAtThreshold()
        {
            IChangeThreshold<TimeSpan> threshold = new TimeSpanChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(TimeSpan.FromSeconds(1), TimeSpan.FromSeconds(2), "1s"));
        }

        [TestMethod]
        public void NotExceedWhenDurationDeltaIsBelowThreshold()
        {
            IChangeThreshold<TimeSpan> threshold = new TimeSpanChangeThreshold();
            Assert.IsFalse(threshold.Exceeds(TimeSpan.FromMilliseconds(1000), TimeSpan.FromMilliseconds(1500), "1s"));
        }

        [TestMethod]
        public void ExceedRegardlessOfSignUsingDuration()
        {
            IChangeThreshold<TimeSpan> threshold = new TimeSpanChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(2), "3s"));
        }

        [TestMethod]
        public void ParseMillisecondThresholdToken()
        {
            IChangeThreshold<TimeSpan> threshold = new TimeSpanChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(TimeSpan.Zero, TimeSpan.FromMilliseconds(250), "250ms"));
            Assert.IsFalse(threshold.Exceeds(TimeSpan.Zero, TimeSpan.FromMilliseconds(249), "250ms"));
        }

        [TestMethod]
        public void ParseMicrosecondThresholdToken()
        {
            IChangeThreshold<TimeSpan> threshold = new TimeSpanChangeThreshold();
            // 500us == 5000 ticks (1 tick = 100ns).
            Assert.IsTrue(threshold.Exceeds(TimeSpan.Zero, TimeSpan.FromTicks(5000), "500us"));
            Assert.IsFalse(threshold.Exceeds(TimeSpan.Zero, TimeSpan.FromTicks(4999), "500us"));
        }
    }
}
