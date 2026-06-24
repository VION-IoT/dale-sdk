using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class ThrottlePolicyShould
    {
        [TestMethod]
        public void ParseTheMinInterval()
        {
            var cfg = new FakeThrottleConfigured { MinInterval = "1s" };

            var policy = ThrottlePolicy.FromConfigured(cfg, typeof(double));

            Assert.AreEqual(TimeSpan.FromSeconds(1), policy.MinInterval);
            Assert.IsFalse(policy.ThrottleDisabled);
        }

        [TestMethod]
        public void MarkThrottleDisabledForZero()
        {
            var cfg = new FakeThrottleConfigured { MinInterval = "0" };

            var policy = ThrottlePolicy.FromConfigured(cfg, typeof(double));

            Assert.AreEqual(TimeSpan.Zero, policy.MinInterval);
            Assert.IsTrue(policy.ThrottleDisabled);
        }

        [TestMethod]
        public void MarkThrottleDisabledForZeroMilliseconds()
        {
            var cfg = new FakeThrottleConfigured { MinInterval = "0ms" };

            var policy = ThrottlePolicy.FromConfigured(cfg, typeof(double));

            Assert.IsTrue(policy.ThrottleDisabled);
        }

        [TestMethod]
        public void CarryTheImmediateFlag()
        {
            var cfg = new FakeThrottleConfigured { MinInterval = "250ms", Immediate = true };

            var policy = ThrottlePolicy.FromConfigured(cfg, typeof(double));

            Assert.IsTrue(policy.Immediate);
        }

        [TestMethod]
        public void LeaveThresholdNullWhenNoMinChange()
        {
            var cfg = new FakeThrottleConfigured { MinInterval = "250ms", MinChange = null };

            var policy = ThrottlePolicy.FromConfigured(cfg, typeof(double));

            Assert.IsNull(policy.Threshold);
            Assert.IsNull(policy.MinChange);
        }

        [TestMethod]
        public void ResolveThresholdFromMinChangeForADoubleProperty()
        {
            var cfg = new FakeThrottleConfigured { MinInterval = "250ms", MinChange = "0.5" };

            var policy = ThrottlePolicy.FromConfigured(cfg, typeof(double));

            Assert.AreEqual("0.5", policy.MinChange);
            Assert.IsNotNull(policy.Threshold);
        }

        [TestMethod]
        public void LeaveThresholdNullWhenNoBuiltInForTheValueType()
        {
            // string has no registered IChangeThreshold; MinChange is therefore inert.
            var cfg = new FakeThrottleConfigured { MinInterval = "250ms", MinChange = "0.5" };

            var policy = ThrottlePolicy.FromConfigured(cfg, typeof(string));

            Assert.IsNull(policy.Threshold);
        }

        [TestMethod]
        public void ResolveTheBuiltInThresholdThroughANullableWrapper()
        {
            // double? must resolve the double built-in — matches the DALE034 analyzer, which unwraps
            // Nullable<T> before checking for a threshold. Without the unwrap a deadband on a nullable
            // numeric property silently no-ops at runtime even though the analyzer accepts it.
            var cfg = new FakeThrottleConfigured { MinInterval = "250ms", MinChange = "0.5" };

            var policy = ThrottlePolicy.FromConfigured(cfg, typeof(double?));

            Assert.IsNotNull(policy.Threshold);
            Assert.IsTrue(policy.Threshold!.Exceeds(1.0, 2.0, "0.5"));
        }

        private sealed class FakeThrottleConfigured : IThrottleConfigured
        {
            public string MinInterval { get; set; } = "250ms";

            public string? MinChange { get; set; }

            public bool Immediate { get; set; }
        }
    }
}