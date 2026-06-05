using System;
using System.Collections.Generic;
using System.Diagnostics.Metrics;
using System.Linq;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.Sdk.Test.Diagnostics
{
    [TestClass]
    public class ActorVitalsMeterShould
    {
        [TestMethod]
        public void EmitCumulativeMessageCountsTaggedForALogicBlock()
        {
            var vitals = new RuntimeVitals(new FakeTimeProvider());
            vitals.Register("logicblock_Heater_1", new ActorIdentity(ActorCategory.LogicBlock, "Heater", "Vion.Examples.Energy"));
            vitals.OnHandled("logicblock_Heater_1", new object(), TimeSpan.FromMilliseconds(2), exception: null);
            vitals.OnHandled("logicblock_Heater_1", new object(), TimeSpan.FromMilliseconds(2), new InvalidOperationException());

            using var meter = new ActorVitalsMeter(vitals);
            var measurement = Collect<long>("vion.actor.messages_handled").Single();

            Assert.AreEqual(2L, measurement.Value);
            Assert.AreEqual("logic-block", Tag(measurement.Tags, "actor.kind"));
            Assert.AreEqual("Heater", Tag(measurement.Tags, "logicblock.type"));
            Assert.AreEqual("logicblock_Heater_1", Tag(measurement.Tags, "logicblock.id"));
            Assert.AreEqual("Vion.Examples.Energy", Tag(measurement.Tags, "library"));
        }

        [TestMethod]
        public void EmitMailboxDepthGaugeTaggedForARuntimeActor()
        {
            var vitals = new RuntimeVitals(new FakeTimeProvider());
            vitals.Register("MqttClient", new ActorIdentity(ActorCategory.Runtime, "MqttClient", Library: null));
            vitals.OnMessagePosted("MqttClient");
            vitals.OnMessagePosted("MqttClient");

            using var meter = new ActorVitalsMeter(vitals);
            var measurement = Collect<long>("vion.actor.mailbox_depth").Single();

            Assert.AreEqual(2L, measurement.Value);
            Assert.AreEqual("runtime", Tag(measurement.Tags, "actor.kind"));
            Assert.AreEqual("MqttClient", Tag(measurement.Tags, "role"));
        }

        [TestMethod]
        public void EmitHandlerDurationMaxGaugeInSeconds()
        {
            var vitals = new RuntimeVitals(new FakeTimeProvider());
            vitals.Register("logicblock_Heater_1", new ActorIdentity(ActorCategory.LogicBlock, "Heater", "Lib"));
            vitals.OnHandled("logicblock_Heater_1", new object(), TimeSpan.FromMilliseconds(12), exception: null);

            using var meter = new ActorVitalsMeter(vitals);
            var measurement = Collect<double>("vion.actor.handler_duration_max").Single();

            Assert.AreEqual(0.012, measurement.Value, 0.0001);
        }

        [TestMethod]
        public void EmitCumulativeHandlerDurationInSeconds()
        {
            var vitals = new RuntimeVitals(new FakeTimeProvider());
            vitals.Register("logicblock_Heater_1", new ActorIdentity(ActorCategory.LogicBlock, "Heater", "Lib"));
            vitals.OnHandled("logicblock_Heater_1", new object(), TimeSpan.FromMilliseconds(200), exception: null);
            vitals.OnHandled("logicblock_Heater_1", new object(), TimeSpan.FromMilliseconds(300), exception: null);

            using var meter = new ActorVitalsMeter(vitals);
            var measurement = Collect<double>("vion.actor.handler_duration").Single();

            Assert.AreEqual(0.5, measurement.Value, 0.0001);
        }

        [TestMethod]
        public void EmitPeakMailboxDepthGauge()
        {
            var vitals = new RuntimeVitals(new FakeTimeProvider());
            vitals.Register("MqttClient", new ActorIdentity(ActorCategory.Runtime, "MqttClient", Library: null));
            vitals.OnMessagePosted("MqttClient");
            vitals.OnMessagePosted("MqttClient");
            vitals.OnMessagePosted("MqttClient");
            vitals.OnMessageReceived("MqttClient");

            using var meter = new ActorVitalsMeter(vitals);
            var measurement = Collect<long>("vion.actor.mailbox_depth_max").Single();

            Assert.AreEqual(3L, measurement.Value);
        }

        private static List<(T Value, KeyValuePair<string, object?>[] Tags)> Collect<T>(string instrumentName)
            where T : struct
        {
            var results = new List<(T, KeyValuePair<string, object?>[])>();
            using var listener = new MeterListener();
            listener.InstrumentPublished = (instrument, l) =>
            {
                if (instrument.Meter.Name == ActorVitalsMeter.MeterName && instrument.Name == instrumentName)
                {
                    l.EnableMeasurementEvents(instrument);
                }
            };
            listener.SetMeasurementEventCallback<T>((instrument, value, tags, state) => results.Add((value, tags.ToArray())));
            listener.Start();
            listener.RecordObservableInstruments();
            return results;
        }

        private static object? Tag(KeyValuePair<string, object?>[] tags, string key)
        {
            return tags.Single(t => t.Key == key).Value;
        }
    }
}
