using System;
using System.Buffers;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Moq;
using Vion.Contracts.Hw.Ai;
using Vion.Contracts.Hw.Ao;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.Test.TestHelpers
{
    /// <summary>A state message as it arrives: the bytes, and the schema name the publisher labelled them with.</summary>
    internal readonly record struct StatePayload(byte[] Bytes, string? Schema);

    /// <summary>
    ///     Drives a handler through its own message loop against a recorded actor context — the whole of a
    ///     handler's outward behaviour is what it hands that context, so every suite here arranges through
    ///     this and asserts on <see cref="Sent" /> and <see cref="Responses" />. The shape
    ///     <c>Vion.Dale.Sdk.Modbus.Rtu.Test</c> established: a package's test project references only its own
    ///     package, so it mocks <see cref="IActorContext" /> rather than reaching for the core test project's
    ///     recorder. Nothing here touches MQTT, a broker or a device.
    /// </summary>
    internal sealed class HandlerHarness
    {
        internal const string ContractIdentifier = "c0";

        internal const string Installation = "vion/test-installation";

        internal const string LogicBlockIdValue = "lb0";

        internal const string ServiceIdentifier = "svc0";

        internal const string ServiceProviderIdentifier = "sp0";

        internal static readonly LogicBlockContractId BlockContract = new(new LogicBlockId(LogicBlockIdValue), ContractIdentifier);

        internal static readonly ServiceProviderContractId ProviderContract = new(ServiceProviderIdentifier, ServiceIdentifier, ContractIdentifier);

        private readonly Mock<IActorReference> _logicBlockActorMock = new();

        private readonly Mock<IActorReference> _mqttClientActorMock = new();

        internal Mock<IActorContext> ActorContextMock { get; } = new();

        /// <summary>Every message the handler handed the context, in order, with the actor it addressed.</summary>
        internal List<(IActorReference Target, object Message)> Sent { get; } = [];

        /// <summary>Every message the handler answered its sender with.</summary>
        internal List<object> Responses { get; } = [];

        internal IActorReference MqttClientActor
        {
            get => _mqttClientActorMock.Object;
        }

        internal HandlerHarness()
        {
            MqttConfiguration.InstallationTopic = Installation;
            ActorContextMock.Setup(context => context.LookupByName(MqttConstants.MqttClientName)).Returns(_mqttClientActorMock.Object);
            ActorContextMock.Setup(context => context.SendTo(It.IsAny<IActorReference>(), It.IsAny<object>(), It.IsAny<Dictionary<string, string>?>()))
                            .Callback<IActorReference, object, Dictionary<string, string>?>((target, message, _) => Sent.Add((target, message)));
            ActorContextMock.Setup(context => context.RespondToSender(It.IsAny<object>())).Callback<object>(Responses.Add);
        }

        /// <summary>The topic a service provider publishes a state change on, for the given action path suffix.</summary>
        internal static string StateTopic(string actionPath, string contractIdentifier = ContractIdentifier)
        {
            return $"{Installation}/{ServiceProviderIdentifier}/{ServiceIdentifier}/{contractIdentifier}{actionPath}";
        }

        /*
         * The inbound payloads are written as the literal JSON a hardware-abstraction layer puts on the
         * wire, not serialized through the context the handler decodes with. Serializing them here would
         * make the arrange inherit whatever naming policy the handler uses, so a handler reading `Value`
         * where the wire carries `value` would pass — which is the one thing these arrangements exist to
         * catch.
         */
        internal static StatePayload AnalogStatePayload(double value)
        {
            return new StatePayload(Json($$"""{"value":{{Number(value)}}}"""), nameof(AiStatePayload));
        }

        internal static StatePayload AnalogOutputStatePayload(double value)
        {
            return new StatePayload(Json($$"""{"value":{{Number(value)}}}"""), nameof(AoStatePayload));
        }

        /// <summary>The neighbouring family's input payload — a truth value under its own label.</summary>
        internal static StatePayload DigitalStatePayload(bool value)
        {
            return new StatePayload(Json($$"""{"value":{{Truth(value)}}}"""), "DiStatePayload");
        }

        /// <summary>The neighbouring family's output payload, for the same reason as <see cref="DigitalStatePayload" />.</summary>
        internal static StatePayload DigitalOutputStatePayload(bool value)
        {
            return new StatePayload(Json($$"""{"value":{{Truth(value)}}}"""), "DoStatePayload");
        }

        /// <summary>An arbitrary document under this topic's own label — a payload the decode must refuse.</summary>
        internal static StatePayload Undecodable(string json, string schema)
        {
            return new StatePayload(Json(json), schema);
        }

        /// <summary>The same bytes under a different schema label — the one arrangement in which only the label is wrong.</summary>
        internal static StatePayload Labelled(StatePayload payload, string schema)
        {
            return payload with { Schema = schema };
        }

        /// <summary>The same payload with its schema label stripped.</summary>
        internal static StatePayload Unlabelled(StatePayload payload)
        {
            return payload with { Schema = null };
        }

        internal static MqttMessageReceived MqttMessage(string topic, StatePayload payload)
        {
            var userProperties = payload.Schema == null ? new List<MqttUserProperty>() : [new MqttUserProperty(MqttUserProperties.Schema.Name, payload.Schema)];

            return new MqttMessageReceived(topic, new ReadOnlySequence<byte>(payload.Bytes), null, null, userProperties);
        }

        /// <summary>Hands the handler one message on its own message loop, the way the actor system would.</summary>
        internal void Send(ServiceProviderHandlerBase handler, object message)
        {
            ((IActorReceiver)handler).HandleMessageAsync(message, ActorContextMock.Object).GetAwaiter().GetResult();
        }

        /// <summary>Links one logic-block contract to one service-provider contract, as the runtime does at start-up.</summary>
        internal void Link(ServiceProviderHandlerBase handler, params ServiceProviderContractId[] providerContracts)
        {
            var map = providerContracts.ToDictionary(providerContract => providerContract,
                                                     _ => new Dictionary<LogicBlockContractId, IActorReference> { [BlockContract] = _logicBlockActorMock.Object });
            Send(handler, new LinkLogicBlockContractActors(map));
            Sent.Clear();
        }

        internal void Link(ServiceProviderHandlerBase handler)
        {
            Link(handler, ProviderContract);
        }

        /// <summary>Everything the handler published, in order.</summary>
        internal List<PublishMqttMessage> Published()
        {
            return Sent.Select(sent => sent.Message).OfType<PublishMqttMessage>().ToList();
        }

        /// <summary>Everything the handler forwarded to a logic block, in order.</summary>
        internal List<ContractMessage<TData>> Forwarded<TData>()
            where TData : struct
        {
            return Sent.Select(sent => sent.Message).OfType<ContractMessage<TData>>().ToList();
        }

        private static byte[] Json(string document)
        {
            return Encoding.UTF8.GetBytes(document);
        }

        private static string Truth(bool value)
        {
            return value ? "true" : "false";
        }

        private static string Number(double value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }
    }
}