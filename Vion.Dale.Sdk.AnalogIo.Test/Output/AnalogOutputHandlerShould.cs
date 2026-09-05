using System.Linq;
using Google.FlatBuffers;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Constants;
using Vion.Contracts.FlatBuffers.Hw.Ao;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.AnalogIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.Test.Output
{
    /// <summary>
    ///     The handler that carries a analog output both ways: the command a block writes, published to the
    ///     service provider, and the confirmation that comes back on the state topic. Driven through its own
    ///     message loop; no broker and no device.
    /// </summary>
    [TestClass]
    public class AnalogOutputHandlerShould
    {
        private static readonly string SetTopic =
            $"{HandlerHarness.Installation}/{HandlerHarness.ServiceProviderIdentifier}/{HandlerHarness.ServiceIdentifier}/{HandlerHarness.ContractIdentifier}{Topics.AoSet}";

        private readonly HandlerHarness _harness = new();

        private readonly AnalogOutputHandler _sut = new(NullLogger<AnalogOutputHandler>.Instance);

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.1")]
        public void SubscribeStateTopicUnderServiceProviderWildcard()
        {
            // Arrange / Act
            _harness.Send(_sut, new RegisterMqttHandlerRequest());

            // Assert — the state topic only; the set topic below is published to, never subscribed.
            var registration = (RegisterMqttHandler)_harness.Sent.Single(sent => sent.Message is RegisterMqttHandler).Message;
            CollectionAssert.AreEqual(new[] { $"/+/+/+{Topics.AoState}" }, registration.TopicGroups.Single().Topics);
            Assert.AreEqual(Topics.Ao, registration.TopicRoutingKey);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.1")]
        [DataRow(0.0)]
        [DataRow(4.2)]
        [DataRow(-12.5)]
        public void ForwardConfirmedValueToMappedContract(double value)
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AoState), HandlerHarness.AnalogOutputStatePayload(value)));

            // Assert
            Assert.AreEqual(value, _harness.Forwarded<AnalogOutputChanged>().Single().Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.2")]
        public void ForwardConfirmedValueWhenPayloadVerifies()
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AoState), HandlerHarness.AnalogOutputStatePayload(4.2)));

            // Assert
            Assert.AreEqual(4.2, _harness.Forwarded<AnalogOutputChanged>().Single().Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.2")]
        [DataRow(0, DisplayName = "no payload at all")]
        [DataRow(1, DisplayName = "one byte")]
        [DataRow(10, DisplayName = "ten bytes")]
        [DataRow(28, DisplayName = "half the message")]
        public void ForwardNothingWhenPayloadDoesNotVerify(int length)
        {
            // Arrange
            _harness.Link(_sut);
            var truncated = HandlerHarness.Truncated(HandlerHarness.AnalogOutputStatePayload(4.2), length);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AoState), truncated));

            // Assert
            Assert.IsEmpty(_harness.Forwarded<AnalogOutputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.1")]
        public void PublishCommandToSetTopicNamingResponseTopic()
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));

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
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));

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
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));

            // Assert
            var published = _harness.Published().Single();
            Assert.AreEqual(MessageMimeTypes.FlatBuffer, published.ContentType);
            Assert.AreEqual(nameof(SetAoPayload), published.UserProperties!.Single(property => property.Name == MqttUserProperties.Schema.Name).Value);
            Assert.IsFalse(published.Retain);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.2")]
        public void PublishEachCommandUnderCorrelationIdentifierOfItsOwn()
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(-12.5)));

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
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));

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
                          new ContractMessage<SetAnalogOutput>(new LogicBlockContractId(new LogicBlockId("elsewhere"), HandlerHarness.ContractIdentifier),
                                                               new SetAnalogOutput(4.2)));

            // Assert
            Assert.IsEmpty(_harness.Published());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.4")]
        public void PublishNothingWhenCommandArrivesBeforeLinking()
        {
            // Arrange / Act
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));

            // Assert
            Assert.IsEmpty(_harness.Published());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.5")]
        public void ReuseCommandTopicBuiltForContract()
        {
            // Arrange
            _harness.Link(_sut);
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));

            // Act
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(-12.5)));

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
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));
            var topicBuiltUnderFirstMap = _harness.Published().Single().Topic;

            // Act — the runtime replaces the map wholesale, and the same contract is mapped again.
            _harness.Send(_sut, new LinkLogicBlockContractActors([]));
            _harness.Link(_sut);
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));

            // Assert — the handler never releases a topic it built, so the cached instance survives the replacement.
            Assert.IsTrue(ReferenceEquals(topicBuiltUnderFirstMap, _harness.Published().Single().Topic));
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-007.2")]
        [DataRow(0.0, DisplayName = "zero")]
        [DataRow(4.2, DisplayName = "an ordinary setpoint")]
        [DataRow(-12.5, DisplayName = "a negative setpoint")]
        [DataRow(double.NaN, DisplayName = "not a number")]
        [DataRow(double.PositiveInfinity, DisplayName = "positive infinity")]
        [DataRow(double.NegativeInfinity, DisplayName = "negative infinity")]
        [DataRow(double.MaxValue, DisplayName = "the largest value the type holds")]
        [DataRow(double.Epsilon, DisplayName = "the smallest value above zero")]
        public void PublishCommandValueUnaltered(double value)
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(value)));

            // Assert
            Assert.AreEqual(value, SetAoPayload.GetRootAsSetAoPayload(new ByteBuffer(_harness.Published().Single().Payload!)).Value);
        }
    }
}