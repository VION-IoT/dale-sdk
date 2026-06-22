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
    }
}
