using System;
using System.Collections.Generic;
using Google.FlatBuffers;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Constants;
using Vion.Contracts.FlatBuffers.Hw.Do;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.Output
{
    /// <summary>
    ///     Handles communication between logic block digital output and the HAL via MQTT.
    /// </summary>
    [InternalApi]
    [ScenarioWire(Inbound = typeof(DigitalOutputChanged), Outbound = typeof(SetDigitalOutput))]
    public partial class DigitalOutputHandler : ServiceProviderHandlerBase
    {
        // The serialized size of a SetDoPayload carrying a non-default value, measured rather than guessed:
        // a builder short of it grows once on every command, and one over it wastes the difference on every
        // command. The analog twin's payload is a different size, so the two do not share a literal.
        private const int SetDoPayloadBytes = 20;

        private readonly Dictionary<ServiceProviderContractId, string> _doResponseTopics = [];

        private readonly Dictionary<ServiceProviderContractId, string> _doTopics = [];

        private readonly ILogger _logger;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DigitalOutputHandler" /> class.
        /// </summary>
        /// <param name="logger">The logger used for logging.</param>
        public DigitalOutputHandler(ILogger<DigitalOutputHandler> logger) : base(logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (Topics.Do, [Topics.DoState]);
        }

        /// <inheritdoc />
        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
            /* The buffer check below cannot separate this contract's payload from its sibling's: the two
               layouts are identical, so a payload of the other direction decodes here as a value nobody
               published and every bound block acts on it. The label the publisher sets beside the payload is
               what separates them, and the far side of this wire refuses on the same label.

               Dropping it is a warning and not a debug line, unlike the buffer refusal below: the block's
               input stays at its last value for as long as the mislabelling lasts, which is an outage nothing
               else reports, and nothing routine reaches this arm — the topic carries one payload type and
               every publisher on this wire labels it. Its volume is the publisher's own state-change rate,
               since state is published on change rather than polled, so a per-message warning cannot outrun
               the condition it reports. The buffer refusal stays at debug because an empty payload does
               arrive routinely: state is published retained, so a retained-clear reaches it on a topic
               nothing is wrong with. */
            if (message.Schema != nameof(DoStatePayload))
            {
                LogRejectedForeignSchema(message.ContractId, message.Schema, message.Topic);
                return;
            }

            // An unverified buffer does not fail loudly: a truncated one reads a value out of whatever
            // survived the cut and forwards it as if a device had sent it, and an empty one throws out of
            // the handler. The generated DoStatePayload.VerifyDoStatePayload wrapper cannot be used — it
            // hardcodes an empty file identifier the runtime then rejects — so the verifier is driven
            // directly, with no identifier to check.
            var buffer = message.GetFlatBufferPayload();
            if (!new Verifier(buffer).VerifyBuffer(null, false, DoStatePayloadVerify.Verify))
            {
                LogRejectedUnverifiablePayload(message.ContractId, message.Topic);
                return;
            }

            var payload = DoStatePayload.GetRootAsDoStatePayload(buffer);
            LogReceivedStateChange(message.ContractId, payload.Value, message.CorrelationId, message.Topic);
            ForwardToLogicBlocks(message.ContractId, new DigitalOutputChanged(payload.Value));
        }

        /// <inheritdoc />
        protected override void HandleContractMessage(IContractMessage message)
        {
            if (message is ContractMessage<SetDigitalOutput> m)
            {
                PublishSetDoMqttMessage(m);
            }
        }

        private void PublishSetDoMqttMessage(ContractMessage<SetDigitalOutput> setDigitalOutputMessage)
        {
            var mappedServiceProviderContractIds = FindMappedServiceProviderContracts(setDigitalOutputMessage.LogicBlockContractId);
            if (mappedServiceProviderContractIds.Count == 0)
            {
                LogNoServiceProviderContractMappingFound(setDigitalOutputMessage.LogicBlockContractId);
                return;
            }

            var payload = CreateSetDoPayload(setDigitalOutputMessage.Data.Value);
            foreach (var serviceProviderContractId in mappedServiceProviderContractIds)
            {
                var topic = GetOrAddDoSetTopic(serviceProviderContractId);
                var responseTopic = GetOrAddDoResponseTopic(serviceProviderContractId);
                var correlationId = Publish(topic, payload, nameof(SetDoPayload), MessageMimeTypes.FlatBuffer, responseTopic: responseTopic);
                LogPublishingDoRequest(setDigitalOutputMessage.Data.Value, correlationId, topic);
            }
        }

        private static byte[] CreateSetDoPayload(bool value)
        {
            var builder = new FlatBufferBuilder(SetDoPayloadBytes);
            var payloadOffset = SetDoPayload.CreateSetDoPayload(builder, value);
            SetDoPayload.FinishSetDoPayloadBuffer(builder, payloadOffset);

            return builder.SizedByteArray();
        }

        private string GetOrAddDoSetTopic(ServiceProviderContractId serviceProviderContractId)
        {
            if (!_doTopics.TryGetValue(serviceProviderContractId, out var topic))
            {
                topic = CreateSetTopic(serviceProviderContractId);
                _doTopics[serviceProviderContractId] = topic;
            }

            return topic;
        }

        private string GetOrAddDoResponseTopic(ServiceProviderContractId serviceProviderContractId)
        {
            if (!_doResponseTopics.TryGetValue(serviceProviderContractId, out var topic))
            {
                topic = $"{CreateSetTopic(serviceProviderContractId)}/{ServiceProviderConstants.DaleIdentifier}/response";
                _doResponseTopics[serviceProviderContractId] = topic;
            }

            return topic;
        }

        private static string CreateSetTopic(ServiceProviderContractId serviceProviderContractId)
        {
            return
                $"{MqttConfiguration.InstallationTopic}/{serviceProviderContractId.ServiceProviderIdentifier}/{serviceProviderContractId.ServiceIdentifier}/{serviceProviderContractId.ContractIdentifier}{Topics.DoSet}";
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Received DO state change (ServiceProviderContractId={ServiceProviderContractId}, Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogReceivedStateChange(ServiceProviderContractId serviceProviderContractId, bool value, Guid correlationId, string topic);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Rejected unverifiable DO payload (ServiceProviderContractId={ServiceProviderContractId}, Topic={Topic})")]
        private partial void LogRejectedUnverifiablePayload(ServiceProviderContractId serviceProviderContractId, string topic);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message =
                           "Dropped a DO state message labelled with another payload type; no value reached any block and this contract's input holds its last value (ServiceProviderContractId={ServiceProviderContractId}, Schema={Schema}, Topic={Topic})")]
        private partial void LogRejectedForeignSchema(ServiceProviderContractId serviceProviderContractId, string? schema, string topic);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "No service provider contract mapping found for contract — cannot send set DO command (LogicBlockContractId={LogicBlockContractId})")]
        private partial void LogNoServiceProviderContractMappingFound(LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing DO request (Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogPublishingDoRequest(bool value, Guid correlationId, string topic);
    }
}