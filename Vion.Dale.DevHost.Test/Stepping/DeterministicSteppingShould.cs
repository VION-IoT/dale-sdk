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
    ///     Regression tests for deterministic next-event stepping via the exact quiescence barrier.
    ///     A registered <see cref="FakeTimeProvider" /> drives real <c>[Timer]</c> ticks on the
    ///     real-wired DevHost / Proto.Actor system: advancing the fake clock completes the outstanding
    ///     <c>Task.Delay(delay, clock)</c> immediately, re-entering the actor on a real thread.
    ///     <c>AdvanceAsync(virtualTime)</c> advances to each next scheduled event and waits for the
    ///     actor system to quiesce before the next advance. Quiescence is determined by the EXACT
    ///     predicate <c>Σ MailboxDepth == 0 AND InFlight == 0</c>: every mailbox empty AND no user
    ///     handler currently executing. So <c>AdvanceAsync(Ns)</c> over a <c>[Timer(1)]</c> fires it
    ///     exactly N times, the same result run-to-run.
    /// </summary>
    [TestClass]
    public class DeterministicSteppingShould
    {
        /// <summary>
        ///     15 runs on a single-block ticker: <c>AdvanceAsync(5s)</c> over a <c>[Timer(1)]</c> must
        ///     land on exactly 5 ticks every time. The first tick is scheduled at startup with a +1s
        ///     fake delay; next-event stepping advances to each subsequent tick at t=1..5s. Any
        ///     deviation means the barrier has a gap — not something to paper over with retries.
        /// </summary>
        [TestMethod]
        public async Task StepDeterministically_AcrossManyRuns()
        {
            for (var run = 0; run < 15; run++)
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

                // Advance 5 virtual seconds: next-event stepping fires the [Timer(1)] at t=1..5s.
                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(5));

                Assert.AreEqual(5, (int)host.Control.GetProperty("ticker", "Ticks")!, $"run {run}: advancing 5 virtual seconds must yield exactly 5 ticks every time.");
            }
        }

        /// <summary>
        ///     Single-tick proof: <c>AdvanceAsync(1s)</c> advances to the first scheduled event (the
        ///     +1s tick) and waits for the actor system to settle. Exactly one tick must land,
        ///     deterministically.
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

            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1));

            Assert.AreEqual(1,
                            (int)host.Control.GetProperty("ticker", "Ticks")!,
                            "Advancing one virtual second and waiting for quiescence must fire exactly one timer tick.");
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

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(async () => await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1)));
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