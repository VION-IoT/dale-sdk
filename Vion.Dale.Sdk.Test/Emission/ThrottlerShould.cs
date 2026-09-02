using System;
using System.Collections.Immutable;
using Vion.Dale.Sdk.Emission;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Emission
{
    /// <summary>
    ///     The per-member emission gate: the decision it reaches for one offered value, and the held value
    ///     it releases when an interval expires. The gate is pure — the caller supplies <c>now</c> — so
    ///     every timing case here is exact rather than raced.
    /// </summary>
    [TestClass]
    public class ThrottlerShould
    {
        private static readonly DateTimeOffset T0 = new(2026,
                                                        6,
                                                        22,
                                                        0,
                                                        0,
                                                        0,
                                                        TimeSpan.Zero);

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.1")]
        [DataRow("250ms", null, false, DisplayName = "default policy")]
        [DataRow("250ms", "1.0", false, DisplayName = "with a deadband")]
        [DataRow("0", null, false, DisplayName = "throttling disabled")]
        [DataRow("250ms", null, true, DisplayName = "immediate")]
        public void SuppressValueEqualToLastEmitted(string minInterval, string? minChange, bool immediate)
        {
            // Arrange
            var throttler = new Throttler(Policy(minInterval, minChange, immediate));
            throttler.Offer(5.0d, T0);

            // Act
            var result = throttler.Offer(5.0d, T0 + TimeSpan.FromSeconds(10));

            // Assert
            Assert.AreEqual(EmitAction.Drop, result.Action);
            Assert.IsFalse(throttler.HasPending);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.2")]
        public void SuppressRebuiltButIdenticalTable()
        {
            // Arrange
            var throttler = new Throttler(Policy(valueType: typeof(ImmutableArray<Row>)));
            throttler.Offer(ImmutableArray.Create(new Row(1, "x"), new Row(2, "y")), T0);

            // Act
            var result = throttler.Offer(ImmutableArray.Create(new Row(1, "x"), new Row(2, "y")), T0 + TimeSpan.FromSeconds(10));

            // Assert
            Assert.AreEqual(EmitAction.Drop, result.Action);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.6")]
        public void EmitWithinIntervalWhenImmediate()
        {
            // Arrange — an interval and a deadband that would both suppress the second value.
            var throttler = new Throttler(Policy("250ms", "1.0", true));
            throttler.Offer(10.0d, T0);

            // Act
            var result = throttler.Offer(10.1d, T0 + TimeSpan.FromMilliseconds(10));

            // Assert
            Assert.AreEqual(EmitAction.Emit, result.Action);
            Assert.AreEqual(10.1d, throttler.LastEmitted);
            Assert.IsFalse(throttler.HasPending);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.7")]
        public void DiscardHeldValueWhenLaterOneReturnsToPublishedValue()
        {
            // Arrange — 2.0 is held inside the interval.
            var throttler = new Throttler(Policy());
            throttler.Offer(1.0d, T0);
            throttler.Offer(2.0d, T0 + TimeSpan.FromMilliseconds(50));

            // Act — the member is back at the value the consumer already has.
            var result = throttler.Offer(1.0d, T0 + TimeSpan.FromMilliseconds(100));

            // Assert — releasing 2.0 later would move the consumer away from the current value.
            Assert.AreEqual(EmitAction.Drop, result.Action);
            Assert.IsFalse(throttler.HasPending);
            Assert.IsFalse(throttler.TryFlush(T0 + TimeSpan.FromMilliseconds(250), out _));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.7")]
        public void DiscardHeldValueWhenLaterOneFallsInsideDeadband()
        {
            // Arrange — 12.0 cleared the deadband and is held inside the interval.
            var throttler = new Throttler(Policy("250ms", "1.0"));
            throttler.Offer(10.0d, T0);
            throttler.Offer(12.0d, T0 + TimeSpan.FromMilliseconds(50));

            // Act — the member settles back near where it was published.
            var result = throttler.Offer(10.4d, T0 + TimeSpan.FromMilliseconds(100));

            // Assert
            Assert.AreEqual(EmitAction.Drop, result.Action);
            Assert.IsFalse(throttler.HasPending);
            Assert.IsFalse(throttler.TryFlush(T0 + TimeSpan.FromMilliseconds(250), out _));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-006.1")]
        public void SuppressValueInsideDeadband()
        {
            // Arrange
            var throttler = new Throttler(Policy("250ms", "1.0"));
            throttler.Offer(10.0d, T0);

            // Act — past the interval, so only the deadband can suppress it.
            var result = throttler.Offer(10.4d, T0 + TimeSpan.FromSeconds(1));

            // Assert — suppressed outright, not held for a later flush.
            Assert.AreEqual(EmitAction.Drop, result.Action);
            Assert.IsFalse(throttler.HasPending);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-006.2")]
        public void EmitRampOnceAccumulatedDriftReachesThreshold()
        {
            // Arrange — throttling disabled, so only the deadband decides.
            var throttler = new Throttler(Policy("0", "1.0"));
            throttler.Offer(10.0d, T0);

            // Act — steps of 0.4, none of which reaches the threshold on its own.
            var actions = new EmitAction[3];
            for (var step = 0; step < actions.Length; step++)
            {
                actions[step] = throttler.Offer(10.0d + 0.4d * (step + 1), T0 + TimeSpan.FromSeconds(step + 1)).Action;
            }

            // Assert — measured against the last EMITTED value, so the drift accumulates and the third step
            // clears it. Compared against the previously OFFERED value, every step would be 0.4 and the ramp
            // would never emit at all.
            CollectionAssert.AreEqual(new[] { EmitAction.Drop, EmitAction.Drop, EmitAction.Emit }, actions);
            Assert.AreEqual(11.2d, throttler.LastEmitted);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-006.1")]
        public void EmitValueClearingDeadband()
        {
            // Arrange
            var throttler = new Throttler(Policy("250ms", "1.0"));
            throttler.Offer(10.0d, T0);

            // Act
            var result = throttler.Offer(12.0d, T0 + TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(EmitAction.Emit, result.Action);
            Assert.AreEqual(12.0d, throttler.LastEmitted);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-008.3")]
        public void ApplyUnderlyingTypesDeadbandOnNullableMember()
        {
            // Arrange — a nullable member resolves its underlying type's deadband, matching DALE034, which
            // unwraps before deciding whether one exists.
            var throttler = new Throttler(Policy("250ms", "1.0", valueType: typeof(double?)));
            throttler.Offer(10.0d, T0);

            // Act
            var result = throttler.Offer(10.4d, T0 + TimeSpan.FromSeconds(1));

            // Assert
            Assert.AreEqual(EmitAction.Drop, result.Action);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.1")]
        public void EmitFirstValueOffered()
        {
            // Arrange — a long interval and a deadband, neither of which may apply to the first value.
            var throttler = new Throttler(Policy("1h", "1000"));

            // Act
            var result = throttler.Offer(1.0d, T0);

            // Assert
            Assert.AreEqual(EmitAction.Emit, result.Action);
            Assert.IsTrue(throttler.HasEmitted);
            Assert.AreEqual(1.0d, throttler.LastEmitted);
            Assert.IsFalse(throttler.HasPending);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.2")]
        public void EmitOnceIntervalElapsed()
        {
            // Arrange
            var throttler = new Throttler(Policy());
            throttler.Offer(1.0d, T0);

            // Act
            var result = throttler.Offer(2.0d, T0 + TimeSpan.FromMilliseconds(250));

            // Assert
            Assert.AreEqual(EmitAction.Emit, result.Action);
            Assert.AreEqual(2.0d, throttler.LastEmitted);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.3")]
        public void HoldLatestValueWithinInterval()
        {
            // Arrange
            var throttler = new Throttler(Policy());
            throttler.Offer(1.0d, T0);

            // Act
            var firstHold = throttler.Offer(2.0d, T0 + TimeSpan.FromMilliseconds(50));
            var secondHold = throttler.Offer(3.0d, T0 + TimeSpan.FromMilliseconds(100));

            // Assert — both held to the same deadline, and the later value replaced the earlier.
            Assert.AreEqual(EmitAction.Hold, firstHold.Action);
            Assert.AreEqual(EmitAction.Hold, secondHold.Action);
            Assert.AreEqual(T0 + TimeSpan.FromMilliseconds(250), secondHold.Deadline);
            Assert.AreEqual(T0 + TimeSpan.FromMilliseconds(250), throttler.PendingDeadline);
            Assert.IsTrue(throttler.TryFlush(T0 + TimeSpan.FromMilliseconds(250), out var flushed));
            Assert.AreEqual(3.0d, flushed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.4")]
        public void ReleaseHeldValueAndClearHold()
        {
            // Arrange
            var throttler = new Throttler(Policy());
            throttler.Offer(1.0d, T0);
            throttler.Offer(3.0d, T0 + TimeSpan.FromMilliseconds(50));

            // Act
            var released = throttler.TryFlush(T0 + TimeSpan.FromMilliseconds(250), out var value);

            // Assert
            Assert.IsTrue(released);
            Assert.AreEqual(3.0d, value);
            Assert.AreEqual(3.0d, throttler.LastEmitted);
            Assert.IsFalse(throttler.HasPending);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.4")]
        public void ReleaseNothingWhenNoValueHeld()
        {
            // Arrange
            var throttler = new Throttler(Policy());
            throttler.Offer(1.0d, T0);

            // Act
            var released = throttler.TryFlush(T0 + TimeSpan.FromSeconds(1), out var value);

            // Assert
            Assert.IsFalse(released);
            Assert.IsNull(value);
            Assert.AreEqual(1.0d, throttler.LastEmitted);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.5")]
        [DataRow("0")]
        [DataRow("0ms")]
        public void EmitEveryDistinctValueWhenThrottlingDisabled(string minInterval)
        {
            // Arrange
            var throttler = new Throttler(Policy(minInterval));
            throttler.Offer(1.0d, T0);

            // Act — same instant, so only a disabled interval can let these through.
            var second = throttler.Offer(2.0d, T0);
            var third = throttler.Offer(3.0d, T0);

            // Assert
            Assert.AreEqual(EmitAction.Emit, second.Action);
            Assert.AreEqual(EmitAction.Emit, third.Action);
            Assert.IsFalse(throttler.HasPending);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.5")]
        public void SuppressInsideDeadbandWhenThrottlingDisabled()
        {
            // Arrange
            var throttler = new Throttler(Policy("0", "1.0"));
            throttler.Offer(10.0d, T0);

            // Act
            var result = throttler.Offer(10.2d, T0);

            // Assert
            Assert.AreEqual(EmitAction.Drop, result.Action);
        }

        private static ThrottlePolicy Policy(string minInterval = "250ms", string? minChange = null, bool immediate = false, Type? valueType = null)
        {
            return ThrottlePolicy.FromConfigured(new ThrottleKnobs { MinInterval = minInterval, MinChange = minChange, Immediate = immediate }, valueType ?? typeof(double));
        }

        private readonly record struct Row(int Index, string Name);
    }
}