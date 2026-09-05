using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.Test.Input
{
    /// <summary>
    ///     The handler that carries a digital input from the wire to the blocks bound to it: what it
    ///     subscribes, what it accepts as a payload, and who it forwards to. Driven through its own message
    ///     loop; no broker and no device.
    /// </summary>
    [TestClass]
    public class DigitalInputHandlerShould
    {
        private readonly HandlerHarness _harness = new();

        private readonly DigitalInputHandler _sut = new(NullLogger<DigitalInputHandler>.Instance);

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.1")]
        public void SubscribeStateTopicUnderServiceProviderWildcard()
        {
            // Arrange / Act
            _harness.Send(_sut, new RegisterMqttHandlerRequest());

            // Assert
            var registration = (RegisterMqttHandler)_harness.Sent.Single(sent => sent.Message is RegisterMqttHandler).Message;
            CollectionAssert.AreEqual(new[] { $"/+/+/+{Topics.DiState}" }, registration.TopicGroups.Single().Topics);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.1")]
        public void RegisterUnderFamilyRoutingKey()
        {
            // Arrange / Act
            _harness.Send(_sut, new RegisterMqttHandlerRequest());

            // Assert
            var registration = (RegisterMqttHandler)_harness.Sent.Single(sent => sent.Message is RegisterMqttHandler).Message;
            Assert.AreEqual(Topics.Di, registration.TopicRoutingKey);
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
        [DataRow(true)]
        [DataRow(false)]
        public void ForwardStateValueToMappedContract(bool value)
        {
            // Arrange
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), HandlerHarness.DigitalStatePayload(value)));

            // Assert
            var forwarded = _harness.Forwarded<DigitalInputChanged>().Single();
            Assert.AreEqual(HandlerHarness.BlockContract, forwarded.LogicBlockContractId);
            Assert.AreEqual(value, forwarded.Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.1")]
        public void ForwardStateValueWhenPayloadVerifies()
        {
            // Arrange — delivery is the rule this carries; it stands here as the guard's positive control, so
            // the refusals below cannot be read as "the guard refuses everything".
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), HandlerHarness.DigitalStatePayload(true)));

            // Assert
            Assert.IsTrue(_harness.Forwarded<DigitalInputChanged>().Single().Data.Value);
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
            var truncated = HandlerHarness.Truncated(HandlerHarness.DigitalStatePayload(true), length);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), truncated));

            // Assert — the refusal is that nothing reached a block, not that something was logged.
            Assert.IsEmpty(_harness.Forwarded<DigitalInputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.2")]
        public void ForwardValueDecodedFromWiderPayload()
        {
            // Arrange — the neighbouring family's payload has the same layout with a wider value, so the
            // schema check passes and the value read is one nothing sent. Only the schema label the wire
            // carries distinguishes the case, and this side cannot read one.
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), HandlerHarness.AnalogStatePayload(4.2)));

            // Assert — the stated bound of what a verified payload means, not a behaviour worth having.
            Assert.IsTrue(_harness.Forwarded<DigitalInputChanged>().Single().Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.3")]
        public void ForwardContractIdentityReadFromTopicRatherThanPayload()
        {
            // Arrange — the payload's own identity strings say "hw0"/"ep0"; the topic says sp0/svc0/c0.
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), HandlerHarness.DigitalStatePayload(true)));

            // Assert
            Assert.AreEqual(HandlerHarness.BlockContract, _harness.Forwarded<DigitalInputChanged>().Single().LogicBlockContractId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.4")]
        public void ForwardNothingWhenServiceProviderContractUnmapped()
        {
            // Arrange
            _harness.Link(_sut, new ServiceProviderContractId("elsewhere", HandlerHarness.ServiceIdentifier, HandlerHarness.ContractIdentifier));

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), HandlerHarness.DigitalStatePayload(true)));

            // Assert
            Assert.IsEmpty(_harness.Forwarded<DigitalInputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.4")]
        public void ForwardNothingWhenStateArrivesBeforeLinking()
        {
            // Arrange — no link at all yet, which is what a retained state message races on start-up.
            var stateMessage = HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), HandlerHarness.DigitalStatePayload(true));

            // Act
            _harness.Send(_sut, stateMessage);
            _harness.Link(_sut);

            // Assert — linking replays nothing, so the block's first value waits for the next message.
            Assert.IsEmpty(_harness.Forwarded<DigitalInputChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-007.2")]
        [DataRow(true)]
        [DataRow(false)]
        public void ForwardStateValueUnaltered(bool value)
        {
            // Arrange — the inbound half of the value rule; a truth value's whole domain is these two rows.
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), HandlerHarness.DigitalStatePayload(value)));

            // Assert
            Assert.AreEqual(value, _harness.Forwarded<DigitalInputChanged>().Single().Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-003.3")]
        public void PublishNothingWhenContractMessageArrives()
        {
            // Arrange — an input carries no command, so its handler has no path a block can drive.
            _harness.Link(_sut);

            // Act
            _harness.Send(_sut, new ContractMessage<DigitalInputChanged>(HandlerHarness.BlockContract, new DigitalInputChanged(true)));

            // Assert
            Assert.IsEmpty(_harness.Sent);
        }
    }
}