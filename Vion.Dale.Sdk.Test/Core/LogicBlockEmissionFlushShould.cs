using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     How many flushes a block keeps armed while it holds throttled values. The TestKit cannot show this:
    ///     its emitted values are the same however many flushes run, and it exposes no count of what is armed.
    ///     The recording context of <see cref="LifecycleHarness" /> captures every flush the block arms, and the
    ///     test delivers each one itself, on a clock the block cannot take for a controllable one, so the
    ///     emission policy stays on.
    /// </summary>
    [TestClass]
    public sealed class LogicBlockEmissionFlushShould
    {
        private static readonly DateTimeOffset Start = new(2026, 11, 24, 0, 0, 0, TimeSpan.Zero);

        private readonly LifecycleHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-010.1")]
        public void KeepOneFlushArmedWhenSupersededWakeupArrives()
        {
            // Arrange — a slow member's hold arms a flush for 2 s; a fast member's hold due sooner supersedes it.
            // The live flush releases the fast value and re-arms for the slow deadline, so two wakeups then fall
            // due together: the superseded one and the live one. The slowest member is still held after both.
            var clock = new SettableClock(Start);
            var block = new ThreeRateBlock();
            _harness.ConfigureAndStart(block, ["Service"], ClockedHost(clock));
            clock.Now = Start.AddSeconds(10);
            block.Service.Slow = 1;
            block.Service.Slow = 2;
            block.Service.Slowest = 1;
            block.Service.Slowest = 2;
            block.Service.Fast = 1;
            block.Service.Fast = 2;
            var superseded = _harness.Scheduled[^2].Message;
            var supersedingFlush = _harness.Scheduled[^1].Message;
            clock.Now = Start.AddSeconds(10.25);
            _harness.Send(block, supersedingFlush);
            var live = _harness.Scheduled[^1].Message;
            var armedBefore = _harness.Scheduled.Count;

            // Act
            clock.Now = Start.AddSeconds(12);
            _harness.Send(block, superseded);
            _harness.Send(block, live);

            // Assert
            Assert.AreEqual(1,
                            _harness.Scheduled.Count - armedBefore,
                            "The two wakeups that fell due together arm one flush for the value still held: a superseded wakeup that re-armed as well would start a second chain of flushes, and each further one another.");
        }

        private static IServiceProvider ClockedHost(TimeProvider clock)
        {
            return new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                          .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                          .AddSingleton(clock)
                                          .BuildServiceProvider();
        }

        /// <summary>A clock set by hand that offers no <c>Advance(TimeSpan)</c>, so the block keeps its emission policy on.</summary>
        private sealed class SettableClock : TimeProvider
        {
            public DateTimeOffset Now { get; set; }

            public SettableClock(DateTimeOffset now)
            {
                Now = now;
            }

            public override DateTimeOffset GetUtcNow()
            {
                return Now;
            }
        }

        private sealed class ThreeRateBlock : LogicBlockBase
        {
            public ThreeRateService Service { get; } = new();

            public ThreeRateBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>Three members held for different intervals, so their deadlines can supersede one another.</summary>
        private sealed class ThreeRateService
        {
            [ServiceProperty(MinInterval = "250ms")]
            public double Fast { get; set; }

            [ServiceProperty(MinInterval = "2s")]
            public double Slow { get; set; }

            [ServiceProperty(MinInterval = "5s")]
            public double Slowest { get; set; }
        }
    }
}
