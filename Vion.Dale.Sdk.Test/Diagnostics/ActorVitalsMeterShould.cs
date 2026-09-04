using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.Sdk.Test.Diagnostics
{
    /// <summary>
    ///     The metrics face of the vitals core. Every instrument name, unit and tag key asserted here is a
    ///     wire: the runtime's export options name the meter, and a dashboard template names each
    ///     instrument, so a rename here goes dark on a fleet rather than failing a build.
    ///     <para>
    ///         The instruments are observable, so a test collects them by driving a listener's own
    ///         observation rather than by waiting for an export tick.
    ///     </para>
    /// </summary>
    [TestClass]
    public sealed class ActorVitalsMeterShould
    {
        private readonly FakeTimeProvider _clock = new();

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-019.1")]
        public void PublishEveryVitalAsInstrumentOfOneMeter()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            vitals.Register("logicblock_Heater_1", new ActorIdentity(ActorCategory.LogicBlock, "Heater", "Lib"));

            // Act
            using var meter = new ActorVitalsMeter(vitals);

            // Assert — read off the artifact rather than from a written list, so a new instrument is not missed.
            var published = PublishedInstruments();
            Assert.HasCount(8, published, "Eight vitals are exported; the ones that are not are the identity dimensions the tags carry instead.");
            CollectionAssert.AllItemsAreUnique(published);
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-019.1")]
        public void ReadCoreAtEachObservationRatherThanBeingPushedTo()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            vitals.Register("a", new ActorIdentity(ActorCategory.Runtime, "MqttClient", null));
            using var meter = new ActorVitalsMeter(vitals);
            var beforeAnyMessage = Collect<long>("vion.actor.messages_handled").Single().Value;

            // Act
            vitals.OnHandled("a", new object(), TimeSpan.FromMilliseconds(2), null);

            // Assert
            Assert.AreEqual(0L, beforeAnyMessage, "The first observation read a core that had handled nothing.");
            Assert.AreEqual(1L, Collect<long>("vion.actor.messages_handled").Single().Value, "The second read the same core again — the meter holds no state of its own.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-019.2")]
        public void PublishCountsCumulativelyAndDurationsInSeconds()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            vitals.Register("logicblock_Heater_1", new ActorIdentity(ActorCategory.LogicBlock, "Heater", "Lib"));
            vitals.OnHandled("logicblock_Heater_1", new object(), TimeSpan.FromMilliseconds(200), null);
            vitals.OnHandled("logicblock_Heater_1", new object(), TimeSpan.FromMilliseconds(300), new InvalidOperationException());

            // Act
            using var meter = new ActorVitalsMeter(vitals);

            // Assert
            Assert.AreEqual(2L, Collect<long>("vion.actor.messages_handled").Single().Value, "Handled messages accumulate.");
            Assert.AreEqual(1L, Collect<long>("vion.actor.errors").Single().Value, "So do errors.");
            Assert.AreEqual(0.5, Collect<double>("vion.actor.handler_duration").Single().Value, 0.0001, "Cumulative handler time is published in seconds.");
            Assert.AreEqual(0.3, Collect<double>("vion.actor.handler_duration_max").Single().Value, 0.0001, "So is the windowed greatest.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-019.2")]
        public void PublishMailboxDepthAndItsPeakAsCounts()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            vitals.Register("MqttClient", new ActorIdentity(ActorCategory.Runtime, "MqttClient", null));
            vitals.OnMessagePosted("MqttClient");
            vitals.OnMessagePosted("MqttClient");
            vitals.OnMessagePosted("MqttClient");
            vitals.OnMessageReceived("MqttClient");

            // Act
            using var meter = new ActorVitalsMeter(vitals);

            // Assert
            Assert.AreEqual(2L, Collect<long>("vion.actor.mailbox_depth").Single().Value, "Depth is instantaneous — the backlog right now.");
            Assert.AreEqual(3L, Collect<long>("vion.actor.mailbox_depth_max").Single().Value, "The peak is over the recent window.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-019.2")]
        public void PublishTimerVitalsInSeconds()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            vitals.Register("logicblock_Heater_1", new ActorIdentity(ActorCategory.LogicBlock, "Heater", "Lib"));
            vitals.OnTimerCallback("logicblock_Heater_1", TimeSpan.FromMilliseconds(120), TimeSpan.FromMilliseconds(40));

            // Act
            using var meter = new ActorVitalsMeter(vitals);

            // Assert
            Assert.AreEqual(0.12, Collect<double>("vion.actor.timer_callback_duration_max").Single().Value, 0.0001);
            Assert.AreEqual(0.04, Collect<double>("vion.actor.timer_jitter_max").Single().Value, 0.0001);
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-019.3")]
        public void TagLogicBlockMeasurementWithItsClassInstanceAndLibrary()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            vitals.Register("logicblock_Heater_1", new ActorIdentity(ActorCategory.LogicBlock, "Heater", "Vion.Examples.Energy"));
            vitals.OnHandled("logicblock_Heater_1", new object(), TimeSpan.FromMilliseconds(2), null);

            // Act
            using var meter = new ActorVitalsMeter(vitals);

            // Assert
            var tags = Collect<long>("vion.actor.messages_handled").Single().Tags;
            Assert.AreEqual("logic-block", Tag(tags, "actor.kind"));
            Assert.AreEqual("Heater", Tag(tags, "logicblock.type"), "The fleet tier aggregates on the class, so the class is a wire.");
            Assert.AreEqual("logicblock_Heater_1", Tag(tags, "logicblock.id"), "The instance tag is emitted so the export can drop it.");
            Assert.AreEqual("Vion.Examples.Energy", Tag(tags, "library"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-019.3")]
        public void TagRuntimeMeasurementWithItsRole()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            vitals.Register("MqttClient", new ActorIdentity(ActorCategory.Runtime, "MqttClient", null));
            vitals.OnMessagePosted("MqttClient");

            // Act
            using var meter = new ActorVitalsMeter(vitals);

            // Assert
            var tags = Collect<long>("vion.actor.mailbox_depth").Single().Tags;
            Assert.AreEqual("runtime", Tag(tags, "actor.kind"));
            Assert.AreEqual("MqttClient", Tag(tags, "role"), "A runtime actor is a singleton, so it carries no library and no instance tag.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-019.3")]
        public void TagMeasurementOfActorWithNoRecordedIdentityAsUnknown()
        {
            // Arrange
            var vitals = new RuntimeVitals(_clock);
            vitals.OnHandled("seen_only_in_traffic", new object(), TimeSpan.FromMilliseconds(1), null);

            // Act
            using var meter = new ActorVitalsMeter(vitals);

            // Assert
            var tags = Collect<long>("vion.actor.messages_handled").Single().Tags;
            Assert.AreEqual("unknown", Tag(tags, "actor.kind"), "A series that vanishes is worse than one labelled unknown.");
            Assert.AreEqual("seen_only_in_traffic", Tag(tags, "actor.id"));
        }

        private static List<string> PublishedInstruments()
        {
            var names = new List<string>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, _) =>
                                           {
                                               if (instrument.Meter.Name == ActorVitalsMeter.MeterName)
                                               {
                                                   names.Add(instrument.Name);
                                               }
                                           };
            listener.Start();
            return names;
        }

        private static List<(T Value, KeyValuePair<string, object?>[] Tags)> Collect<T>(string instrumentName)
            where T : struct
        {
            var results = new List<(T, KeyValuePair<string, object?>[])>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, enable) =>
                                           {
                                               if (instrument.Meter.Name == ActorVitalsMeter.MeterName && instrument.Name == instrumentName)
                                               {
                                                   enable.EnableMeasurementEvents(instrument);
                                               }
                                           };
            listener.SetMeasurementEventCallback<T>((_, value, tags, _) => results.Add((value, tags.ToArray())));
            listener.Start();
            listener.RecordObservableInstruments();
            return results;
        }

        private static object? Tag(KeyValuePair<string, object?>[] tags, string key)
        {
            return tags.Single(tag => tag.Key == key).Value;
        }
    }
}