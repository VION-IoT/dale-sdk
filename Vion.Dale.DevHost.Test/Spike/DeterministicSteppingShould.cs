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
    ///     SPIKE Task 1 — proves that a registered <see cref="FakeTimeProvider" /> drives a real
    ///     [Timer] tick on the real-wired DevHost / Proto.Actor system.
    ///
    ///     The settle window is 300 ms — intentionally shorter than the 1-second wall-clock delay.
    ///     Without the fix, the timer fires via <c>Task.Delay(delay)</c> on the real clock, which
    ///     takes 1 full second, so the poll times out and Ticks stays 0.
    ///     With the fix, <c>Task.Delay(delay, _timeProvider)</c> completes the moment
    ///     <see cref="FakeTimeProvider.Advance" /> is called, so the tick lands well within 300 ms.
    /// </summary>
    [TestClass]
    public class DeterministicSteppingShould
    {
        [TestMethod]
        public async Task Advance_DrivesOneTimerTick()
        {
            // Arrange — a frozen clock starting at 2026-01-01 00:00:00 UTC
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

            // Act — advance the fake clock by one timer interval (1 s).
            // With the fix this completes the outstanding Task.Delay immediately; without the fix,
            // the real-clock delay won't fire within the 300 ms settle window below.
            clock.Advance(TimeSpan.FromSeconds(1));

            // Settle the async re-entrant continuation with a *bounded* poll.
            // The window (300 ms) is intentionally shorter than the 1-second wall-clock timer so
            // the test fails before the fix and passes only when the fake clock drives the delay.
            // TODO(spike): replace bounded poll with quiescence barrier
            var deadline = DateTime.UtcNow + TimeSpan.FromMilliseconds(300);
            while (DateTime.UtcNow < deadline)
            {
                var v = host.Control.GetProperty("ticker", "Ticks");
                if (v is int n && n >= 1)
                {
                    break;
                }

                await Task.Delay(25);
            }

            // Assert
            Assert.AreEqual(1, (int)host.Control.GetProperty("ticker", "Ticks")!,
                            "Advancing the fake clock by one interval must fire exactly one timer tick " +
                            "within 300 ms — well under the 1-second real-clock delay.");
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

        // Extended-settle diagnostic: with 3s poll window the real-clock timer fires. This lets us
        // distinguish "timer never fires at all" from "timer fires too late".
        // After the fix this should pass with Ticks == 1; we don't include it in the spike gate
        // because it only proves real-clock firing, not deterministic stepping.
        [TestMethod]
        [Ignore("Diagnostic only — not part of the spike gate")]
        public async Task Diagnostic_TimerFires_WithRealClock_In3sWindow()
        {
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();
            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(config)
                                                 .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                 .Build();
            await host.StartAsync();

            clock.Advance(TimeSpan.FromSeconds(1));

            var deadline = DateTime.UtcNow + TimeSpan.FromSeconds(3);
            while (DateTime.UtcNow < deadline)
            {
                var v = host.Control.GetProperty("ticker", "Ticks");
                if (v is int n && n >= 1) break;
                await Task.Delay(50);
            }

            var ticks = (int)host.Control.GetProperty("ticker", "Ticks")!;
            Console.WriteLine($"Ticks after 3s settle: {ticks}");
            Assert.IsGreaterThanOrEqualTo(ticks, 1, $"Expected at least 1 tick, got {ticks}");
        }
    }
}
