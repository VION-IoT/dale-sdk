using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.Test.Input
{
    /// <summary>
    ///     The handler that carries a analog input from the wire to the blocks bound to it: what it
    ///     subscribes, what it accepts as a payload, and who it forwards to. Driven through its own message
    ///     loop; no broker and no device.
    /// </summary>
    [TestClass]
    public class AnalogInputHandlerShould
    {
        private readonly HandlerHarness _harness = new();

        private readonly AnalogInputHandler _sut = new(NullLogger<AnalogInputHandler>.Instance);

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.1")]
        public void SubscribeStateTopicUnderServiceProviderWildcard()
        {
            // Arrange / Act
            _harness.Send(_sut, new RegisterMqttHandlerRequest());

            // Assert
            var registration = (RegisterMqttHandler)_harness.Sent.Single(sent => sent.Message is RegisterMqttHandler).Message;
            CollectionAssert.AreEqual(new[] { $"/+/+/+{Topics.AiState}" }, registration.TopicGroups.Single().Topics);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.1")]
        public void RegisterUnderFamilyRoutingKey()
        {
            // Arrange / Act
            _harness.Send(_sut, new RegisterMqttHandlerRequest());

            // Assert
            var registration = (RegisterMqttHandler)_harness.Sent.Single(sent => sent.Message is RegisterMqttHandler).Message;
            Assert.AreEqual(Topics.Ai, registration.TopicRoutingKey);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.2")]
        public void RegisterAgainWhenRegistrationRequestRepeats()
        {
            // Arrange
            _harness.Send(_sut, new RegisterMqttHandlerRequest());

            // Act — the request a runtime re-issues after a broker reconnect.
            _harness.Send(_sut, new RegisterMqttHandlerRequest());

            // Assert
            Assert.AreEqual(2, _harness.Sent.Count(sent => sent.Message is RegisterMqttHandler));
            Assert.AreEqual(2, _harness.Responses.OfType<RegisterMqttHandlerResponse>().Count());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.1")]
        [DataRow(0.0)]
        [DataRow(4.2)]
        [DataRow(-12.5)]
        public void ForwardStateValueToMappedContract(double value)
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AiState), HandlerHarness.AnalogStatePayload(value)));

            // Assert
            var forwarded = _harness.Forwarded<AnalogInputChanged>().Single();
            Assert.AreEqual(HandlerHarness.BlockContract, forwarded.LogicBlockContractId);
            Assert.AreEqual(value, forwarded.Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.2")]
        public void ForwardStateValueWhenPayloadVerifies()
        {
            // Arrange — the positive half of the guard: a payload a service provider actually sends passes it,
            // so the refusals below cannot be read as "the guard refuses everything".
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AiState), HandlerHarness.AnalogStatePayload(4.2)));

            // Assert
            Assert.AreEqual(4.2, _harness.Forwarded<AnalogInputChanged>().Single().Data.Value);
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
            var truncated = HandlerHarness.Truncated(HandlerHarness.AnalogStatePayload(4.2), length);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AiState), truncated));

            // Assert — the refusal is that nothing reached a block, not that something was logged.
            Assert.IsEmpty(_harness.Forwarded<AnalogInputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.3")]
        public void ForwardContractIdentityReadFromTopicRatherThanPayload()
        {
            // Arrange — the payload's own identity strings say "hw0"/"ep0"; the topic says sp0/svc0/c0.
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AiState), HandlerHarness.AnalogStatePayload(4.2)));

            // Assert
            Assert.AreEqual(HandlerHarness.BlockContract, _harness.Forwarded<AnalogInputChanged>().Single().LogicBlockContractId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.4")]
        public void ForwardNothingWhenServiceProviderContractUnmapped()
        {
            // Arrange
            _harness.Link(_sut, new ServiceProviderContractId("elsewhere", HandlerHarness.ServiceIdentifier, HandlerHarness.ContractIdentifier));

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AiState), HandlerHarness.AnalogStatePayload(4.2)));

            // Assert
            Assert.IsEmpty(_harness.Forwarded<AnalogInputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.4")]
        public void ForwardNothingWhenStateArrivesBeforeLinking()
        {
            // Arrange — no link at all yet, which is what a retained state message races on start-up.
            var stateMessage = HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AiState), HandlerHarness.AnalogStatePayload(4.2));

            // Act
            _harness.Send(_sut, stateMessage);
            _harness.Link(_sut);

            // Assert — linking replays nothing, so the block's first value waits for the next message.
            Assert.IsEmpty(_harness.Forwarded<AnalogInputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-003.3")]
        public void PublishNothingWhenContractMessageArrives()
        {
            // Arrange — an input carries no command, so its handler has no path a block can drive.
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<AnalogInputChanged>(HandlerHarness.BlockContract, new AnalogInputChanged(4.2)));

            // Assert
            Assert.IsEmpty(_harness.Sent);
        }
    }
}