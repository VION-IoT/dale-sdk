using System;
using System.Buffers;
using System.Text;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Abstractions
{
    /// <summary>What a provider face's handler can read off a message it received.</summary>
    [TestClass]
    public class ServiceProviderMqttMessageShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.8")]
        public void ReadJsonPayloadThroughSuppliedTypeMetadata()
        {
            // Arrange — a document only the supplied metadata reads in full: the shared options expect
            // camel-cased names, so they would leave the measured value at its default.
            var message = Received(new ReadOnlySequence<byte>(Encoding.UTF8.GetBytes("""{"measured_value":7,"quality":1}""")));

            // Act
            var reading = message.GetJsonPayload(BindProbeSnakeCaseContext.Default.BindProbeReading);

            // Assert
            Assert.AreEqual(new BindProbeReading(7, BindProbeQuality.Uncertain), reading);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.8")]
        public void ReadSegmentedJsonPayloadThroughSuppliedTypeMetadata()
        {
            // Arrange — the same document split inside a member name, so the read goes through a reader
            // over both segments rather than through one span.
            var first = new Segment(Encoding.UTF8.GetBytes("""{"measured_val"""));
            var last = first.Append(Encoding.UTF8.GetBytes("""ue":7,"quality":1}"""));
            var message = Received(new ReadOnlySequence<byte>(first, 0, last, last.Memory.Length));

            // Act
            var reading = message.GetJsonPayload(BindProbeSnakeCaseContext.Default.BindProbeReading);

            // Assert
            Assert.AreEqual(new BindProbeReading(7, BindProbeQuality.Uncertain), reading);
        }

        private static ServiceProviderMqttMessage Received(ReadOnlySequence<byte> payload)
        {
            return new ServiceProviderMqttMessage(new MqttMessageReceived("probe/topic", payload, null, null, []), new ServiceProviderContractId("sp", "svc", "c1"), Guid.Empty);
        }

        private sealed class Segment : ReadOnlySequenceSegment<byte>
        {
            public Segment(byte[] bytes)
            {
                Memory = bytes;
            }

            public Segment Append(byte[] bytes)
            {
                var next = new Segment(bytes) { RunningIndex = RunningIndex + Memory.Length };
                Next = next;
                return next;
            }
        }
    }
}