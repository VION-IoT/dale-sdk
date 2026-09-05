using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     The two places the recording context meets genuine concurrency, kept apart from the rest of the
    ///     suite because both drive real threads rather than the virtual clock. Neither asserts a wall-clock
    ///     bound: the first caps an unbounded drain so the runner can recover from a regression rather than
    ///     hang, and the assertion is the tick count; the second's outcome is deterministic once every
    ///     producer has completed, however the interleaving fell out.
    /// </summary>
    [TestClass]
    public class RecordingContextConcurrencyShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-TKIT-008.5")]
        public void RunSelfReschedulingTickOncePerFlush()
        {
            // A periodic tick implemented via InvokeSynchronizedAfter (re-arming itself each
            // invocation) used to cause FlushPendingActions to loop forever, allocating GBs.
            // The drain must be single-pass: actions re-queued during the flush are deferred
            // to the next FlushPendingActions call (mirroring production, where SendToSelfAfter
            // honours its delay and only fires later).
            // Arrange
            var block = LogicBlockTestHelper.Create<SelfReschedulingLogicBlock>();
            var testContext = block.CreateTestContext().Build();

            // Ready() has queued one OnTick into _pendingActions. With a fixed flush, it runs
            // once and re-queues its successor; with the buggy flush, it loops indefinitely.
            // Run on a worker thread so the test thread can time-cap it and recover.
            // Act
            var flushTask = Task.Run(testContext.FlushPendingActions);
            var completed = flushTask.Wait(TimeSpan.FromSeconds(2));

            // Cooperative shutdown: if the flush was unbounded, set the stop flag so the
            // background loop drops out the next time it reads it, then wait for it to drain
            // before returning to the test runner. Without this an orphaned thread keeps
            // allocating into subsequent tests.
            block.StopRescheduling = true;
            flushTask.Wait(TimeSpan.FromSeconds(5));

            // Assert
            Assert.IsTrue(completed, "FlushPendingActions must not unboundedly drain when an action re-schedules itself via InvokeSynchronizedAfter.");
            Assert.AreEqual(1, block.TickCount, "OnTick should fire exactly once per FlushPendingActions call.");
        }

        // --- Thread safety: enqueue from any thread, drive from the test thread (VION-63) ---

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-004.1")]
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
            // Arrange
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
            // Act
            do
            {
                Drive();
            }
            while (!enqueueing.IsCompleted);

            DrainRemaining();
            enqueueing.GetAwaiter().GetResult(); // surface a producer-side failure as itself

            // Assert
            Assert.AreEqual(totalActions,
                            runCounts.Count(runs => runs == 1),
                            "Every action enqueued from a background thread must run exactly once — none lost to a snapshot-then-clear gap, none run twice.");
            Assert.HasCount(totalActions,
                            testContext.GetSentMessagesOfTypePublic<LogicBlockBase.InvokeActionMessage>(),
                            "Every action enqueued from a background thread must be recorded exactly once in the sent-message log.");
        }
    }
}