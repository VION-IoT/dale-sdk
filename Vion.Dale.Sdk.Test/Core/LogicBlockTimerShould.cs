using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Diagnostics;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     What a bound <c>[Timer]</c> does once the block is running: when its first tick is armed, what a
    ///     tick that arrives outside the started window does, and what a registered vitals collector is told
    ///     about each callback. Which declarations bind at all is
    ///     <c>Configuration.Timers.DeclarativeTimerBinderShould</c>'s.
    /// </summary>
    [TestClass]
    public sealed class LogicBlockTimerShould
    {
        private readonly LifecycleHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.4")]
        public void ArmFirstTickAtConfigurationRatherThanAtStart()
        {
            // Arrange
            var block = new TickingBlock();

            // Act
            _harness.Configure(block);

            // Assert
            var armed = _harness.Context.Scheduled.Where(entry => entry.Message.GetType().Name == "TimerTickMessage").ToList();
            Assert.HasCount(1, armed, "A timer is armed when it is registered, which is inside the configuration.");
            Assert.AreEqual(TickingBlock.Interval, armed.Single().Delay, "The first tick falls one interval after the configuration, not after the start.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.5")]
        public void ArmNextTickAndInvokeNothingBeforeStart()
        {
            // Arrange
            var block = new TickingBlock();
            _harness.Configure(block);
            var tick = FirstTick();

            // Act
            _harness.Send(block, tick);

            // Assert
            Assert.AreEqual(0, block.Ticks, "A tick armed at the configuration reaches a block that is not started yet, and must invoke nothing.");
            Assert.HasCount(2, TicksArmed(), "The chain is kept, so the timer still has its cadence when the block starts.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.5")]
        public void ArmNextTickAndInvokeNothingAfterStop()
        {
            // Arrange
            var block = new TickingBlock();
            _harness.ConfigureAndStart(block);
            var tick = FirstTick();
            _harness.Send(block, tick);
            _harness.Send(block, new StopLogicBlockRequest());
            var armedBeforeTick = TicksArmed().Count;
            var ticksBeforeStop = block.Ticks;

            // Act
            _harness.Send(block, tick);

            // Assert
            Assert.AreEqual(ticksBeforeStop, block.Ticks, "A stopped block's timer invokes nothing.");
            Assert.HasCount(armedBeforeTick + 1, TicksArmed(), "It keeps its cadence, so a restarted block resumes it — timers are armed at the configuration and never again.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.5")]
        public void ResumeTimerAfterRestart()
        {
            // Arrange
            var block = new TickingBlock();
            _harness.ConfigureAndStart(block);
            var tick = FirstTick();
            _harness.Send(block, new StopLogicBlockRequest());
            _harness.Send(block, tick);

            // Act
            _harness.Send(block, new StartLogicBlockRequest());
            _harness.Send(block, tick);

            // Assert
            Assert.AreEqual(1, block.Ticks, "The tick that arrived while the block was stopped invoked nothing; the one after the restart did.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.6")]
        public void ArmNextTickBeforeInvokingCallbackThatThrows()
        {
            // Arrange
            var block = new ThrowingTickBlock();
            _harness.ConfigureAndStart(block);
            var tick = FirstTick();
            var armedBeforeTick = TicksArmed().Count;

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _harness.Send(block, tick));
            Assert.HasCount(armedBeforeTick + 1,
                            TicksArmed(),
                            "The next tick is armed before the callback runs, so a callback that throws once does not silence the block for good.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.7")]
        public void ReportCallbackDurationAndJitterWithVitalsCollector()
        {
            // Arrange
            var clock = new FakeTimeProvider();
            var collector = new RecordingVitalsCollector();
            var block = new SlowTickBlock(clock, TimeSpan.FromMilliseconds(40));
            _harness.ConfigureAndStart(block, serviceProvider: HostWith(clock, collector));
            var tick = FirstTick();

            // Act
            _harness.Send(block, tick);

            // Assert
            var reported = collector.Callbacks.Single();
            Assert.AreEqual(TimeSpan.FromMilliseconds(40), reported.Duration, "The collector is told how long the callback ran.");
            Assert.AreEqual(TimeSpan.Zero, reported.Jitter, "A timer's first tick has no predecessor to measure against.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.7")]
        public void ReportCallbackThatThrewAsWellAsOneThatReturned()
        {
            // Arrange
            var clock = new FakeTimeProvider();
            var collector = new RecordingVitalsCollector();
            var block = new ThrowingTickBlock();
            _harness.ConfigureAndStart(block, serviceProvider: HostWith(clock, collector));
            var tick = FirstTick();

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _harness.Send(block, tick));
            Assert.HasCount(1, collector.Callbacks, "A callback that threw is exactly the one an operator needs the duration of.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-007.7")]
        public void RunCallbackWithoutVitalsCollector()
        {
            // Arrange
            var block = new TickingBlock();
            _harness.ConfigureAndStart(block);
            var tick = FirstTick();

            // Act
            _harness.Send(block, tick);

            // Assert
            Assert.AreEqual(1, block.Ticks, "The callback still runs on a host that registers no collector — the measurement is what is skipped, not the tick.");
        }

        private static IServiceProvider HostWith(TimeProvider clock, IActorVitalsCollector collector)
        {
            return new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                          .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                          .AddSingleton(clock)
                                          .AddSingleton(collector)
                                          .BuildServiceProvider();
        }

        private object FirstTick()
        {
            return _harness.Context.Scheduled.First(entry => entry.Message.GetType().Name == "TimerTickMessage").Message;
        }

        private List<object> TicksArmed()
        {
            return _harness.Context.Scheduled.Where(entry => entry.Message.GetType().Name == "TimerTickMessage").Select(entry => entry.Message).ToList();
        }

        /// <summary>Captures what the timer watchdog reported, so the test reads it rather than a log.</summary>
        private sealed class RecordingVitalsCollector : IActorVitalsCollector
        {
            public List<(string ActorName, TimeSpan Duration, TimeSpan Jitter)> Callbacks { get; } = [];

            public void Register(string actorName, ActorIdentity identity)
            {
            }

            public void OnMessagePosted(string actorName)
            {
            }

            public void OnMessageReceived(string actorName)
            {
            }

            public void OnTimerCallback(string actorName, TimeSpan callbackDuration, TimeSpan jitter)
            {
                Callbacks.Add((actorName, callbackDuration, jitter));
            }
        }

        private sealed class TickingBlock : LogicBlockBase
        {
            public static readonly TimeSpan Interval = TimeSpan.FromSeconds(6);

            public int Ticks { get; private set; }

            public TickingBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            [Timer(6.0)]
            private void Tick()
            {
                Ticks++;
            }
        }

        private sealed class ThrowingTickBlock : LogicBlockBase
        {
            public ThrowingTickBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            [Timer(6.0)]
            private void Tick()
            {
                throw new InvalidOperationException("the timer callback refused");
            }
        }

        /// <summary>A callback that advances the injected clock, so its measured duration is exact.</summary>
        private sealed class SlowTickBlock : LogicBlockBase
        {
            private readonly FakeTimeProvider _clock;

            private readonly TimeSpan _spent;

            public SlowTickBlock(FakeTimeProvider clock, TimeSpan spent) : base(NullLogger.Instance)
            {
                _clock = clock;
                _spent = spent;
            }

            protected override void Ready()
            {
            }

            [Timer(6.0)]
            private void Tick()
            {
                _clock.Advance(_spent);
            }
        }
    }
}