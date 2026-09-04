using System;
using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Mqtt
{
    /// <summary>
    ///     The message vocabulary the SDK publishes so a host outside the private runtime can drive a provider
    ///     face and a contract. What the MQTT client does with these — the registrations it aborts, the
    ///     prefixing it applies, the retry its attempt counter drives — is the runtime's and is specified
    ///     nowhere here; the one consequence of that split an author has to know is the last test below.
    /// </summary>
    [TestClass]
    public class HandlerProtocolShould
    {
        private static IEnumerable<object[]> ProtocolMessages
        {
            get
            {
                yield return [new RegisterMqttHandlerRequest()];
                yield return [new RegisterMqttHandlerResponse()];
                yield return [new RegisterMqttHandler("Handler", "key", [])];
                yield return [new MqttTopicGroup([])];
                yield return [new MqttMessageReceived("topic", default, null, null, [])];
                yield return [new PublishMqttMessage("topic")];
                yield return [new PublishMqttMessageRequest("topic")];
                yield return [new PublishMqttMessageResponse(true)];
                yield return [new RegisterMessageToSendOnConnect(new PublishMqttMessage("topic"))];
                yield return [new RegisterServiceProvider()];
            }
        }

        private static IEnumerable<object[]> Envelopes
        {
            get
            {
                yield return [new ContractMessage<PokeBindProbe>(new LogicBlockContractId(new LogicBlockId("b"), "c"), new PokeBindProbe(1))];
                yield return
                [
                    new FunctionInterfaceMessage<BindLinkContract.Nudge>(new InterfaceId(new LogicBlockId("b"), "e"),
                                                                         new InterfaceId(new LogicBlockId("b2"), "e2"),
                                                                         new BindLinkContract.Nudge(1)),
                ];
                yield return [new LinkLogicBlockContractActors([])];
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-016.1")]
        [DynamicData(nameof(ProtocolMessages))]
        public void PublishConstructibleHandlerProtocolMessage(object message)
        {
            // Arrange / Act
            var type = message.GetType();

            // Assert
            Assert.IsTrue(type.IsPublic, $"{type.Name} is not on the published surface.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-016.1")]
        public void ConvertBetweenBothPublishForms()
        {
            // Arrange
            var message = new PublishMqttMessage("topic",
                                                 [1],
                                                 "application/x-flatbuffers",
                                                 [2],
                                                 "answer/here",
                                                 [],
                                                 true);

            // Act
            var roundTripped = message.ToRequest().ToMessage();

            // Assert
            Assert.AreEqual(message, roundTripped);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-016.2")]
        public void AnswerRegistrationRequestOfHandlerWithEmptyRoutingKey()
        {
            // Arrange — the client skips a registration whose routing key is empty; the answer is sent anyway.
            var context = new LifecycleHarness.RecordingActorContext();
            var handler = new BindProbeHandler(string.Empty, []);

            // Act
            ((IActorReceiver)handler).HandleMessageAsync(new RegisterMqttHandlerRequest(), context).GetAwaiter().GetResult();

            // Assert
            Assert.IsInstanceOfType<RegisterMqttHandlerResponse>(context.Responses.Single());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-016.3")]
        [DynamicData(nameof(Envelopes))]
        public void PublishConstructibleEnvelope(object envelope)
        {
            // Arrange / Act
            var type = envelope.GetType();

            // Assert
            Assert.IsTrue(type.IsPublic || type.IsNestedPublic, $"{type.Name} is not on the published surface.");
        }
    }
}