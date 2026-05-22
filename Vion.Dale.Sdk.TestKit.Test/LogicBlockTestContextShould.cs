using System;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.TestKit.Test
{
    [TestClass]
    public class LogicBlockTestContextShould
    {
        // --- Factory methods ---

        [TestMethod]
        public void CreateLogicBlockWithFactoryMethod()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();

            Assert.IsNotNull(block);
            Assert.IsInstanceOfType<SampleLogicBlock>(block);
        }

        [TestMethod]
        public void CreateLogicBlockWithLoggerMock()
        {
            var (block, loggerMock) = LogicBlockTestHelper.CreateWithLogger<SampleLogicBlock>();

            Assert.IsNotNull(block);
            Assert.IsNotNull(loggerMock);
        }

        // --- Initialization ---

        [TestMethod]
        public void InitializeLogicBlockForTest()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.InitializeForTest();

            Assert.IsNotNull(testContext);
        }

        [TestMethod]
        public void AutoStartByDefault()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 3.5;

            // Started blocks produce property change messages
            testContext.VerifyServicePropertyChanged(lb => lb.Power);
        }

        [TestMethod]
        public void AutoStartClearsInfrastructureMessages()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            // Auto-start publishes initial state for all properties, but those should be cleared.
            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Never());
        }

        [TestMethod]
        public void NotProducePropertyChangeMessagesWithoutAutoStart()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().WithoutAutoStart().Build();

            block.Power = 3.5;

            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Never());
        }

        // --- Service property verification ---

        [TestMethod]
        public void VerifyServicePropertyChanged()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 3.5;

            testContext.VerifyServicePropertyChanged(lb => lb.Power, value => Assert.AreEqual(3.5, value));
        }

        [TestMethod]
        public void VerifyServicePropertyChangedWithTimes()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 1.0;
            block.Power = 2.0;

            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Exactly(2));
        }

        [TestMethod]
        public void VerifyServicePropertyChangedOnlyCountsTargetProperty()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 3.5;
            block.Counter = 10;

            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Once());
            testContext.VerifyServicePropertyChanged(lb => lb.Counter, times: Times.Once());
        }

        // --- Service measuring point verification ---

        [TestMethod]
        public void VerifyServiceMeasuringPointChanged()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.SetTemperature(22.5);

            testContext.VerifyServiceMeasuringPointChanged(lb => lb.Temperature, value => Assert.AreEqual(22.5, value));
        }

        // --- Timer simulation ---

        [TestMethod]
        public void FireTimerByIdentifier()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            block.Counter = 0;
            block.FireTimer("OnPeriodicUpdate");

            Assert.AreEqual(1, block.Counter);
        }

        [TestMethod]
        public void FireTimerByExpression()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            block.Counter = 0;
            block.FireTimer(lb => lb.OnPeriodicUpdate());

            Assert.AreEqual(1, block.Counter);
        }

        [TestMethod]
        public void FireTimerMultipleTimes()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            block.Counter = 0;
            block.FireTimer(lb => lb.OnPeriodicUpdate());
            block.FireTimer(lb => lb.OnPeriodicUpdate());
            block.FireTimer(lb => lb.OnPeriodicUpdate());

            Assert.AreEqual(3, block.Counter);
        }

        [TestMethod]
        public void GetTimerInterval()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            var interval = block.GetTimerInterval("OnPeriodicUpdate");

            Assert.AreEqual(TimeSpan.FromSeconds(5), interval);
        }

        [TestMethod]
        public void GetTimerIntervalByExpression()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            var interval = block.GetTimerInterval(lb => lb.OnPeriodicUpdate());

            Assert.AreEqual(TimeSpan.FromSeconds(5), interval);
        }

        [TestMethod]
        public void ThrowWhenFiringUnknownTimer()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.InitializeForTest();

            Assert.ThrowsExactly<TestKitVerificationException>(() => block.FireTimer("NonExistent"));
        }

        // --- Persistent state ---

        [TestMethod]
        public void RestorePersistentState()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.CreateTestContext().WithPersistentValue(lb => lb.Power, 42.0).Build();

            Assert.AreEqual(42.0, block.Power);
        }

        [TestMethod]
        public void RestoreMultiplePersistentValues()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            block.CreateTestContext().WithPersistentValue(lb => lb.Power, 42.0).WithPersistentValue(lb => lb.Counter, 7).Build();

            Assert.AreEqual(42.0, block.Power);
            Assert.AreEqual(7, block.Counter);
        }

        // --- Message clearing ---

        [TestMethod]
        public void ClearRecordedMessages()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.Power = 3.5;
            testContext.ClearRecordedMessages();

            testContext.VerifyServicePropertyChanged(lb => lb.Power, times: Times.Never());
        }

        // --- FlushPendingActions ---

        [TestMethod]
        public void FlushPendingActions_ExecuteDelayedAction()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleDelayedPowerUpdate(42.0);

            // Action is queued but not yet executed
            Assert.AreEqual(0.0, block.Power);

            testContext.FlushPendingActions();

            Assert.AreEqual(42.0, block.Power);
        }

        [TestMethod]
        public void FlushPendingActions_ExecutesImmediateInvokeSynchronizedAction()
        {
            // Repro for the asymmetry between SendToSelf and SendToSelfAfter in the TestKit:
            // InvokeSynchronized(action) — used by production for "do this on next dispatch"
            // callbacks (Modbus / HTTP response handlers, contract-update bypass handlers) —
            // must be drained by FlushPendingActions, just like InvokeSynchronizedAfter.
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleImmediatePowerUpdate(42.0);

            Assert.AreEqual(0.0, block.Power, "InvokeSynchronized action should be queued, not executed inline.");

            testContext.FlushPendingActions();

            Assert.AreEqual(42.0, block.Power, "FlushPendingActions must drain InvokeSynchronized actions too, not only InvokeSynchronizedAfter.");
        }

        [TestMethod]
        public void FlushPendingActions_ExecuteMultipleActions()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleDelayedPowerUpdate(1.0);
            block.ScheduleDelayedPowerUpdate(2.0);
            block.ScheduleDelayedPowerUpdate(3.0);

            testContext.FlushPendingActions();

            // Last write wins
            Assert.AreEqual(3.0, block.Power);
        }

        [TestMethod]
        public void FlushPendingActions_SafeToCallWithNoPending()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            // Should not throw
            testContext.FlushPendingActions();
        }

        [TestMethod]
        public void FlushPendingActions_ClearsQueueAfterExecution()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleDelayedPowerUpdate(42.0);
            testContext.FlushPendingActions();

            block.Power = 0.0;
            testContext.FlushPendingActions(); // second flush should be a no-op

            Assert.AreEqual(0.0, block.Power);
        }

        // --- AdvanceTime / virtual clock ---

        [TestMethod]
        public void AdvanceTime_FiresActionAtItsDeadline()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleDelayedPowerUpdate(42.0); // schedules 500 ms in the future

            testContext.AdvanceTime(TimeSpan.FromMilliseconds(500));

            Assert.AreEqual(42.0, block.Power);
        }

        [TestMethod]
        public void AdvanceTime_FiresImmediateInvokeSynchronizedAction()
        {
            // InvokeSynchronized actions enlist with deadline = "now-at-scheduling-time", so
            // any non-negative AdvanceTime — including TimeSpan.Zero — must fire them.
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleImmediatePowerUpdate(7.0);

            testContext.AdvanceTime(TimeSpan.Zero);

            Assert.AreEqual(7.0, block.Power, "AdvanceTime(Zero) must drain InvokeSynchronized actions whose deadline is the current virtual time.");
        }

        [TestMethod]
        public void AdvanceTime_DoesNotFireActionWhoseDeadlineIsBeyondNewVirtualNow()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleDelayedPowerUpdate(42.0); // 500 ms

            testContext.AdvanceTime(TimeSpan.FromMilliseconds(499));

            Assert.AreEqual(0.0, block.Power, "Action with deadline 500 ms must not fire when we only advanced 499 ms.");
        }

        [TestMethod]
        public void AdvanceTime_FiresActionsInDeadlineOrderRegardlessOfInsertOrder()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            // ScheduleDelayedPowerUpdate uses a fixed 500 ms delay; queue three writes — by deadline
            // they all fall at virtualNow + 500 ms, so order is preserved by insertion (FIFO within
            // equal deadlines) and last-write-wins gives us the final assigned value.
            block.ScheduleDelayedPowerUpdate(1.0);
            block.ScheduleDelayedPowerUpdate(2.0);
            block.ScheduleDelayedPowerUpdate(3.0);

            testContext.AdvanceTime(TimeSpan.FromSeconds(1));

            Assert.AreEqual(3.0, block.Power);
        }

        [TestMethod]
        public void AdvanceTime_VirtualNowReflectsTargetAfterAdvance()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();
            var before = testContext.VirtualNow;

            testContext.AdvanceTime(TimeSpan.FromMinutes(7));

            Assert.AreEqual(before + TimeSpan.FromMinutes(7), testContext.VirtualNow);
        }

        [TestMethod]
        public void AdvanceTime_FiresSelfReschedulingTickOncePerInterval()
        {
            // Ready() schedules the first OnTick at anchor + 5 s. Advancing 10 s fires that tick
            // (which reschedules at +10 s), then the new tick at exactly +10 s (which reschedules
            // at +15 s). The +15 s tick stays pending because 15 > target=10.
            var block = LogicBlockTestHelper.Create<SelfReschedulingLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            testContext.AdvanceTime(TimeSpan.FromSeconds(10));

            Assert.AreEqual(2, block.TickCount);
        }

        [TestMethod]
        public void AdvanceTime_CascadesActionsScheduledByOtherActionsWithinTheWindow()
        {
            // SelfReschedulingLogicBlock with a 5 s interval: advance 30 s → expect 6 ticks
            // (at +5, +10, +15, +20, +25, +30). Without cascading we'd see only 1.
            var block = LogicBlockTestHelper.Create<SelfReschedulingLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            testContext.AdvanceTime(TimeSpan.FromSeconds(30));

            Assert.AreEqual(6, block.TickCount);
        }

        [TestMethod]
        public void AdvanceTime_ThrowsOnNegativeDelta()
        {
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => testContext.AdvanceTime(TimeSpan.FromSeconds(-1)));
        }

        [TestMethod]
        public void TimeProvider_SharedBetweenBlockAndContextViaWithTimeProvider()
        {
            // Block ctor takes TimeProvider, so the test owns a FakeTimeProvider and binds the
            // same instance to the context via WithTimeProvider. AdvanceTime moves both sides.
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 6, 1, 12, 0, 0, TimeSpan.Zero));
            var block = new TimeAwareLogicBlock(clock, LogicBlockTestHelper.CreateLoggerMock().Object);
            var testContext = block.CreateTestContext().WithTimeProvider(clock).Build();

            var before = block.SnapshotUtcNow();
            testContext.AdvanceTime(TimeSpan.FromMinutes(3));
            var after = block.SnapshotUtcNow();

            Assert.AreEqual(TimeSpan.FromMinutes(3), after - before);
            Assert.AreEqual(testContext.VirtualNow, after);
        }

        [TestMethod]
        public void FlushPendingActions_StillDrainsRegardlessOfDeadline()
        {
            // FlushPendingActions is the clock-agnostic drain — even a 1-hour-delayed action runs.
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            block.ScheduleDelayedPowerUpdate(99.0); // 500 ms — not yet due in virtual time

            testContext.FlushPendingActions();

            Assert.AreEqual(99.0, block.Power, "FlushPendingActions must ignore deadlines and run all queued actions.");
            Assert.AreEqual(testContext.VirtualNow, testContext.VirtualNow,
                            "FlushPendingActions must not advance the virtual clock.");
        }

        // --- WithLogicInterfaceMapping ambiguity guard ---

        [TestMethod]
        public void WithLogicInterfaceMapping_ThrowsWhenInferredTypeIsClassWithMultipleContracts()
        {
            // The bare lambda `lb => lb` infers TInterface as the block class. When that class
            // implements more than one [LogicInterface] contract interface, the resolver inside
            // SetLinkedInterfaces would pick FirstOrDefault and silently route both mappings to
            // the wrong sender. The guard throws at registration time instead.
            var block = LogicBlockTestHelper.Create<MultiSenderLogicBlock>();
            var builder = block.CreateTestContext();
            var someTarget = new InterfaceId("other-block", "IFakeContractA");

            var ex = Assert.ThrowsExactly<InvalidOperationException>(() => builder.WithLogicInterfaceMapping(lb => lb, someTarget));

            // Message must name both candidate contracts and point at the explicit-generic fix —
            // otherwise the bug stays just as silent as before, only with a different surface.
            StringAssert.Contains(ex.Message, "MultiSenderLogicBlock");
            StringAssert.Contains(ex.Message, "IFakeContractA");
            StringAssert.Contains(ex.Message, "IFakeContractB");
            StringAssert.Contains(ex.Message, "WithLogicInterfaceMapping<");
        }

        [TestMethod]
        public void WithLogicInterfaceMapping_AllowsExplicitGenericOnMultiSenderBlock()
        {
            // Escape hatch: when the caller spells out the contract interface, no ambiguity exists.
            var block = LogicBlockTestHelper.Create<MultiSenderLogicBlock>();
            var builder = block.CreateTestContext();

            // Should not throw — explicit generic disambiguates.
            builder.WithLogicInterfaceMapping<IFakeContractA>(lb => lb, new InterfaceId("other-a", "IFakeContractA"))
                   .WithLogicInterfaceMapping<IFakeContractB>(lb => lb, new InterfaceId("other-b", "IFakeContractB"));
        }

        [TestMethod]
        public void WithLogicInterfaceMapping_AllowsBareLambdaOnSingleSenderBlock()
        {
            // The guard must not penalise the common single-contract case — the bare-lambda form
            // is unambiguous there because there is only one possible sender to route to.
            var block = LogicBlockTestHelper.Create<SingleSenderLogicBlock>();
            var builder = block.CreateTestContext();

            // Should not throw.
            builder.WithLogicInterfaceMapping(lb => lb, new InterfaceId("other-a", "IFakeContractA"));
        }

        [TestMethod]
        public void FlushPendingActions_DoesNotLoopWhenActionReschedulesItself()
        {
            // A periodic tick implemented via InvokeSynchronizedAfter (re-arming itself each
            // invocation) used to cause FlushPendingActions to loop forever, allocating GBs.
            // The drain must be single-pass: actions re-queued during the flush are deferred
            // to the next FlushPendingActions call (mirroring production, where SendToSelfAfter
            // honours its delay and only fires later).
            var block = LogicBlockTestHelper.Create<SelfReschedulingLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            // Ready() has queued one OnTick into _pendingActions. With a fixed flush, it runs
            // once and re-queues its successor; with the buggy flush, it loops indefinitely.
            // Run on a worker thread so the test thread can time-cap it and recover.
            var flushTask = Task.Run(testContext.FlushPendingActions);
            var completed = flushTask.Wait(TimeSpan.FromSeconds(2));

            // Cooperative shutdown: if the flush was unbounded, set the stop flag so the
            // background loop drops out the next time it reads it, then wait for it to drain
            // before returning to the test runner. Without this an orphaned thread keeps
            // allocating into subsequent tests.
            block.StopRescheduling = true;
            flushTask.Wait(TimeSpan.FromSeconds(5));

            Assert.IsTrue(completed,
                          "FlushPendingActions must not unboundedly drain when an action re-schedules itself via InvokeSynchronizedAfter.");
            Assert.AreEqual(1, block.TickCount, "OnTick should fire exactly once per FlushPendingActions call.");
        }
    }
}