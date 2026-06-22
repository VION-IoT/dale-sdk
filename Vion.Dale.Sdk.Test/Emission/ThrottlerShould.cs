using System;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class ThrottlerShould
    {
        private static readonly DateTimeOffset T0 = new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero);

        private sealed class Cfg : IThrottleConfigured
        {
            public string MinInterval { get; set; } = "250ms";
            public string? MinChange { get; set; }
            public bool Immediate { get; set; }
        }

        private static ThrottlePolicy Policy(
            string minInterval = "250ms",
            string? minChange = null,
            bool immediate = false,
            Type? valueType = null)
        {
            return ThrottlePolicy.FromConfigured(
                new Cfg { MinInterval = minInterval, MinChange = minChange, Immediate = immediate },
                valueType ?? typeof(double));
        }

        [TestMethod]
        public void EmitTheFirstChangeOnTheLeadingEdge()
        {
            var throttler = new Throttler(Policy());

            var result = throttler.Offer(1.0d, T0);

            Assert.AreEqual(EmitAction.Emit, result.Action);
            Assert.IsTrue(throttler.HasEmitted);
            Assert.AreEqual(1.0d, throttler.LastEmitted);
            Assert.IsFalse(throttler.HasPending);
        }

        private sealed record Sample(int A, string B);

        [TestMethod]
        public void HoldWithinTheIntervalKeepingTheLatestValueThenFlushAtTheDeadline()
        {
            var throttler = new Throttler(Policy("250ms"));

            // Leading-edge emit at T0.
            Assert.AreEqual(EmitAction.Emit, throttler.Offer(1.0d, T0).Action);

            // Two changes inside the 250ms window -> both Hold, deadline = T0 + 250ms, latest wins.
            var firstHold = throttler.Offer(2.0d, T0 + TimeSpan.FromMilliseconds(50));
            Assert.AreEqual(EmitAction.Hold, firstHold.Action);
            Assert.AreEqual(T0 + TimeSpan.FromMilliseconds(250), firstHold.Deadline);

            var secondHold = throttler.Offer(3.0d, T0 + TimeSpan.FromMilliseconds(100));
            Assert.AreEqual(EmitAction.Hold, secondHold.Action);
            Assert.AreEqual(T0 + TimeSpan.FromMilliseconds(250), secondHold.Deadline);

            Assert.IsTrue(throttler.HasPending);
            Assert.AreEqual(T0 + TimeSpan.FromMilliseconds(250), throttler.PendingDeadline);

            // Flush at the deadline -> the latest held value (3.0), pending cleared.
            var flushed = throttler.TryFlush(T0 + TimeSpan.FromMilliseconds(250), out var value);
            Assert.IsTrue(flushed);
            Assert.AreEqual(3.0d, value);
            Assert.AreEqual(3.0d, throttler.LastEmitted);
            Assert.IsFalse(throttler.HasPending);
        }

        [TestMethod]
        public void DropAValueEqualToTheLastEmittedEvenAfterTheInterval()
        {
            var throttler = new Throttler(Policy("250ms"));

            Assert.AreEqual(EmitAction.Emit, throttler.Offer(5.0d, T0).Action);

            // Equal value well past the interval -> floor still drops it.
            var result = throttler.Offer(5.0d, T0 + TimeSpan.FromSeconds(10));

            Assert.AreEqual(EmitAction.Drop, result.Action);
            Assert.IsFalse(throttler.HasPending);
        }

        [TestMethod]
        public void DropARebuiltEqualRecordViaValueEquality()
        {
            var throttler = new Throttler(Policy("250ms", valueType: typeof(Sample)));

            var first = new Sample(1, "x");
            var rebuilt = new Sample(1, "x"); // reference-distinct, value-equal

            Assert.AreEqual(EmitAction.Emit, throttler.Offer(first, T0).Action);

            var result = throttler.Offer(rebuilt, T0 + TimeSpan.FromSeconds(1));

            Assert.AreEqual(EmitAction.Drop, result.Action);
        }
    }
}
