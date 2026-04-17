using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Mqtt;
using Vion.Dale.Sdk.Utils;
using Google.FlatBuffers;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Constants;
using Vion.Contracts.FlatBuffers.Hw.Do;
using Vion.Contracts.Mqtt;

namespace Vion.Dale.Sdk.DigitalIo.Output
{
    /// <summary>
    ///     Handles communication between logic block digital output and the HAL via MQTT.
    /// </summary>
    public partial class DigitalOutputHandler : ServiceProviderHandlerBase
    {
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
            var payload = DoStatePayload.GetRootAsDoStatePayload(message.GetFlatBufferPayload());
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
                var correlationId = Publish(topic, payload, nameof(SetDoPayload), responseTopic: responseTopic);
                LogPublishingDoRequest(setDigitalOutputMessage.Data.Value, correlationId, topic);
            }
        }

        private static byte[] CreateSetDoPayload(bool value)
        {
            var builder = new FlatBufferBuilder(20);
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

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "No service provider contract mapping found for contract — Cannot send set DO command (LogicBlockContractId={LogicBlockContractId})")]
        private partial void LogNoServiceProviderContractMappingFound(LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing DO request (Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogPublishingDoRequest(bool value, Guid correlationId, string topic);
    }
}
