using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Constants;
using Vion.Contracts.Hw;
using Vion.Contracts.Hw.Ao;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.Output
{
    /// <summary>
    ///     Handles communication between logic block analog output and the HAL via MQTT.
    /// </summary>
    [InternalApi]
    [ScenarioWire(Inbound = typeof(AnalogOutputChanged), Outbound = typeof(SetAnalogOutput))]
    public partial class AnalogOutputHandler : ServiceProviderHandlerBase
    {
        private readonly Dictionary<ServiceProviderContractId, string> _aoResponseTopics = [];

        private readonly Dictionary<ServiceProviderContractId, string> _aoTopics = [];

        private readonly ILogger _logger;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AnalogOutputHandler" /> class.
        /// </summary>
        /// <param name="logger">The logger used for logging.</param>
        public AnalogOutputHandler(ILogger<AnalogOutputHandler> logger) : base(logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (Topics.Ao, [Topics.AoState]);
        }

        /// <inheritdoc />
        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
            // The label the publisher sets is what separates this contract's payload from its sibling's,
            // and it is judged before the bytes so a foreign payload is refused by name rather than by
            // whatever the decode happens to make of it. Refusing is a warning because the block's output
            // then holds its last value, which nothing else reports.
            if (message.Schema != nameof(AoStatePayload))
            {
                LogRejectedForeignSchema(message.ContractId, message.Schema, message.Topic);
                return;
            }

            AoStatePayload payload;
            try
            {
                payload = message.GetJsonPayload(HwJsonContext.Default.AoStatePayload);
            }
            catch (Exception exception) when (exception is JsonException or InvalidOperationException)
            {
                // An empty, truncated or wrong-typed document throws out of the decode, and a bare `null`
                // one deserializes to nothing and is refused by the read itself. Letting either leave this
                // arm would put a stack trace in the gateway's log for what is an ordinary bad frame, and
                // the actor middleware that caught it would drop the message anyway.
                LogRejectedUndecodablePayload(message.ContractId, message.Topic);
                return;
            }

            LogReceivedStateChange(message.ContractId, payload.Value, message.CorrelationId, message.Topic);
            ForwardToLogicBlocks(message.ContractId, new AnalogOutputChanged(payload.Value));
        }

        /// <inheritdoc />
        protected override void HandleContractMessage(IContractMessage message)
        {
            if (message is ContractMessage<SetAnalogOutput> m)
            {
                PublishSetAoMqttMessage(m);
            }
        }

        private void PublishSetAoMqttMessage(ContractMessage<SetAnalogOutput> setAnalogOutputMessage)
        {
            // JSON has no number for NaN or either infinity, so no document can carry one to the service
            // provider, and serializing one throws out of this arm into the actor middleware. Dropping it
            // here reports the refusal against the contract instead of as a stack trace.
            if (!double.IsFinite(setAnalogOutputMessage.Data.Value))
            {
                LogRejectedNonFiniteCommand(setAnalogOutputMessage.LogicBlockContractId, setAnalogOutputMessage.Data.Value);
                return;
            }

            var mappedServiceProviderContractIds = FindMappedServiceProviderContracts(setAnalogOutputMessage.LogicBlockContractId);
            if (mappedServiceProviderContractIds.Count == 0)
            {
                LogNoServiceProviderContractMappingFound(setAnalogOutputMessage.LogicBlockContractId);
                return;
            }

            var payload = new SetAoPayload(setAnalogOutputMessage.Data.Value);
            foreach (var serviceProviderContractId in mappedServiceProviderContractIds)
            {
                var topic = GetOrAddAoSetTopic(serviceProviderContractId);
                var responseTopic = GetOrAddAoResponseTopic(serviceProviderContractId);
                var correlationId = PublishJson(topic, payload, HwJsonContext.Default.SetAoPayload, nameof(SetAoPayload), responseTopic: responseTopic);
                LogPublishingAoRequest(setAnalogOutputMessage.Data.Value, correlationId, topic);
            }
        }

        private string GetOrAddAoSetTopic(ServiceProviderContractId serviceProviderContractId)
        {
            if (!_aoTopics.TryGetValue(serviceProviderContractId, out var topic))
            {
                topic = CreateSetTopic(serviceProviderContractId);
                _aoTopics[serviceProviderContractId] = topic;
            }

            return topic;
        }

        private string GetOrAddAoResponseTopic(ServiceProviderContractId serviceProviderContractId)
        {
            if (!_aoResponseTopics.TryGetValue(serviceProviderContractId, out var topic))
            {
                topic = $"{CreateSetTopic(serviceProviderContractId)}/{ServiceProviderConstants.DaleIdentifier}/response";
                _aoResponseTopics[serviceProviderContractId] = topic;
            }

            return topic;
        }

        private static string CreateSetTopic(ServiceProviderContractId serviceProviderContractId)
        {
            return
                $"{MqttConfiguration.InstallationTopic}/{serviceProviderContractId.ServiceProviderIdentifier}/{serviceProviderContractId.ServiceIdentifier}/{serviceProviderContractId.ContractIdentifier}{Topics.AoSet}";
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Received AO state change (ServiceProviderContractId={ServiceProviderContractId}, Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogReceivedStateChange(ServiceProviderContractId serviceProviderContractId, double value, Guid correlationId, string topic);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Rejected undecodable AO payload (ServiceProviderContractId={ServiceProviderContractId}, Topic={Topic})")]
        private partial void LogRejectedUndecodablePayload(ServiceProviderContractId serviceProviderContractId, string topic);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message =
                           "Dropped a AO state message labelled with another payload type; no value reached any block and this contract's input holds its last value (ServiceProviderContractId={ServiceProviderContractId}, Schema={Schema}, Topic={Topic})")]
        private partial void LogRejectedForeignSchema(ServiceProviderContractId serviceProviderContractId, string? schema, string topic);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message = "Dropped a AO command whose value is not finite; JSON carries no number for it, so no command reached any service provider (LogicBlockContractId={LogicBlockContractId}, Value={Value})")]
        private partial void LogRejectedNonFiniteCommand(LogicBlockContractId logicBlockContractId, double value);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "No service provider contract mapping found for contract — cannot send set AO command (LogicBlockContractId={LogicBlockContractId})")]
        private partial void LogNoServiceProviderContractMappingFound(LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing AO request (Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogPublishingAoRequest(double value, Guid correlationId, string topic);
    }
}