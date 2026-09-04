using System;
using System.Buffers;
using System.Text;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Mqtt
{
    /// <summary>
    ///     What a received MQTT message can be read as. The contract-identity parse runs inside the sealed
    ///     dispatch of <c>ServiceProviderHandlerBase</c>, before a subclass sees anything, so what it throws is
    ///     part of what the base promises rather than something a handler can catch and reshape.
    /// </summary>
    [TestClass]
    public class MqttMessageExtensionsShould
    {
        [TestInitialize]
        public void Initialize()
        {
            MqttTopics.Configure();
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.1")]
        [DataRow("/sp/svc/c1/state/more", DisplayName = "an action path follows the contract")]
        [DataRow("/sp/svc/c1", DisplayName = "the topic ends at the contract")]
        public void ReadContractIdentityFromThreeSegmentsAfterInstallationTopic(string tail)
        {
            // Arrange
            var message = Received(MqttConfiguration.InstallationTopic + tail);

            // Act
            var contractId = message.ExtractServiceProviderContractId();

            // Assert
            Assert.AreEqual(new ServiceProviderContractId("sp", "svc", "c1"), contractId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.2")]
        [DataRow("/sp/svc", DisplayName = "two segments after the installation topic")]
        [DataRow("/sp", DisplayName = "one segment after the installation topic")]
        public void RefuseTopicCarryingTooFewSegments(string tail)
        {
            // Arrange
            var topic = MqttConfiguration.InstallationTopic + tail;
            var message = Received(topic);

            // Act / Assert
            var exception = Assert.Throws<UnexpectedSegmentCountException>(() => message.ExtractServiceProviderContractId());
            StringAssert.Contains(exception.Message, topic);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.2")]
        public void RefuseTopicShorterThanInstallationTopic()
        {
            // Arrange
            var message = Received("ab");

            // Act / Assert
            var exception = Assert.Throws<TopicSubstringNotFoundException>(() => message.ExtractServiceProviderContractId());
            StringAssert.Contains(exception.Message, "ab");
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.3")]
        public void ReadCorrelationIdentifierFromBinaryAndTextForms()
        {
            // Arrange
            var correlationId = Guid.NewGuid();

            // Act
            var fromBinary = Received("t", correlationId.ToByteArray()).TryGetCorrelationId();
            var fromText = Received("t", Encoding.UTF8.GetBytes(correlationId.ToString())).TryGetCorrelationId();

            // Assert
            Assert.AreEqual(correlationId, fromBinary);
            Assert.AreEqual(correlationId, fromText);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.3")]
        [DataRow(new byte[] { 1, 2, 3, 4 }, DisplayName = "correlation data of neither supported length")]
        [DataRow(new byte[0], DisplayName = "empty correlation data")]
        [DataRow(null, DisplayName = "no correlation data at all")]
        public void ReportEmptyCorrelationIdentifierForUnreadableData(byte[]? correlationData)
        {
            // Arrange
            var message = Received("t", correlationData);

            // Act
            var correlationId = message.TryGetCorrelationId();

            // Assert
            Assert.AreEqual(Guid.Empty, correlationId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.4")]
        public void RefuseStrictCorrelationReadWithNoData()
        {
            // Arrange
            var message = Received("strict/topic");

            // Act / Assert
            var exception = Assert.Throws<MissingCorrelationIdException>(() => message.GetCorrelationId());
            StringAssert.Contains(exception.Message, "strict/topic");
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.4")]
        public void RefuseStrictCorrelationReadOfUnparseableData()
        {
            // Arrange
            var message = Received("strict/topic", [1, 2, 3, 4]);

            // Act / Assert
            var exception = Assert.Throws<InvalidCorrelationIdFormatException>(() => message.GetCorrelationId());
            StringAssert.Contains(exception.Message, "strict/topic");
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.6")]
        public void CarryResponseTopicAndCorrelationDataThroughHeaders()
        {
            // Arrange
            var correlationData = Guid.NewGuid().ToByteArray();
            var message = Received("t", correlationData, "answer/here");

            // Act
            var headers = message.GetMessageHeader();

            // Assert
            Assert.IsNotNull(headers);
            Assert.AreEqual("answer/here", headers[MqttConstants.ResponseTopic]);
            Assert.AreEqual(Convert.ToBase64String(correlationData), headers[MqttConstants.CorrelationData]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-012.6")]
        [DataRow(true, false, DisplayName = "correlation data without a response topic")]
        [DataRow(false, true, DisplayName = "a response topic without correlation data")]
        [DataRow(false, false, DisplayName = "neither")]
        public void CarryNoHeadersWithoutBothHalves(bool withCorrelationData, bool withResponseTopic)
        {
            // Arrange
            var message = Received("t", withCorrelationData ? Guid.NewGuid().ToByteArray() : null, withResponseTopic ? "answer/here" : null);

            // Act
            var headers = message.GetMessageHeader();

            // Assert
            Assert.IsNull(headers);
        }

        private static MqttMessageReceived Received(string topic, byte[]? correlationData = null, string? responseTopic = null)
        {
            return new MqttMessageReceived(topic, ReadOnlySequence<byte>.Empty, correlationData, responseTopic, []);
        }
    }
}