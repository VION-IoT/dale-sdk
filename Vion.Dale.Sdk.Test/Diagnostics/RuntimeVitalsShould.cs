using System;
using System.Linq;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.Sdk.Test.Diagnostics
{
    /// <summary>
    ///     The per-actor vitals aggregate: what each of its four feeds records, what the snapshot reports,
    ///     and which of the eleven vitals go stale on purpose. All timing runs on an injected clock, so every
    ///     window here is stepped rather than waited out.
    /// </summary>
    [TestClass]
    public sealed class RuntimeVitalsShould
    {
        private static readonly TimeSpan Window = TimeSpan.FromSeconds(10);

        private readonly FakeTimeProvider _clock = new();

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.1")]
        public void ReportRegisteredActorBeforeItHandlesAnything()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            var identity = new ActorIdentity(ActorCategory.LogicBlock, "Heater", "Vion.Examples.Energy");

            // Act
            vitals.Register("logicblock_Heater_1", identity);

            // Assert
            var actor = vitals.Snapshot().Single();
            Assert.AreEqual("logicblock_Heater_1", actor.ActorName, "A silent block is exactly the one an operator is looking for, so it is in the snapshot from its spawn.");
            Assert.AreEqual(identity, actor.Identity, "The identity is recorded at the spawn, which is the only place that knows it.");
            Assert.AreEqual(0L, actor.MessagesHandled);
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.2")]
        public void CountHandledMessageAndAccumulateItsDuration()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);

            // Act
            vitals.OnHandled("logicblock_Foo_1", new object(), TimeSpan.FromMilliseconds(5), null);
            vitals.OnHandled("logicblock_Foo_1", new object(), TimeSpan.FromMilliseconds(12), null);

            // Assert
            var actor = vitals.Snapshot().Single();
            Assert.AreEqual(2L, actor.MessagesHandled, "The count and the total are what the backend derives a rate and a mean from.");
            Assert.AreEqual(TimeSpan.FromMilliseconds(17), actor.HandlerDurationTotal);
            Assert.AreEqual(0L, actor.Errors);
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.2")]
        public void CountHandlerThatThrewAsBothHandledAndError()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);

            // Act
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(3), new InvalidOperationException());

            // Assert
            var actor = vitals.Snapshot().Single();
            Assert.AreEqual(1L, actor.MessagesHandled);
            Assert.AreEqual(1L, actor.Errors, "The pipeline swallows the exception, so this count is the only trace it leaves.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.2")]
        public void TrackEachActorSeparately()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);

            // Act
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(1), null);
            vitals.OnHandled("b", new object(), TimeSpan.FromMilliseconds(1), null);
            vitals.OnHandled("b", new object(), TimeSpan.FromMilliseconds(1), null);

            // Assert
            var snapshot = vitals.Snapshot();
            Assert.AreEqual(1L, snapshot.Single(actor => actor.ActorName == "a").MessagesHandled, "The per-actor tag set is the whole point of the aggregate.");
            Assert.AreEqual(2L, snapshot.Single(actor => actor.ActorName == "b").MessagesHandled);
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.2")]
        public void RecordThroughObserverInterfaceAsThroughCore()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            IActorMessageObserver observer = vitals;

            // Act
            observer.OnHandled("a", new object(), TimeSpan.FromMilliseconds(2), null);

            // Assert
            Assert.AreEqual(1L, vitals.Snapshot().Single().MessagesHandled, "The pipeline feeds the core through this face, so the two must be one object.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.3")]
        public void ReportMailboxDepthAsPostedLessReceived()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);

            // Act
            vitals.OnMessagePosted("a");
            vitals.OnMessagePosted("a");
            vitals.OnMessagePosted("a");
            vitals.OnMessageReceived("a");

            // Assert
            Assert.AreEqual(2, vitals.Snapshot().Single().MailboxDepth, "The mailbox exposes no length, so the depth is derived from the two counts around it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.3")]
        public void ReportMailboxDepthOfNothingWhenMoreWereReceivedThanPosted()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);

            // Act
            vitals.OnMessageReceived("a");

            // Assert
            Assert.AreEqual(0, vitals.Snapshot().Single().MailboxDepth, "The two counters are fed from different threads, so their difference can read backwards for an instant.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.4")]
        public void ReportGreatestHandlerDurationOfCurrentWindowOnly()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock, Window);

            // Act
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(50), null);
            _clock.Advance(Window + TimeSpan.FromSeconds(1));
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(4), null);

            // Assert
            Assert.AreEqual(TimeSpan.FromMilliseconds(4),
                            vitals.Snapshot().Single().HandlerDurationMax,
                            "A lifetime high-water mark on a gateway that runs for months describes a minute nobody remembers.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.4")]
        public void ReportGreatestMailboxDepthOfCurrentWindowOnly()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock, Window);
            vitals.OnMessagePosted("a");
            vitals.OnMessagePosted("a");
            vitals.OnMessagePosted("a");
            vitals.OnMessageReceived("a");

            // Act
            _clock.Advance(Window + TimeSpan.FromSeconds(1));
            vitals.OnMessageReceived("a");

            // Assert
            Assert.AreEqual(2, vitals.Snapshot().Single().MailboxDepthMax, "The peak is over the recent window too, and the window is tracked per value rather than per actor.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.4")]
        public void ReportNothingForGreatestValuesOnceWindowPassedWithNoSample()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock, Window);
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(50), null);
            vitals.OnTimerCallback("a", TimeSpan.FromMilliseconds(30), TimeSpan.FromMilliseconds(4));

            // Act
            _clock.Advance(Window + TimeSpan.FromSeconds(1));

            // Assert
            var actor = vitals.Snapshot().Single();
            Assert.AreEqual(TimeSpan.Zero, actor.HandlerDurationMax, "An idle actor reads nothing rather than its last busy minute.");
            Assert.AreEqual(TimeSpan.Zero, actor.TimerCallbackDurationMax);
            Assert.AreEqual(TimeSpan.Zero, actor.TimerJitterMax);
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.5")]
        [DataRow(0d, DisplayName = "a window of nothing")]
        [DataRow(-1d, DisplayName = "a window of less than nothing")]
        public void RefuseWindowOfNothingOrLess(double seconds)
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new RuntimeVitals(_clock, TimeSpan.FromSeconds(seconds)),
                                                              "Such a window has already elapsed at every read, so four of the eleven vitals would report nothing while the counts beside them kept rising.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.6")]
        public void ReportTimerJitterAsSizeOfDifferenceWhicheverWayItFell()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock, Window);

            // Act
            vitals.OnTimerCallback("a", TimeSpan.FromMilliseconds(10), TimeSpan.FromMilliseconds(3));
            vitals.OnTimerCallback("a", TimeSpan.FromMilliseconds(4), TimeSpan.FromMilliseconds(-9));
            vitals.OnTimerCallback("a", TimeSpan.FromMilliseconds(25), TimeSpan.FromMilliseconds(2));

            // Assert
            var actor = vitals.Snapshot().Single();
            Assert.AreEqual(TimeSpan.FromMilliseconds(25), actor.TimerCallbackDurationMax);
            Assert.AreEqual(TimeSpan.FromMilliseconds(9), actor.TimerJitterMax, "A watchdog cares how far a tick missed its slot, not which side of it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.7")]
        public void ReportInstantOfLastHandledMessage()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock, Window);
            vitals.OnHandled("a", new object(), TimeSpan.Zero, null);
            _clock.Advance(TimeSpan.FromSeconds(3));

            // Act
            vitals.OnHandled("a", new object(), TimeSpan.Zero, null);

            // Assert
            Assert.AreEqual(_clock.GetUtcNow(), vitals.Snapshot().Single().LastActivityUtc, "The diagnostics block reads this to tell a quiet actor from a wedged one.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.8")]
        public void KeepCountsAndTotalAcrossWindows()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock, Window);

            // Act
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(5), null);
            _clock.Advance(Window + TimeSpan.FromSeconds(1));
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(12), null);

            // Assert
            var actor = vitals.Snapshot().Single();
            Assert.AreEqual(TimeSpan.FromMilliseconds(17), actor.HandlerDurationTotal, "The cumulative pair is what a rate is derived from, so it never resets.");
            Assert.AreEqual(2L, actor.MessagesHandled);
        }
    }
}