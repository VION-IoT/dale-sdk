using System.Linq;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Constants;
using Vion.Contracts.Hw;
using Vion.Contracts.Hw.Ai;
using Vion.Contracts.Hw.Ao;
using Vion.Contracts.Hw.Do;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.AnalogIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.Test.Output
{
    /// <summary>
    ///     The handler that carries an analog output both ways: the command a block writes, published to the
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
        [TestProperty("spec", "AC-IO-005.1")]
        public void ForwardConfirmedValueWhenPayloadDecodes()
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
        [DataRow("", DisplayName = "no payload at all")]
        [DataRow("{", DisplayName = "an opening brace alone")]
        [DataRow("""{"value":""", DisplayName = "cut off before the value")]
        [DataRow("null", DisplayName = "a bare null document")]
        [DataRow("not json", DisplayName = "not JSON at all")]
        [DataRow("""{"value":true}""", DisplayName = "a truth value where a real number belongs")]
        [DataRow("""{"value":"4.2"}""", DisplayName = "a real number spelled as a string")]
        [DataRow("""{"value":"NaN"}""", DisplayName = "not a number spelled as a named literal")]
        [DataRow("""{"value":"Infinity"}""", DisplayName = "positive infinity spelled as a named literal")]
        [DataRow("""{"value":"-Infinity"}""", DisplayName = "negative infinity spelled as a named literal")]
        public void ForwardNothingWhenPayloadUndecodable(string document)
        {
            // Arrange — every row carries this topic's own label, so the decode is the only thing left to
            // refuse on.
            _harness.Link(_sut);
            var undecodable = HandlerHarness.Document(document, nameof(AoStatePayload));

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AoState), undecodable));

            // Assert
            Assert.IsEmpty(_harness.Forwarded<AnalogOutputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.5")]
        [DataRow(nameof(AiStatePayload), DisplayName = "the sibling contract's payload type")]
        [DataRow(nameof(DoStatePayload), DisplayName = "the neighbouring family's payload type")]
        public void ForwardNothingWhenSchemaNamesAnotherPayloadType(string schema)
        {
            // Arrange — this topic's own payload under another payload type's label, so the label is the only
            // thing left to refuse on.
            _harness.Link(_sut);
            var mislabelled = HandlerHarness.Labelled(HandlerHarness.AnalogOutputStatePayload(4.2), schema);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AoState), mislabelled));

            // Assert
            Assert.IsEmpty(_harness.Forwarded<AnalogOutputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.5")]
        public void ForwardNothingWhenSchemaMissing()
        {
            // Arrange — the same payload with no label. Every publisher on this wire sets one.
            _harness.Link(_sut);
            var unlabelled = HandlerHarness.Unlabelled(HandlerHarness.AnalogOutputStatePayload(4.2));

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AoState), unlabelled));

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
            Assert.AreEqual(MessageMimeTypes.Json, published.ContentType);
            Assert.AreEqual(nameof(SetAoPayload), published.UserProperties!.Single(property => property.Name == MqttUserProperties.Schema.Name).Value);
            Assert.IsFalse(published.Retain);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-006.2")]
        [DataRow(4.2, """{"value":4.2}""")]
        [DataRow(0.0, """{"value":0}""")]
        public void PublishCommandPayloadEncodingValueAlone(double value, string expectedDocument)
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(value)));

            // Assert — the document as written, not a round-trip decode: a writer that named the member
            // differently would still decode back to the same value, and the far side reads the text.
            Assert.AreEqual(expectedDocument, Encoding.UTF8.GetString(_harness.Published().Single().Payload!));
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
        [TestProperty("spec", "AC-IO-003.3")]
        public void PublishNothingForContractMessageOtherThanCommand()
        {
            // Arrange — an output's handler carries one command and answers to nothing else; only a
            // mis-declared wire could deliver another kind here.
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<AnalogOutputChanged>(HandlerHarness.BlockContract, new AnalogOutputChanged(4.2)));

            // Assert
            Assert.IsEmpty(_harness.Sent);
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
        [TestProperty("spec", "AC-IO-006.5")]
        public void KeepCommandTopicWhenInstallationTopicSetAgain()
        {
            // Arrange — the topic is built and cached under the installation topic the harness set.
            _harness.Link(_sut);
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(4.2)));
            var topicBuiltAtFirstCommand = _harness.Published().Single().Topic;

            // Act — the process-global installation topic set a second time. Nothing is restored afterwards
            // because nothing moved: the setter keeps the first value it was given and ignores every later one.
            MqttConfiguration.InstallationTopic = "vion/another-installation";
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(0.0)));

            // Assert — the same instance, so the cached topic survived the attempt rather than being rebuilt
            // to the same string; the second assertion is why nothing could have moved under it.
            Assert.IsTrue(ReferenceEquals(topicBuiltAtFirstCommand, _harness.Published()[1].Topic));
            Assert.AreEqual(HandlerHarness.Installation, MqttConfiguration.InstallationTopic);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-007.2")]
        [DataRow(0.0, DisplayName = "zero")]
        [DataRow(4.2, DisplayName = "an ordinary reading")]
        [DataRow(-12.5, DisplayName = "a negative reading")]
        [DataRow(double.MaxValue, DisplayName = "the largest value the type holds")]
        [DataRow(double.Epsilon, DisplayName = "the smallest value above zero")]
        public void ForwardConfirmedValueUnaltered(double value)
        {
            // Arrange — the inbound half of the value rule: nothing between the wire and the block clamps or
            // rounds an extreme reading, so a HAL that reports one is reported to the block.
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AoState), HandlerHarness.AnalogOutputStatePayload(value)));

            // Assert
            Assert.AreEqual(value, _harness.Forwarded<AnalogOutputChanged>().Single().Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-007.2")]
        [DataRow(0.0, DisplayName = "zero")]
        [DataRow(4.2, DisplayName = "an ordinary setpoint")]
        [DataRow(-12.5, DisplayName = "a negative setpoint")]
        [DataRow(double.MaxValue, DisplayName = "the largest value the type holds")]
        [DataRow(double.Epsilon, DisplayName = "the smallest value above zero")]
        public void PublishCommandValueUnaltered(double value)
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(value)));

            // Assert
            Assert.AreEqual(value, JsonSerializer.Deserialize(_harness.Published().Single().Payload!, HwJsonContext.Default.SetAoPayload)!.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-007.3")]
        [DataRow(double.NaN, DisplayName = "not a number")]
        [DataRow(double.PositiveInfinity, DisplayName = "positive infinity")]
        [DataRow(double.NegativeInfinity, DisplayName = "negative infinity")]
        public void PublishNothingWhenCommandValueNotFinite(double value)
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<SetAnalogOutput>(HandlerHarness.BlockContract, new SetAnalogOutput(value)));

            // Assert
            Assert.IsEmpty(_harness.Published());
        }
    }
}