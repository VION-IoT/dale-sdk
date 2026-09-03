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
    ///     Next-event stepping, as distinct from a single clock jump. The regression it guards: one
    ///     <c>FakeTimeProvider.Advance(5s)</c> fires a <c>[Timer(1)]</c> only ONCE, because its reschedule runs
    ///     after Advance returns. Stepping to each scheduled event and quiescing between fires every due timer
    ///     the right number of times with no drift, even though delays self-reschedule.
    ///     <para>
    ///         <see cref="TrackPendingRegistrationsWithoutLeaking" /> cites no criterion: it pins an
    ///         implementation <em>premise</em> (the engine-owned schedule's bookkeeping), not a
    ///         consumer-observable requirement. The live proof is the no-drift test, which would fail if the
    ///         schedule accumulated stale entries.
    ///     </para>
    /// </summary>
    [TestClass]
    public class NextEventSteppingShould
    {
        private static readonly DateTimeOffset Epoch = new(2026,
                                                           1,
                                                           1,
                                                           0,
                                                           0,
                                                           0,
                                                           TimeSpan.Zero);

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.1")]
        public async Task FireEveryDueTimerWithoutDrift()
        {
            // Arrange
            await using var host = SteppedHost<TickerBlock, TestDependencyInjection>("ticker", new FakeTimeProvider(Epoch));
            await host.StartAsync();

            // Act
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(5));
            var afterFive = (int)host.Control.GetProperty("ticker", "Ticks")!;
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1));

            // Assert — the sixth tick was scheduled at virtual t=6 s; one more second must fire it. A count of
            // 1 after the first advance would be the single-jump-fires-once bug.
            Assert.AreEqual(5, afterFive);
            Assert.AreEqual(6, (int)host.Control.GetProperty("ticker", "Ticks")!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.3")]
        public async Task FireEachRateAtItsOwnCadence()
        {
            // Arrange — one block, a 1 s and a 5 s timer, so the hops land on distinct due-times.
            await using var host = SteppedHost<DualRateBlock, DualRateDependencyInjection>("dual", new FakeTimeProvider(Epoch));
            await host.StartAsync();

            // Act
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(5));

            // Assert
            Assert.AreEqual(5, (int)host.Control.GetProperty("dual", "Fast")!);
            Assert.AreEqual(1, (int)host.Control.GetProperty("dual", "Slow")!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.2")]
        public async Task AdvanceTheRemainderWhenNoEventFallsAtTheBudgetEnd()
        {
            // Arrange — the only scheduled event is at t=1 s, so a 0.4 s budget contains none of it.
            var clock = new FakeTimeProvider(Epoch);
            await using var host = SteppedHost<TickerBlock, TestDependencyInjection>("ticker", clock);
            await host.StartAsync();

            // Act
            await host.Control.AdvanceAsync(TimeSpan.FromMilliseconds(400));

            // Assert — the caller asked for 0.4 virtual seconds and got exactly that, with nothing fired.
            Assert.AreEqual(Epoch.AddMilliseconds(400), host.Control.VirtualTimeUtc);
            Assert.AreEqual(0, (int)host.Control.GetProperty("ticker", "Ticks")!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.3")]
        public async Task AdvanceOneVirtualInstantPerEventHop()
        {
            // Arrange
            var clock = new FakeTimeProvider(Epoch);
            await using var host = SteppedHost<TickerBlock, TestDependencyInjection>("ticker", clock);
            await host.StartAsync();

            // Act — one hop moves the clock to the next scheduled instant and no further.
            await host.Control.AdvanceToNextEventAsync();
            var afterOneHop = host.Control.VirtualTimeUtc;
            await host.Control.AdvanceToNextEventAsync();

            // Assert
            Assert.AreEqual(Epoch.AddSeconds(1), afterOneHop);
            Assert.AreEqual(Epoch.AddSeconds(2), host.Control.VirtualTimeUtc);
            Assert.AreEqual(2, (int)host.Control.GetProperty("ticker", "Ticks")!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.4")]
        public async Task RefuseNegativeAdvanceBudget()
        {
            // Arrange
            await using var host = SteppedHost<TickerBlock, TestDependencyInjection>("ticker", new FakeTimeProvider(Epoch));
            await host.StartAsync();

            // Act / Assert
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => host.Control.AdvanceAsync(TimeSpan.FromSeconds(-1)));
        }

        [TestMethod]
        public void TrackPendingRegistrationsWithoutLeaking()
        {
            // Arrange
            var schedule = new VirtualSchedule();
            var early = new object();
            var late = new object();

            // Act / Assert — the empty, populated and drained states in the order the stepper meets them.
            Assert.IsNull(schedule.NextDue());
            Assert.AreEqual(0, schedule.PendingCount);

            schedule.Register(late, Epoch.AddSeconds(5));
            schedule.Register(early, Epoch.AddSeconds(1));
            Assert.AreEqual(2, schedule.PendingCount);
            Assert.AreEqual(Epoch.AddSeconds(1), schedule.NextDue());

            schedule.Unregister(early);
            Assert.AreEqual(1, schedule.PendingCount);
            Assert.AreEqual(Epoch.AddSeconds(5), schedule.NextDue());

            schedule.Unregister(late);
            Assert.AreEqual(0, schedule.PendingCount);
            Assert.IsNull(schedule.NextDue());

            // An unknown or already-removed token is a harmless no-op.
            schedule.Unregister(new object());
            schedule.Unregister(early);
            Assert.AreEqual(0, schedule.PendingCount);
        }

        private static IDevHost SteppedHost<TBlock, TDi>(string name, FakeTimeProvider clock)
            where TBlock : LogicBlockBase
            where TDi : IConfigureServices
        {
            var config = DevConfigurationBuilder.Create().AddLogicBlock<TBlock>(name).Build();

            return DevHostBuilder.Create()
                                 .WithDi<TDi>()
                                 .WithConfiguration(config)
                                 .ConfigureServices(s => s.AddSingleton<TimeProvider>(clock))
                                 .Build();
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
