using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Control;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The decisive acceptance tests for NEXT-EVENT virtual-time stepping (Phase 1b Task 1). The
    ///     regression they guard: a single <c>FakeTimeProvider.Advance(5s)</c> fires a <c>[Timer(1)]</c> only
    ///     ONCE — its reschedule runs async, after Advance returns. Next-event stepping advances the fake clock
    ///     to EACH scheduled event and quiesces between, so a <c>[Timer(1)]</c> fires the right number of times
    ///     with no drift even though delays self-reschedule.
    /// </summary>
    [TestClass]
    public class NextEventSteppingShould
    {
        private static FakeTimeProvider NewClock()
        {
            return new FakeTimeProvider(new DateTimeOffset(2026,
                                                           1,
                                                           1,
                                                           0,
                                                           0,
                                                           0,
                                                           TimeSpan.Zero));
        }

        /// <summary>
        ///     The headline regression guard: <c>AdvanceAsync(5s)</c> over a <c>[Timer(1)]</c> fires it
        ///     EXACTLY 5 times — never once (the single-jump-fires-once bug). Run across 20 iterations:
        ///     identical every time, proving determinism.
        /// </summary>
        [TestMethod]
        public async Task FireTimerFiveTimes_Under5sAdvance_DeterministicallyAcrossIterations()
        {
            for (var run = 0; run < 20; run++)
            {
                var clock = NewClock();
                var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();

                await using var host = DevHostBuilder.Create()
                                                     .WithDi<TestDependencyInjection>()
                                                     .WithConfiguration(config)
                                                     .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                     .Build();

                await host.StartAsync();

                await host.Control.AdvanceAsync(TimeSpan.FromSeconds(5));

                Assert.AreEqual(5,
                                (int)host.Control.GetProperty("ticker", "Ticks")!,
                                $"run {run}: a [Timer(1)] must fire exactly 5 times under AdvanceAsync(5s). " +
                                "A value of 1 is the single-jump-fires-once bug; any other value is drift.");
            }
        }

        /// <summary>
        ///     No drift: after <c>AdvanceAsync(5s)</c> (5 ticks), advancing one more second yields the 6th
        ///     tick — proving the 6th tick was scheduled at virtual t=6s, not drifted off-cadence.
        /// </summary>
        [TestMethod]
        public async Task NotDrift_SixthTickFiresOnExtraSecond()
        {
            var clock = NewClock();
            var config = DevConfigurationBuilder.Create().AddLogicBlock<TickerBlock>("ticker").Build();

            await using var host = DevHostBuilder.Create()
                                                 .WithDi<TestDependencyInjection>()
                                                 .WithConfiguration(config)
                                                 .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                 .Build();

            await host.StartAsync();

            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(5));
            Assert.AreEqual(5, (int)host.Control.GetProperty("ticker", "Ticks")!, "Five seconds → five ticks.");

            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1));
            Assert.AreEqual(6,
                            (int)host.Control.GetProperty("ticker", "Ticks")!,
                            "The 6th tick was scheduled at virtual t=6s; one more second must fire it with no drift.");
        }

        /// <summary>
        ///     Mixed rates: a block with both a 1s and a 5s timer. <c>AdvanceAsync(5s)</c> must fire the
        ///     1s timer 5× and the 5s timer 1× — next-event stepping advances to each distinct due-time
        ///     (t=1,2,3,4,5 for the fast timer; t=5 for the slow one) and fires the right one each hop.
        /// </summary>
        [TestMethod]
        public async Task FireMixedRates_FastFiveTimes_SlowOnce()
        {
            var clock = NewClock();
            var config = DevConfigurationBuilder.Create().AddLogicBlock<DualRateBlock>("dual").Build();

            await using var host = DevHostBuilder.Create()
                                                 .WithDi<DualRateDependencyInjection>()
                                                 .WithConfiguration(config)
                                                 .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                                 .Build();

            await host.StartAsync();

            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(5));

            Assert.AreEqual(5, (int)host.Control.GetProperty("dual", "Fast")!, "The 1s timer must fire 5× over 5 virtual seconds.");
            Assert.AreEqual(1, (int)host.Control.GetProperty("dual", "Slow")!, "The 5s timer must fire exactly 1× over 5 virtual seconds.");
        }

        /// <summary>
        ///     Schedule hygiene: register/unregister must not leak tokens. A focused unit test on the real
        ///     <see cref="VirtualSchedule" /> (exposed via InternalsVisibleTo) — the live-system proof is the
        ///     no-drift test above, which would fail if the schedule accumulated stale entries.
        /// </summary>
        [TestMethod]
        public void VirtualSchedule_RegistersAndUnregisters_WithoutLeaking()
        {
            var schedule = new VirtualSchedule();
            var t0 = new DateTimeOffset(2026,
                                        1,
                                        1,
                                        0,
                                        0,
                                        0,
                                        TimeSpan.Zero);

            Assert.IsNull(schedule.NextDue(), "Empty schedule has no next-due.");
            Assert.AreEqual(0, schedule.PendingCount);

            var early = new object();
            var late = new object();
            schedule.Register(late, t0.AddSeconds(5));
            schedule.Register(early, t0.AddSeconds(1));

            Assert.AreEqual(2, schedule.PendingCount, "Both registrations are pending.");
            Assert.AreEqual(t0.AddSeconds(1), schedule.NextDue(), "NextDue is the minimum pending due-time.");

            // Unregistering the earliest exposes the later one — and never leaves the earlier token behind.
            schedule.Unregister(early);
            Assert.AreEqual(1, schedule.PendingCount);
            Assert.AreEqual(t0.AddSeconds(5), schedule.NextDue(), "After unregistering the earliest, NextDue moves to the later one.");

            schedule.Unregister(late);
            Assert.AreEqual(0, schedule.PendingCount, "Unregistering every token drains the schedule — no leak.");
            Assert.IsNull(schedule.NextDue());

            // Unregistering an unknown / already-removed token is a harmless no-op.
            schedule.Unregister(new object());
            schedule.Unregister(early);
            Assert.AreEqual(0, schedule.PendingCount);
        }
    }

    /// <summary>
    ///     A block with two timers at different rates — the mixed-rate fixture. <c>Fast</c> increments on a
    ///     <c>[Timer(1)]</c>, <c>Slow</c> on a <c>[Timer(5)]</c>. Both are read-only service properties the
    ///     next-event test asserts.
    /// </summary>
    [LogicBlock(Name = "DualRate")]
    public class DualRateBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Fast")]
        public int Fast { get; private set; }

        [ServiceProperty(Title = "Slow")]
        public int Slow { get; private set; }

        public DualRateBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnFast()
        {
            Fast++;
        }

        [Timer(5)]
        public void OnSlow()
        {
            Slow++;
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>DI registration for the mixed-rate fixture, discovered by <c>WithDi&lt;DualRateDependencyInjection&gt;()</c>.</summary>
    public class DualRateDependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<DualRateBlock>();
        }
    }
}
