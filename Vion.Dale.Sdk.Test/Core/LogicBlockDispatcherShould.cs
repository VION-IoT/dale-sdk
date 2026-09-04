using System;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     The two members a block schedules its own work through. The claim each test makes is about what
    ///     the block hands its actor — the action wrapper and the delay it is armed at — which the recording
    ///     context of <see cref="LifecycleHarness" /> captures; that the actor then delivers it is
    ///     <c>ActorContextShould</c>'s, over a real actor system.
    /// </summary>
    [TestClass]
    public sealed class LogicBlockDispatcherShould
    {
        private readonly LifecycleHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-006.1")]
        public void ScheduleActionOnBlocksOwnActor()
        {
            // Arrange
            var block = new SchedulingBlock();
            _harness.ConfigureAndStart(block);
            var ran = false;

            // Act
            block.Schedule(() => ran = true);
            var scheduled = _harness.Context.Scheduled.Last().Message;
            _harness.Send(block, scheduled);

            // Assert
            Assert.IsTrue(ran, "The action reaches the block as a message of its own, so it runs on the block's actor and needs no locking.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-006.1")]
        public void ScheduleDelayedActionAtRequestedDelay()
        {
            // Arrange
            var block = new SchedulingBlock();
            _harness.ConfigureAndStart(block);

            // Act
            block.ScheduleAfter(() => { }, TimeSpan.FromSeconds(30));

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(30), _harness.Context.Scheduled.Last().Delay, "A delayed action is armed at the delay the block asked for.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-006.2")]
        [DataRow(-5d, DisplayName = "a delay in the past")]
        [DataRow(0d, DisplayName = "no delay at all")]
        public void ArmDelayAlreadyPastAsDueNow(double seconds)
        {
            // Arrange
            var block = new SchedulingBlock();
            _harness.ConfigureAndStart(block);

            // Act
            block.ScheduleAfter(() => { }, TimeSpan.FromSeconds(seconds));

            // Assert
            Assert.AreEqual(TimeSpan.Zero,
                            _harness.Context.Scheduled.Last().Delay,
                            "A delay in the past used to throw out of the handler and be swallowed, losing the action and the cycle it was rescheduling.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-006.2")]
        public void RefuseDelayLongerThanRealClockCanWait()
        {
            // Arrange
            var block = new SchedulingBlock();
            _harness.ConfigureAndStart(block);

            // Act / Assert
            var refusal = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => block.ScheduleAfter(() => { }, TimeSpan.MaxValue));
            StringAssert.Contains(refusal.Message, "longest a real clock can wait", "The refusal says what the bound is, because the block author has nothing else to go on.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-006.3")]
        public void RefuseSchedulingBeforeBlockHasReceivedFirstMessage()
        {
            // Arrange
            var block = new SchedulingBlock();

            // Act / Assert
            var immediate = Assert.ThrowsExactly<InvalidOperationException>(() => block.Schedule(() => { }));
            var delayed = Assert.ThrowsExactly<InvalidOperationException>(() => block.ScheduleAfter(() => { }, TimeSpan.FromSeconds(1)));
            StringAssert.Contains(immediate.Message, "Ready()", "The refusal names where a block schedules from, which a bare null reference did not.");
            StringAssert.Contains(delayed.Message, "Ready()");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-006.4")]
        public void RunScheduledActionThatComesDueAfterStop()
        {
            // Arrange
            var block = new SchedulingBlock();
            _harness.ConfigureAndStart(block);
            var ran = false;
            block.ScheduleAfter(() => ran = true, TimeSpan.FromSeconds(5));
            var scheduled = _harness.Context.Scheduled.Last().Message;

            // Act
            _harness.Send(block, new StopLogicBlockRequest());
            _harness.Send(block, scheduled);

            // Assert
            Assert.IsTrue(ran, "There is no cancellation, so an action already armed runs when it comes due — a cycle that must not ends by not re-scheduling itself.");
        }

        /// <summary>A block that hands the dispatcher whatever a test gives it.</summary>
        private sealed class SchedulingBlock : LogicBlockBase
        {
            public SchedulingBlock() : base(NullLogger.Instance)
            {
            }

            public void Schedule(Action action)
            {
                InvokeSynchronized(action);
            }

            public void ScheduleAfter(Action action, TimeSpan delay)
            {
                InvokeSynchronizedAfter(action, delay);
            }

            protected override void Ready()
            {
            }
        }
    }
}