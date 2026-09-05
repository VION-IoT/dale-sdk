using System.Linq;
using Google.FlatBuffers;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Constants;
using Vion.Contracts.FlatBuffers.Hw.Do;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.DigitalIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.Test.Output
{
    /// <summary>
    ///     The handler that carries a digital output both ways: the command a block writes, published to the
    ///     service provider, and the confirmation that comes back on the state topic. Driven through its own
    ///     message loop; no broker and no device.
    /// </summary>
    [TestClass]
    public class DigitalOutputHandlerShould
    {
        private static readonly string SetTopic =
            $"{HandlerHarness.Installation}/{HandlerHarness.ServiceProviderIdentifier}/{HandlerHarness.ServiceIdentifier}/{HandlerHarness.ContractIdentifier}{Topics.DoSet}";

        private readonly HandlerHarness _harness = new();

        private readonly DigitalOutputHandler _sut = new(NullLogger<DigitalOutputHandler>.Instance);

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.1")]
        public void SubscribeStateTopicUnderServiceProviderWildcard()
        {
            // Arrange / Act
            _harness.Send(_sut, new RegisterMqttHandlerRequest());

            // Assert — the state topic only; the set topic below is published to, never subscribed.
            var registration = (RegisterMqttHandler)_harness.Sent.Single(sent => sent.Message is RegisterMqttHandler).Message;
            CollectionAssert.AreEqual(new[] { $"/+/+/+{Topics.DoState}" }, registration.TopicGroups.Single().Topics);
            Assert.AreEqual(Topics.Do, registration.TopicRoutingKey);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.1")]
        [DataRow(true)]
        [DataRow(false)]
        public void ForwardConfirmedValueToMappedContract(bool value)
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DoState), HandlerHarness.DigitalOutputStatePayload(value)));

            // Assert
            Assert.AreEqual(value, _harness.Forwarded<DigitalOutputChanged>().Single().Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.2")]
        public void ForwardConfirmedValueWhenPayloadVerifies()
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DoState), HandlerHarness.DigitalOutputStatePayload(true)));

            // Assert
            Assert.IsTrue(_harness.Forwarded<DigitalOutputChanged>().Single().Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.2")]
        [DataRow(0, DisplayName = "no payload at all")]
        [DataRow(1, DisplayName = "one byte")]
        [DataRow(10, DisplayName = "ten bytes")]
        [DataRow(24, DisplayName = "half the message")]
        public void ForwardNothingWhenPayloadDoesNotVerify(int length)
        {
            // Arrange
            _harness.Link(_sut);
            var truncated = HandlerHarness.Truncated(HandlerHarness.DigitalOutputStatePayload(true), length);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DoState), truncated));

            // Assert
            Assert.IsEmpty(_harness.Forwarded<DigitalOutputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.1")]
        public void PublishCommandToSetTopicNamingResponseTopic()
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));

            // Assert
            var published = _harness.Published().Single();
            Assert.AreEqual(SetTopic, published.Topic);
            Assert.AreEqual($"{SetTopic}/{ServiceProviderConstants.DaleIdentifier}/response", published.ResponseTopic);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.1")]
        public void PublishCommandToMqttClient()
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));

            // Assert
            Assert.AreEqual(_harness.MqttClientActor, _harness.Sent.Single().Target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.2")]
        public void PublishCommandLabelledWithPayloadSchemaAndContentType()
        {
            // Arrange — the label is what the far side dispatches on, so it is part of the command and not decoration.
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));

            // Assert
            var published = _harness.Published().Single();
            Assert.AreEqual(MessageMimeTypes.FlatBuffer, published.ContentType);
            Assert.AreEqual(nameof(SetDoPayload), published.UserProperties!.Single(property => property.Name == MqttUserProperties.Schema.Name).Value);
            Assert.IsFalse(published.Retain);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.2")]
        public void PublishEachCommandUnderCorrelationIdentifierOfItsOwn()
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(false)));

            // Assert
            var published = _harness.Published();
            Assert.HasCount(16, published[0].CorrelationData!);
            CollectionAssert.AreNotEqual(published[0].CorrelationData, published[1].CorrelationData);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.3")]
        public void PublishOnceForEachMappedServiceProviderContract()
        {
            // Arrange
            var providerContract2 = new ServiceProviderContractId("sp1", HandlerHarness.ServiceIdentifier, HandlerHarness.ContractIdentifier);
            _harness.Link(_sut, HandlerHarness.ProviderContract, providerContract2);

            // Act
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));

            // Assert
            var topics = _harness.Published().Select(published => published.Topic).ToList();
            CollectionAssert.AreEquivalent(new[] { SetTopic, SetTopic.Replace(HandlerHarness.ServiceProviderIdentifier, "sp1") }, topics);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.4")]
        public void PublishNothingWhenLogicBlockContractUnmapped()
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut,
                          new ContractMessage<SetDigitalOutput>(new LogicBlockContractId(new LogicBlockId("elsewhere"), HandlerHarness.ContractIdentifier),
                                                                new SetDigitalOutput(true)));

            // Assert
            Assert.IsEmpty(_harness.Published());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.4")]
        public void PublishNothingWhenCommandArrivesBeforeLinking()
        {
            // Arrange / Act
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));

            // Assert
            Assert.IsEmpty(_harness.Published());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.5")]
        public void ReuseCommandTopicBuiltForContract()
        {
            // Arrange
            _harness.Link(_sut);
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));

            // Act
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(false)));

            // Assert — the same instance, so the topic was built once and kept, not rebuilt per command.
            var published = _harness.Published();
            Assert.IsTrue(ReferenceEquals(published[0].Topic, published[1].Topic));
            Assert.IsTrue(ReferenceEquals(published[0].ResponseTopic, published[1].ResponseTopic));
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.5")]
        public void KeepCommandTopicAfterContractMapReplaced()
        {
            // Arrange — the topic is built and cached under the first map.
            _harness.Link(_sut);
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));
            var topicBuiltUnderFirstMap = _harness.Published().Single().Topic;

            // Act — the runtime replaces the map wholesale, and the same contract is mapped again.
            _harness.Send(_sut, new LinkLogicBlockContractActors([]));
            _harness.Link(_sut);
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));

            // Assert — the handler never releases a topic it built, so the cached instance survives the replacement.
            Assert.IsTrue(ReferenceEquals(topicBuiltUnderFirstMap, _harness.Published().Single().Topic));
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-007.2")]
        [DataRow(true)]
        [DataRow(false)]
        public void PublishCommandValueUnaltered(bool value)
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(value)));

            // Assert
            Assert.AreEqual(value, SetDoPayload.GetRootAsSetDoPayload(new ByteBuffer(_harness.Published().Single().Payload!)).Value);
        }
    }
}