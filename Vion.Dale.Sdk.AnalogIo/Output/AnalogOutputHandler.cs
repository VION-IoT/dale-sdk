using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;
using Google.FlatBuffers;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Constants;
using Vion.Contracts.FlatBuffers.Hw.Ao;
using Vion.Contracts.Mqtt;

namespace Vion.Dale.Sdk.AnalogIo.Output
{
    /// <summary>
    ///     Handles communication between logic block analog output and the HAL via MQTT.
    /// </summary>
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
            var payload = AoStatePayload.GetRootAsAoStatePayload(message.GetFlatBufferPayload());
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
            var mappedServiceProviderContractIds = FindMappedServiceProviderContracts(setAnalogOutputMessage.LogicBlockContractId);
            if (mappedServiceProviderContractIds.Count == 0)
            {
                LogNoServiceProviderContractMappingFound(setAnalogOutputMessage.LogicBlockContractId);
                return;
            }

            var payload = CreateSetAoPayload(setAnalogOutputMessage.Data.Value);
            foreach (var serviceProviderContractId in mappedServiceProviderContractIds)
            {
                var topic = GetOrAddAoSetTopic(serviceProviderContractId);
                var responseTopic = GetOrAddAoResponseTopic(serviceProviderContractId);
                var correlationId = Publish(topic, payload, nameof(SetAoPayload), responseTopic: responseTopic);
                LogPublishingAoRequest(setAnalogOutputMessage.Data.Value, correlationId, topic);
            }
        }

        private static byte[] CreateSetAoPayload(double value)
        {
            var builder = new FlatBufferBuilder(20);
            var payloadOffset = SetAoPayload.CreateSetAoPayload(builder, value);
            SetAoPayload.FinishSetAoPayloadBuffer(builder, payloadOffset);

            return builder.SizedByteArray();
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

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "No service provider contract mapping found for contract — cannot send set AO command (LogicBlockContractId={LogicBlockContractId})")]
        private partial void LogNoServiceProviderContractMappingFound(LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing AO request (Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogPublishingAoRequest(double value, Guid correlationId, string topic);
    }
}