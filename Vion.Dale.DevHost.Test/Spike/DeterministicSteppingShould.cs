using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test.Spike
{
    /// <summary>
    ///     SPIKE Task 3 — deterministic multi-cycle stepping on top of Task 1's fake-clock-driven timers.
    ///
    ///     Task 1 proved a registered <see cref="FakeTimeProvider" /> drives a real [Timer] tick on the
    ///     real-wired DevHost / Proto.Actor system: advancing the fake clock completes the outstanding
    ///     <c>Task.Delay(delay, clock)</c> immediately, re-entering the actor on a real thread.
    ///
    ///     Task 3 builds on that: after each <see cref="FakeTimeProvider.Advance" /> the due timer
    ///     continuation cascades on real threads, so to step deterministically the stepper waits for the
    ///     actor system to QUIESCE (every mailbox drained, no handler in flight) before the next advance.
    ///     Quiescence is read from <c>RuntimeVitals</c> via the in-flight-aware
    ///     <c>Σ(MessagesPosted − MessagesHandled) == 0</c> signal. The proof is
    ///     <see cref="StepDeterministically_AcrossManyRuns" />: 50 runs of 5 cycles, every one landing on
    ///     exactly 5 ticks, no flake.
    /// </summary>
    [TestClass]
    public class DeterministicSteppingShould
    {
        // The spike's actual proof: N deterministic cycles produce the EXACT same result across many runs.
        // 5 cycles → 5 ticks (the first tick is scheduled at startup with a +1s fake delay, so cycle 1's
        // Advance(1s) yields Ticks==1; five advances yield five). If any run gives Ticks != 5 the quiescence
        // heuristic has a gap — that is the spike failing honestly, NOT something to paper over with a sleep.
        [TestMethod]
        public async Task StepDeterministically_AcrossManyRuns()
        {
            for (var run = 0; run < 50; run++)
            {
                var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

                var config = DevConfigurationBuilder.Create()
                                                    .AddLogicBlock<TickerBlock>("ticker")
                                                    .Build();

                await using var host = DevHostBuilder.Create()
                                                     .WithDi<TestDependencyInjection>()
                                                     .WithConfiguration(config)
                                                     .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                     .Build();

                await host.StartAsync();

                // 5 deterministic cycles: each advances the fake clock 1 s and waits for quiescence.
                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1), 5);

                Assert.AreEqual(5, (int)host.Control.GetProperty("ticker", "Ticks")!,
                                $"run {run}: 5 deterministic cycles must yield exactly 5 ticks every time.");
            }
        }

        /// <summary>
        ///     Task 1's single-tick proof, now routed through the quiescence barrier instead of the temporary
        ///     bounded poll: <c>AdvanceAsync(1s, 1)</c> advances the fake clock one interval and waits for the
        ///     actor system to settle. Without Task 1's fix the timer would fire via the real clock (1 full
        ///     second) and the 10 s quiescence budget would still cover it — so the load-bearing assertion is
        ///     simply that exactly one tick lands, deterministically.
        /// </summary>
        [TestMethod]
        public async Task Advance_DrivesOneTimerTick()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));

            var config = DevConfigurationBuilder.Create()
                                                .AddLogicBlock<TickerBlock>("ticker")
                                                .Build();

            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(config)
                                                 .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                 .Build();

            await host.StartAsync();

            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1), 1);

            Assert.AreEqual(1, (int)host.Control.GetProperty("ticker", "Ticks")!,
                            "Advancing the fake clock by one interval and waiting for quiescence must fire exactly one timer tick.");
        }

        /// <summary>
        ///     Stepping on a real (non-fake) clock is meaningless and must fail loudly rather than hang or
        ///     silently no-op. Building the host WITHOUT registering a FakeTimeProvider leaves the real
        ///     <see cref="TimeProvider.System" /> in place; the first stepping call must throw.
        /// </summary>
        [TestMethod]
        public async Task AdvanceAsync_OnRealClock_Throws()
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            await Assert.ThrowsExactlyAsync<InvalidOperationException>(
                async () => await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1), 1));
        }

        // Diagnostic test: verify Task.Delay with FakeTimeProvider fires and the settle window
        // proves the mechanism works WITHOUT the actor system (isolates the TimeProvider behaviour).
        [TestMethod]
        public async Task FakeTimeProvider_TaskDelay_FiresOnAdvance()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
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
