using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     Regression tests for deterministic multi-cycle stepping via the exact quiescence barrier.
    ///     A registered <see cref="FakeTimeProvider" /> drives real <c>[Timer]</c> ticks on the
    ///     real-wired DevHost / Proto.Actor system: advancing the fake clock completes the outstanding
    ///     <c>Task.Delay(delay, clock)</c> immediately, re-entering the actor on a real thread.
    ///     After each advance the stepper waits for the actor system to quiesce before the next
    ///     advance. Quiescence is determined by the EXACT predicate
    ///     <c>Σ MailboxDepth == 0 AND InFlight == 0</c>: every mailbox empty AND no user handler
    ///     currently executing. Each cycle boundary therefore lands on a settled, reproducible state
    ///     and N cycles yield the same result run-to-run.
    /// </summary>
    [TestClass]
    public class DeterministicSteppingShould
    {
        /// <summary>
        ///     50 runs × 5 cycles on a single-block ticker: every run must land on exactly 5 ticks.
        ///     The first tick is scheduled at startup with a +1s fake delay, so <c>Advance(1s)</c>
        ///     yields <c>Ticks == 1</c>; five advances yield five. Any deviation means the barrier
        ///     has a gap — not something to paper over with retries.
        /// </summary>
        [TestMethod]
        public async Task StepDeterministically_AcrossManyRuns()
        {
            for (var run = 0; run < 50; run++)
            {
                var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                    1,
                                                                    1,
                                                                    0,
                                                                    0,
                                                                    0,
                                                                    TimeSpan.Zero));

                var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();

                await using var host = DevHostBuilder.Create()
                                                     .WithDi<TestDependencyInjection>()
                                                     .WithConfiguration(config)
                                                     .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                     .Build();

                await host.StartAsync();

                // 5 deterministic cycles: each advances the fake clock 1 s and waits for quiescence.
                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1), 5);

                Assert.AreEqual(5, (int)host.Control.GetProperty("ticker", "Ticks")!, $"run {run}: 5 deterministic cycles must yield exactly 5 ticks every time.");
            }
        }

        /// <summary>
        ///     Single-tick proof: <c>AdvanceAsync(1s, 1)</c> advances the fake clock one interval and
        ///     waits for the actor system to settle. Exactly one tick must land, deterministically.
        /// </summary>
        [TestMethod]
        public async Task Advance_DrivesOneTimerTick()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                1,
                                                                1,
                                                                0,
                                                                0,
                                                                0,
                                                                TimeSpan.Zero));

            var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();

            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(config)
                                                 .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                 .Build();

            await host.StartAsync();

            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1), 1);

            Assert.AreEqual(1,
                            (int)host.Control.GetProperty("ticker", "Ticks")!,
                            "Advancing the fake clock by one interval and waiting for quiescence must fire exactly one timer tick.");
        }

        /// <summary>
        ///     Stepping on a real (non-fake) clock is meaningless and must fail loudly rather than hang
        ///     or silently no-op. Building the host WITHOUT registering a FakeTimeProvider leaves the
        ///     real <see cref="TimeProvider.System" /> in place; the first stepping call must throw.
        /// </summary>
        [TestMethod]
        public async Task AdvanceAsync_OnRealClock_Throws()
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1), 1));
        }

        /// <summary>
        ///     Verify that <c>Task.Delay</c> with a <see cref="FakeTimeProvider" /> fires immediately
        ///     on <c>Advance</c> — this isolates the underlying mechanism from the actor system.
        /// </summary>
        [TestMethod]
        public async Task FakeTimeProvider_TaskDelay_FiresOnAdvance()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                1,
                                                                1,
                                                                0,
                                                                0,
                                                                0,
                                                                TimeSpan.Zero));
            var fired = false;

            _ = Task.Run(async () =>
                         {
                             await Task.Delay(TimeSpan.FromSeconds(1), clock);
                             fired = true;
                         });

            await Task.Delay(50); // let the background task start
            Assert.IsFalse(fired, "Should not have fired before Advance");

            clock.Advance(TimeSpan.FromSeconds(1));
            await Task.Delay(50); // let the continuation run
            Assert.IsTrue(fired, "Should have fired after Advance(1s)");
        }
    }
}