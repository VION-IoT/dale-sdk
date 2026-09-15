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
            var document = Encoding.UTF8.GetBytes("""{"measured_value":7,"quality":1}""");
            var received = new MqttMessageReceived("probe/topic", new ReadOnlySequence<byte>(document), null, null, []);
            var message = new ServiceProviderMqttMessage(received, new ServiceProviderContractId("sp", "svc", "c1"), Guid.Empty);

            // Act
            var reading = message.GetJsonPayload(BindProbeSnakeCaseContext.Default.BindProbeReading);

            // Assert
            Assert.AreEqual(new BindProbeReading(7, BindProbeQuality.Uncertain), reading);
        }
    }
}