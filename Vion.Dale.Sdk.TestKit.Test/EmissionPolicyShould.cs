using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     The emission policy as a block author observes it: when it applies at all, what reaches the
    ///     handlers while a block runs, and what it publishes at each edge of the block's life. Every case
    ///     runs on virtual time, so the trailing-edge releases are exact rather than raced.
    /// </summary>
    [TestClass]
    public class EmissionPolicyShould
    {
        private static readonly TimeSpan DefaultInterval = TimeSpan.FromMilliseconds(250);

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-001.2")]
        public void PublishEveryChangeOnAClockItCanAdvance()
        {
            // Arrange — the TestKit's own clock, and no override.
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var context = block.CreateTestContext().Build();

            // Act — three changes well inside the member's 250 ms interval.
            block.Voltage = 1.0;
            block.Voltage = 2.0;
            block.Voltage = 3.0;

            // Assert
            context.VerifyServicePropertyChanged(lb => lb.Voltage, times: Times.Exactly(3));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-001.1")]
        public void ApplyThePolicyOnAClockItCannotAdvance()
        {
            // Arrange — a clock with no Advance(TimeSpan), so the block cannot take it for a test clock.
            // No override is registered: the clock alone must turn the policy on.
            var clock = new UnadvanceableClock(new DateTimeOffset(2026, 6, 22, 0, 0, 0, TimeSpan.Zero));
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var context = block.CreateTestContext().WithServices(services => services.AddSingleton<TimeProvider>(clock)).Build();
            clock.MoveOn(DefaultInterval);

            // Act
            block.Voltage = 1.0;
            block.Voltage = 2.0;
            block.Voltage = 3.0;

            // Assert — the leading edge only; the rest are held.
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, value => Assert.AreEqual(1.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-001.3")]
        public void ApplyThePolicyOnAnAdvanceableClockWhenOverridden()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);

            // Act
            block.Voltage = 1.0;
            block.Voltage = 2.0;
            block.Voltage = 3.0;

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, value => Assert.AreEqual(1.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-004.1")]
        public void SuppressAValueEqualToTheOneLastPublished()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SettableBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);

            // Act — the same value twice, an interval apart, so only the dedup floor can suppress it.
            block.SetVoltage(5.0);
            context.AdvanceTime(DefaultInterval);
            block.SetVoltage(5.0);

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, value => Assert.AreEqual(5.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-011.1")]
        public void PublishNothingBeforeTheBlockStarts()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).WithoutAutoStart().Build();

            // Act
            block.Voltage = 7.0;

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, times: Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-011.2")]
        public void PublishEveryMembersValueWhenTheBlockStarts()
        {
            // Arrange — a value assigned before start reaches no one, so the start publish is the only
            // thing that can carry it.
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).WithoutAutoStart().Build();
            block.Voltage = 7.0;

            // Act
            block.HandleMessageAsync(new StartLogicBlockRequest(), context).GetAwaiter().GetResult();

            // Assert — published at once, not held until the interval elapses.
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, value => Assert.AreEqual(7.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-003.3")]
        public void RefuseToStartWhenAnIntervalIsNotADuration()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<UnparseableIntervalBlock>();

            // Act / Assert — the gates are built at start, so a knob DALE036 would have rejected fails
            // there rather than being defaulted away.
            Assert.ThrowsExactly<FormatException>(() => block.CreateTestContext().Build());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-005.4")]
        [TestProperty("spec", "AC-EMIT-010.3")]
        public void ReleaseTheHeldValueWhenTheIntervalElapses()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);

            // Act
            block.Voltage = 1.0;
            block.Voltage = 2.0;
            block.Voltage = 3.0;
            context.AdvanceTime(DefaultInterval);

            // Assert — the leading edge, then the latest held value.
            var published = context.GetSentMessagesOfTypePublic<ServicePropertyValueChanged>().Select(m => m.Value).ToList();
            CollectionAssert.AreEqual(new object[] { 1.0, 3.0 }, published);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-010.1")]
        [TestProperty("spec", "AC-EMIT-010.2")]
        public void ReleaseEachMemberAtItsOwnDeadline()
        {
            // Arrange — two members whose intervals differ by eight times.
            var block = LogicBlockTestHelper.Create<TwoRateBlock>();
            var context = Forced(block);
            context.AdvanceTime(TimeSpan.FromSeconds(2));
            block.SetFast(1.0);
            block.SetSlow(1.0);
            block.SetFast(2.0);
            block.SetSlow(2.0);

            // Act — only the fast member's deadline has passed.
            context.AdvanceTime(DefaultInterval);

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Fast, times: Times.Exactly(2));
            context.VerifyServicePropertyEmitted(lb => lb.Slow, times: Times.Once());

            // Act — the slow member's deadline passes, and the block re-armed for it.
            context.AdvanceTime(TimeSpan.FromSeconds(2));

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Slow, times: Times.Exactly(2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-010.4")]
        public void ReleaseNothingAfterTheBlockStops()
        {
            // Arrange — a held value whose deadline has not arrived when the block stops.
            var block = LogicBlockTestHelper.Create<SettableBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);
            block.SetVoltage(1.0);
            block.SetVoltage(9.0);
            block.HandleMessageAsync(new StopLogicBlockRequest(), context).GetAwaiter().GetResult();
            context.ClearRecordedMessages();

            // Act
            context.AdvanceTime(DefaultInterval);

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, times: Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-011.4")]
        public void PublishAPropertysValueAgainWhenAskedToRepublish()
        {
            // Arrange — after a reconnect the gate believes a value was delivered that was not, so the
            // dedup floor would suppress the re-assertion.
            var block = LogicBlockTestHelper.Create<SettableBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);
            block.SetVoltage(5.0);
            context.ClearRecordedMessages();

            // Act
            block.HandleMessageAsync(new PublishServiceState(), context).GetAwaiter().GetResult();

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, value => Assert.AreEqual(5.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-011.4")]
        public void PublishAMeasuringPointsValueAgainWhenAskedToRepublish()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<MeasuredBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);
            block.SetFrequency(50.0);
            context.ClearRecordedMessages();

            // Act
            block.HandleMessageAsync(new PublishServiceState(), context).GetAwaiter().GetResult();

            // Assert
            context.VerifyServiceMeasuringPointEmitted(lb => lb.Frequency, value => Assert.AreEqual(50.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-011.5")]
        public void PublishTheExactCurrentValueWhenTheBlockStops()
        {
            // Arrange — 9.0 is held inside the interval and has never reached anyone.
            var block = LogicBlockTestHelper.Create<SettableBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);
            block.SetVoltage(1.0);
            block.SetVoltage(9.0);
            context.ClearRecordedMessages();

            // Act — stop without letting the interval elapse.
            block.HandleMessageAsync(new StopLogicBlockRequest(), context).GetAwaiter().GetResult();

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, value => Assert.AreEqual(9.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-011.5")]
        public void PublishNothingOnStopForAMemberAlreadyUpToDate()
        {
            // Arrange — the current value is the one last published, so there is nothing to correct.
            var block = LogicBlockTestHelper.Create<SettableBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);
            block.SetVoltage(1.0);
            context.ClearRecordedMessages();

            // Act
            block.HandleMessageAsync(new StopLogicBlockRequest(), context).GetAwaiter().GetResult();

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, times: Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-011.6")]
        public void PublishNothingExtraOnStopWhenThePolicyIsNotApplied()
        {
            // Arrange — no override, so every assignment was already published as it happened.
            var block = LogicBlockTestHelper.Create<SettableBlock>();
            var context = block.CreateTestContext().Build();
            block.SetVoltage(1.0);
            block.SetVoltage(9.0);
            context.ClearRecordedMessages();

            // Act
            block.HandleMessageAsync(new StopLogicBlockRequest(), context).GetAwaiter().GetResult();

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, times: Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-011.7")]
        public void PublishTheRemainingMembersWhenOneCannotBeRead()
        {
            // Arrange — Faulty's getter starts throwing inside Stopping(), which runs before the block
            // publishes its final values.
            var block = LogicBlockTestHelper.Create<FaultyReadBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);
            block.SetVoltage(1.0);
            block.SetVoltage(9.0);
            context.ClearRecordedMessages();

            // Act
            block.HandleMessageAsync(new StopLogicBlockRequest(), context).GetAwaiter().GetResult();

            // Assert — Faulty is bound first, so its throw happens before Voltage is read.
            context.VerifyServicePropertyEmitted(lb => lb.Voltage, value => Assert.AreEqual(9.0, value), Times.Once());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-011.8")]
        public void PublishFinalValuesBeforeClearingRetainedState()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SettableBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);
            block.SetVoltage(1.0);
            block.SetVoltage(9.0);
            context.ClearRecordedMessages();

            // Act
            block.HandleMessageAsync(new StopLogicBlockRequest(), context).GetAwaiter().GetResult();

            // Assert — the final value must reach a consumer while it is still listening, so it precedes
            // the clear that wipes the retained state.
            var order = context.GetSentMessagesOfTypePublic<object>().Select(m => m.GetType().Name).ToList();
            Assert.IsLessThan(order.IndexOf(nameof(ServicePropertyValueCleared)), order.IndexOf(nameof(ServicePropertyValueChanged)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.1")]
        public void GateAMeasuringPointTheSameWayAsAServiceProperty()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<MeasuredBlock>();
            var context = Forced(block);
            context.AdvanceTime(DefaultInterval);

            // Act
            block.SetFrequency(50.0);
            block.SetFrequency(50.1);
            block.SetFrequency(50.2);
            context.AdvanceTime(DefaultInterval);

            // Assert
            var published = context.GetSentMessagesOfTypePublic<ServiceMeasuringPointValueChanged>().Select(m => m.Value).ToList();
            CollectionAssert.AreEqual(new object[] { 50.0, 50.2 }, published);
        }

        private static LogicBlockTestContext<TBlock> Forced<TBlock>(TBlock block)
            where TBlock : LogicBlockBase
        {
            return block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();
        }

        // A clock a block cannot recognise as a test clock: it moves, but not through Advance(TimeSpan).
        private sealed class UnadvanceableClock : TimeProvider
        {
            private DateTimeOffset _now;

            public UnadvanceableClock(DateTimeOffset now)
            {
                _now = now;
            }

            public void MoveOn(TimeSpan delta)
            {
                _now += delta;
            }

            public override DateTimeOffset GetUtcNow()
            {
                return _now;
            }
        }

        // DALE036 rejects this at compile time; suppressed here to reach the start-time backstop.
        private sealed class UnparseableIntervalBlock : LogicBlockBase
        {
#pragma warning disable DALE036
            [ServiceProperty(MinInterval = "soon")]
            public double Voltage { get; set; }
#pragma warning restore DALE036

            public UnparseableIntervalBlock(ILogger logger) : base(logger)
            {
            }

            protected override void Ready()
            {
            }
        }

        private sealed class ThrottledBlock : LogicBlockBase
        {
            [ServiceProperty(MinInterval = "250ms")]
            public double Voltage { get; set; }

            public ThrottledBlock(ILogger logger) : base(logger)
            {
            }

            protected override void Ready()
            {
            }
        }

        // The same member, written through a method so a test can drive a read-only property the way a
        // block's own logic does.
        private sealed class SettableBlock : LogicBlockBase
        {
            [ServiceProperty(MinInterval = "250ms")]
            public double Voltage { get; private set; }

            public SettableBlock(ILogger logger) : base(logger)
            {
            }

            public void SetVoltage(double value)
            {
                Voltage = value;
            }

            protected override void Ready()
            {
            }
        }

        private sealed class MeasuredBlock : LogicBlockBase
        {
            [ServiceMeasuringPoint(MinInterval = "250ms")]
            public double Frequency { get; private set; }

            public MeasuredBlock(ILogger logger) : base(logger)
            {
            }

            public void SetFrequency(double value)
            {
                Frequency = value;
            }

            protected override void Ready()
            {
            }
        }

        private sealed class TwoRateBlock : LogicBlockBase
        {
            [ServiceProperty(MinInterval = "250ms")]
            public double Fast { get; private set; }

            [ServiceProperty(MinInterval = "2s")]
            public double Slow { get; private set; }

            public TwoRateBlock(ILogger logger) : base(logger)
            {
            }

            public void SetFast(double value)
            {
                Fast = value;
            }

            public void SetSlow(double value)
            {
                Slow = value;
            }

            protected override void Ready()
            {
            }
        }

        // Faulty reads fine until the block is told to stop, so the block starts normally and then meets a
        // member it cannot read while publishing its final values.
        private sealed class FaultyReadBlock : LogicBlockBase
        {
            private bool _stopping;

            [ServiceProperty(MinInterval = "250ms")]
            public double Faulty => _stopping ? throw new InvalidOperationException() : 1.0;

            [ServiceProperty(MinInterval = "250ms")]
            public double Voltage { get; private set; }

            public FaultyReadBlock(ILogger logger) : base(logger)
            {
            }

            public void SetVoltage(double value)
            {
                Voltage = value;
            }

            protected override void Ready()
            {
            }

            protected override void Stopping()
            {
                _stopping = true;
            }
        }
    }
}
