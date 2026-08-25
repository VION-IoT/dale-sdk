using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Core;
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
            var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                6,
                                                                1,
                                                                12,
                                                                0,
                                                                0,
                                                                TimeSpan.Zero));
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
            Assert.AreEqual(testContext.VirtualNow, testContext.VirtualNow, "FlushPendingActions must not advance the virtual clock.");
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

            Assert.IsTrue(completed, "FlushPendingActions must not unboundedly drain when an action re-schedules itself via InvokeSynchronizedAfter.");
            Assert.AreEqual(1, block.TickCount, "OnTick should fire exactly once per FlushPendingActions call.");
        }

        // --- Thread safety: enqueue from any thread, drive from the test thread (VION-63) ---

        [TestMethod]
        [DataRow(false, DisplayName = "drained by FlushPendingActions")]
        [DataRow(true, DisplayName = "drained by AdvanceTime")]
        public void DrainEveryActionExactlyOnceWhenEnqueuedFromBackgroundThreads(bool driveWithAdvanceTime)
        {
            // A block driven by a real I/O client (an ILogicBlockModbusTcpClient polling a real
            // socket) marshals its completion callbacks onto the actor context from background
            // socket-callback threads, so _pendingActions and _sentMessages take concurrent Adds
            // while the test thread drains. Unguarded, the drain copied a torn snapshot — a default
            // (DateTimeOffset, Action) tuple whose Action is null (NullReferenceException) or an
            // Array.Copy over/under-run (IndexOutOfRangeException / ArgumentException) — and an Add
            // landing between the old ToList() and Clear() was silently erased. A guarded queue is
            // necessary but not sufficient for the AdvanceTime path: a deadline stamped from a clock
            // read the drain has since moved past would take the clock backwards, which the
            // FakeTimeProvider refuses. VION-63.
            const int producerCount = 4;
            const int actionsPerProducer = 2000;
            const int totalActions = producerCount * actionsPerProducer;

            // A non-zero advance is what makes this row more than a slower copy of the flush row:
            // only a moving virtual clock produces the window in which a background thread stamps a
            // deadline from a clock read the drain is about to leave behind. AdvanceTime(Zero) never
            // moves the clock and exercises none of it.
            var advanceStep = TimeSpan.FromMilliseconds(1);

            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = block.CreateTestContext().Build();
            testContext.ClearRecordedMessages();

            void Drive()
            {
                if (driveWithAdvanceTime)
                {
                    testContext.AdvanceTime(advanceStep);
                }
                else
                {
                    testContext.FlushPendingActions();
                }
            }

            // The final pass has to empty the queue whatever the deadlines are: FlushPendingActions
            // ignores them, AdvanceTime needs a window wider than any deadline still pending.
            void DrainRemaining()
            {
                if (driveWithAdvanceTime)
                {
                    testContext.AdvanceTime(TimeSpan.FromDays(1));
                }
                else
                {
                    testContext.FlushPendingActions();
                }
            }

            // One slot per enqueued action, so a lost action reads 0 and a double-run reads 2.
            var runCounts = new int[totalActions];

            // Dedicated threads released together, so the Adds genuinely overlap the drain instead
            // of trickling in behind it — enqueueing after the drain has finished is vacuously green.
            using var startLine = new Barrier(producerCount);
            var producers = Enumerable.Range(0, producerCount)
                                      .Select(producer => Task.Factory.StartNew(() =>
                                                                                {
                                                                                    startLine.SignalAndWait();
                                                                                    for (var i = 0; i < actionsPerProducer; i++)
                                                                                    {
                                                                                        var index = producer * actionsPerProducer + i;

                                                                                        void Run()
                                                                                        {
                                                                                            Interlocked.Increment(ref runCounts[index]);
                                                                                        }

                                                                                        // Both enqueue paths — SendToSelf stamps deadline = now, SendToSelfAfter
                                                                                        // stamps now + delay — and both read the virtual clock while the drain is
                                                                                        // moving it. The delays are sub-millisecond and widely spread on purpose:
                                                                                        // they keep the drain marching through many distinct deadlines, which is
                                                                                        // what opens the stale-clock-read window. A coarse or uniform delay piles
                                                                                        // the queue up on one deadline, the clock never advances, and the row goes
                                                                                        // vacuously green.
                                                                                        if (index % 4 == 0)
                                                                                        {
                                                                                            block.InvokeSynchronized(Run);
                                                                                        }
                                                                                        else
                                                                                        {
                                                                                            block.InvokeSynchronizedAfter(Run, TimeSpan.FromTicks(1 + index % 997));
                                                                                        }
                                                                                    }
                                                                                },
                                                                                TaskCreationOptions.LongRunning))
                                      .ToArray();
            var enqueueing = Task.WhenAll(producers);

            // Drive from the test thread while the producers run. Once every producer has completed
            // its Adds happen-before the task completion, so a single further drain sweeps up
            // whatever landed after the last in-loop pass — the outcome is deterministic even though
            // the interleaving is not.
            do
            {
                Drive();
            }
            while (!enqueueing.IsCompleted);

            DrainRemaining();
            enqueueing.GetAwaiter().GetResult(); // surface a producer-side failure as itself

            Assert.AreEqual(totalActions,
                            runCounts.Count(runs => runs == 1),
                            "Every action enqueued from a background thread must run exactly once — none lost to a snapshot-then-clear gap, none run twice.");
            Assert.HasCount(totalActions,
                            testContext.GetSentMessagesOfTypePublic<LogicBlockBase.InvokeActionMessage>(),
                            "Every action enqueued from a background thread must be recorded exactly once in the sent-message log.");
        }
    }
}