using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     Deterministic next-event stepping over the exact quiescence barrier, on the real-wired DevHost and
    ///     Proto.Actor system. A registered <see cref="FakeTimeProvider" /> drives real <c>[Timer]</c> ticks:
    ///     advancing it completes the outstanding <c>Task.Delay(delay, clock)</c> immediately, re-entering the
    ///     actor on a real thread. Advancing by a budget steps to each next scheduled event and waits for the
    ///     system to quiesce before the next advance — every mailbox empty and no handler in flight — so a
    ///     given budget over a given block set lands on the same state run to run.
    ///     <para>
    ///         <see cref="FireTimerDelayOnAdvance" /> cites no criterion: it pins an implementation
    ///         <em>premise</em> the design rests on (a fake clock's pending delay completes when the clock
    ///         moves), not a consumer-observable requirement.
    ///     </para>
    /// </summary>
    [TestClass]
    public class DeterministicSteppingShould
    {
        // A hang guard, never a synchronisation point: every wait in this class is on a signal the SUT or the
        // fake clock raises, and this bound only decides how long a hung one takes to report.
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private static readonly DateTimeOffset Epoch = new(2026,
                                                           1,
                                                           1,
                                                           0,
                                                           0,
                                                           0,
                                                           TimeSpan.Zero);

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.1")]
        public async Task FireOneTickPerVirtualSecondOfBudget()
        {
            // Arrange — TickerBlock schedules its first tick at startup with a +1 s delay.
            var clock = new FakeTimeProvider(Epoch);
            await using var host = SteppedHost(clock);
            await host.StartAsync();

            // Act — next-event stepping walks t=1..5 s, quiescing between.
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.AreEqual(5, (int)host.Control.GetProperty("ticker", "Ticks")!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.1")]
        public async Task ReachOneTickCountAcrossFreshHosts()
        {
            // Arrange
            var counts = new int[15];

            // Act — a gap in the barrier shows here and nowhere else, so this is run rather than retried.
            for (var run = 0; run < counts.Length; run++)
            {
                await using var host = SteppedHost(new FakeTimeProvider(Epoch));
                await host.StartAsync();
                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(5));
                counts[run] = (int)host.Control.GetProperty("ticker", "Ticks")!;
            }

            // Assert
            CollectionAssert.AreEqual(new[] { 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5, 5 }, counts);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.1")]
        public async Task RefuseToStepRealClockNamingProviderAndRemedy()
        {
            // Arrange — no FakeTimeProvider registered, so TimeProvider.System stays in place.
            var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
            await host.StartAsync();

            // Act / Assert
            var refused = await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.Control.AdvanceAsync(TimeSpan.FromSeconds(1)));
            StringAssert.Contains(refused.Message, "SystemTimeProvider");
            StringAssert.Contains(refused.Message, "FakeTimeProvider");
            Assert.IsFalse(host.Control.IsStepped);
        }

        [TestMethod]
        public async Task FireTimerDelayOnAdvance()
        {
            // Arrange — the premise, isolated from the actor system: a fake clock's pending delay is what
            // re-enters an actor when the stepper moves the clock. The delay's own task is the signal, so
            // nothing here waits on the real clock (section 16); the timeout below is a hang guard, not
            // synchronisation.
            var clock = new FakeTimeProvider(Epoch);
            var pending = Task.Delay(TimeSpan.FromSeconds(1), clock);

            Assert.IsFalse(pending.IsCompleted, "the delay completed before the clock moved");

            // Act
            clock.Advance(TimeSpan.FromSeconds(1));

            // Assert
            await pending.WaitAsync(Timeout);
            Assert.IsTrue(pending.IsCompletedSuccessfully);
        }

        private static IDevHost SteppedHost(FakeTimeProvider clock)
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();

            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).ConfigureServices(s => s.AddSingleton<TimeProvider>(clock)).Build();
        }
    }
}