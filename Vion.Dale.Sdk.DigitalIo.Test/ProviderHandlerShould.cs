using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.DigitalIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;

namespace Vion.Dale.Sdk.DigitalIo.Test
{
    /// <summary>
    ///     The two handlers of the provider faces. They exist to be declared, not to run: a production host
    ///     leaves them out by their marking and the development host stands in for them, so nothing on a real
    ///     host ever constructs one. What they must not do is claim a topic — the routing key they would claim
    ///     is empty, which is the hazard the marking exists to avoid.
    /// </summary>
    [TestClass]
    public class ProviderHandlerShould
    {
        private readonly HandlerHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.3")]
        [DynamicData(nameof(ProviderHandlers))]
        public void SubscribeNothingUnderEmptyRoutingKey(ServiceProviderHandlerBase handler)
        {
            // Arrange / Act
            _harness.Send(handler, new RegisterMqttHandlerRequest());

            // Assert
            var registration = (RegisterMqttHandler)_harness.Sent.Single(sent => sent.Message is RegisterMqttHandler).Message;
            Assert.AreEqual(string.Empty, registration.TopicRoutingKey);
            Assert.IsEmpty(registration.TopicGroups.Single().Topics);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.3")]
        [DynamicData(nameof(ProviderHandlers))]
        public void AnswerRegistrationRequest(ServiceProviderHandlerBase handler)
        {
            // Arrange / Act
            _harness.Send(handler, new RegisterMqttHandlerRequest());

            // Assert — a host waits on the answer, so subscribing to nothing must not withhold it.
            Assert.IsInstanceOfType<RegisterMqttHandlerResponse>(_harness.Responses.Single());
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-005.2")]
        [DynamicData(nameof(ProviderHandlers))]
        public void DecodeNothingWhenMqttMessageArrives(ServiceProviderHandlerBase handler)
        {
            // Arrange — no topic is subscribed, so this cannot happen on a host; it is what makes the guard
            // on the consumer handlers the whole of this area's decode surface.
            _harness.Link(handler);

            // Act
            _harness.Send(handler, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), HandlerHarness.DigitalStatePayload(true)));

            // Assert
            Assert.IsEmpty(_harness.Sent);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-004.3")]
        [DynamicData(nameof(ProviderHandlers))]
        public void PublishNothingWhenContractMessageArrives(ServiceProviderHandlerBase handler)
        {
            // Arrange
            _harness.Link(handler);

            // Act
            _harness.Send(handler, new ContractMessage<SetDigitalOutput>(HandlerHarness.BlockContract, new SetDigitalOutput(true)));

            // Assert
            Assert.IsEmpty(_harness.Sent);
        }

        public static IEnumerable<object[]> ProviderHandlers()
        {
            yield return [new DigitalInputProviderHandler(NullLogger<DigitalInputProviderHandler>.Instance)];
            yield return [new DigitalOutputProviderHandler(NullLogger<DigitalOutputProviderHandler>.Instance)];
        }
    }
}
