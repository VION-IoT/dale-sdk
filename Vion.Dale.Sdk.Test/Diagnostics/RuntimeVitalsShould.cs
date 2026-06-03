using System;
using System.Linq;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.Sdk.Test.Diagnostics
{
    [TestClass]
    public class RuntimeVitalsShould
    {
        [TestMethod]
        public void RecordAHandledMessageForTheReceivingActor()
        {
            var clock = new FakeTimeProvider();
            var now = clock.GetUtcNow();
            var vitals = new RuntimeVitals(clock);

            vitals.OnHandled("logicblock_Foo_1", new object(), TimeSpan.FromMilliseconds(5), exception: null);

            var snapshot = vitals.Snapshot();

            Assert.HasCount(1, snapshot);
            Assert.AreEqual("logicblock_Foo_1", snapshot[0].ActorName);
            Assert.AreEqual(1L, snapshot[0].MessagesHandled);
            Assert.AreEqual(0L, snapshot[0].Errors);
            Assert.AreEqual(now, snapshot[0].LastActivityUtc);
        }

        [TestMethod]
        public void TrackTheMaximumHandlerDuration()
        {
            var vitals = new RuntimeVitals(new FakeTimeProvider());

            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(5), exception: null);
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(12), exception: null);
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(7), exception: null);

            var actor = vitals.Snapshot().Single();

            Assert.AreEqual(3L, actor.MessagesHandled);
            Assert.AreEqual(TimeSpan.FromMilliseconds(12), actor.HandlerDurationMax);
        }

        [TestMethod]
        public void CountAHandledMessageThatThrewAsAnError()
        {
            var vitals = new RuntimeVitals(new FakeTimeProvider());

            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(3), new InvalidOperationException("boom"));

            var actor = vitals.Snapshot().Single();

            Assert.AreEqual(1L, actor.MessagesHandled);
            Assert.AreEqual(1L, actor.Errors);
        }

        [TestMethod]
        public void TrackEachActorSeparately()
        {
            var vitals = new RuntimeVitals(new FakeTimeProvider());

            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(1), exception: null);
            vitals.OnHandled("b", new object(), TimeSpan.FromMilliseconds(1), exception: null);
            vitals.OnHandled("b", new object(), TimeSpan.FromMilliseconds(1), exception: null);

            var snapshot = vitals.Snapshot();

            Assert.HasCount(2, snapshot);
            Assert.AreEqual(1L, snapshot.Single(v => v.ActorName == "a").MessagesHandled);
            Assert.AreEqual(2L, snapshot.Single(v => v.ActorName == "b").MessagesHandled);
        }

        [TestMethod]
        public void FeedTheCoreWhenInvokedThroughTheObserverInterface()
        {
            IActorMessageObserver observer = new RuntimeVitals(new FakeTimeProvider());

            observer.OnHandled("a", new object(), TimeSpan.FromMilliseconds(2), exception: null);

            Assert.AreEqual(1L, ((RuntimeVitals)observer).Snapshot().Single().MessagesHandled);
        }
    }
}
