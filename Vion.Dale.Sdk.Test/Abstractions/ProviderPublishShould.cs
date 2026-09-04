using System;
using System.Linq;
using System.Text;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Abstractions
{
    /// <summary>
    ///     The conventions a provider face's publish carries onto the wire, read off the message the handler
    ///     addressed to the MQTT client. The far side of that wire is outside this repository, so these are
    ///     the promises a HAL reads and the ones a change here has to be proposed against.
    /// </summary>
    [TestClass]
    public class ProviderPublishShould
    {
        private readonly LifecycleHarness.RecordingActorContext _context = new();

        private readonly BindProbeHandler _sut = new();

        [TestInitialize]
        public void Initialize()
        {
            ((IActorReceiver)_sut).HandleMessageAsync(new RegisterMqttHandlerRequest(), _context).GetAwaiter().GetResult();
            _context.Sent.Clear();
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.1")]
        public void AddressPublishToMqttClient()
        {
            // Arrange / Act
            _sut.PublishProbe();

            // Assert
            Assert.AreEqual(MqttConstants.MqttClientName, _context.Sent.Single().Target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.1")]
        public void CarrySchemaNameAsUserProperty()
        {
            // Arrange / Act
            _sut.PublishProbe();

            // Assert
            var property = Published().UserProperties!.Single();
            Assert.AreEqual(MqttUserProperties.Schema.Name, property.Name);
            Assert.AreEqual("ProbeSchema", property.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.1")]
        [DataRow(false, DisplayName = "the caller does not ask for retention")]
        [DataRow(true, DisplayName = "the caller asks for retention")]
        public void CarryCallersRetainFlag(bool retain)
        {
            // Arrange / Act
            _sut.PublishProbe(retain: retain);

            // Assert
            Assert.AreEqual(retain, Published().Retain);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.1")]
        [DataRow(null, DisplayName = "the caller names no response topic")]
        [DataRow("probe/response", DisplayName = "the caller names a response topic")]
        public void CarryCallersResponseTopic(string? responseTopic)
        {
            // Arrange / Act
            _sut.PublishProbe(responseTopic: responseTopic);

            // Assert
            Assert.AreEqual(responseTopic, Published().ResponseTopic);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.2")]
        public void MintCorrelationIdentifierWhereCallerSuppliesNone()
        {
            // Arrange / Act
            var reported = _sut.PublishProbe();

            // Assert
            Assert.AreNotEqual(Guid.Empty, reported);
            Assert.AreEqual(reported, new Guid(Published().CorrelationData!));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.2")]
        public void ReuseCorrelationIdentifierCallerSupplies()
        {
            // Arrange
            var correlationId = Guid.NewGuid();

            // Act
            var reported = _sut.PublishProbe(correlationId: correlationId);

            // Assert
            Assert.AreEqual(correlationId, reported);
            Assert.AreEqual(correlationId, new Guid(Published().CorrelationData!));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.3")]
        public void DeclareFlatBufferContentTypeWhereCallerNamesNone()
        {
            // Arrange / Act
            _sut.PublishProbe();

            // Assert
            Assert.AreEqual(MessageMimeTypes.FlatBuffer, Published().ContentType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.3")]
        public void DeclareContentTypeCallerNames()
        {
            // Arrange / Act
            _sut.PublishProbe(MessageMimeTypes.Json);

            // Assert
            Assert.AreEqual(MessageMimeTypes.Json, Published().ContentType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.4")]
        public void DeclareJsonContentTypeForJsonPublish()
        {
            // Arrange / Act
            _sut.PublishProbeAsJson(7);

            // Assert
            Assert.AreEqual(MessageMimeTypes.Json, Published().ContentType);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-011.4")]
        public void SerializeJsonPayloadWithSharedOptions()
        {
            // Arrange / Act
            _sut.PublishProbeAsJson(7);

            // Assert
            Assert.AreEqual("7", Encoding.UTF8.GetString(Published().Payload!));
        }

        private PublishMqttMessage Published()
        {
            return _context.Sent.Select(sent => sent.Message).OfType<PublishMqttMessage>().Single();
        }
    }
}