using System;
using System.Collections.Generic;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Constants;
using Vion.Contracts.Hw;
using Vion.Contracts.Hw.Do;
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
        private readonly Dictionary<ServiceProviderContractId, string> _doResponseTopics = [];

        private readonly Dictionary<ServiceProviderContractId, string> _doTopics = [];

        private readonly ILogger _logger;

        private readonly HashSet<LogicBlockContractId> _unmappedContractsReported = [];

        private bool _linkMapReceived;

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
            // The label the publisher sets is what separates this contract's payload from its sibling's,
            // and it is judged before the bytes so a foreign payload is refused by name rather than by
            // whatever the decode happens to make of it. Refusing is a warning because the block's output
            // then holds its last value, which nothing else reports.
            if (message.Schema != nameof(DoStatePayload))
            {
                LogRejectedForeignSchema(message.ContractId, message.Schema, message.Topic);
                return;
            }

            DoStatePayload payload;
            try
            {
                payload = message.GetJsonPayload(HwJsonContext.Default.DoStatePayload);
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
            ForwardToLogicBlocks(message.ContractId, new DigitalOutputChanged(payload.Value));
        }

        /// <inheritdoc />
        protected override void OnContractActorsLinked(LinkLogicBlockContractActors message)
        {
            // Forgetting what was reported under the previous link map is what makes a contract that is
            // still unmapped after a reconfiguration report again, instead of staying silent for the life
            // of the handler actor. An operator who fixed the mapping never reaches the arm at all.
            _unmappedContractsReported.Clear();
            _linkMapReceived = true;
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
                // Before the first link map there is nothing to be unmapped against: the runtime links the
                // block actors before it links the contracts, so a block that drives an output from Ready()
                // reaches this arm on a correctly mapped gateway. That drop stays silent, as it was.
                // After it, once per contract per link map — a block drives its output on every state
                // change, so reporting each dropped write would bury the log under one mis-mapped block.
                if (_linkMapReceived && _unmappedContractsReported.Add(setDigitalOutputMessage.LogicBlockContractId))
                {
                    LogNoServiceProviderContractMappingFound(setDigitalOutputMessage.LogicBlockContractId);
                }

                return;
            }

            var payload = new SetDoPayload(setDigitalOutputMessage.Data.Value);
            foreach (var serviceProviderContractId in mappedServiceProviderContractIds)
            {
                var topic = GetOrAddDoSetTopic(serviceProviderContractId);
                var responseTopic = GetOrAddDoResponseTopic(serviceProviderContractId);
                var correlationId = PublishJson(topic, payload, HwJsonContext.Default.SetDoPayload, nameof(SetDoPayload), responseTopic: responseTopic);
                LogPublishingDoRequest(setDigitalOutputMessage.Data.Value, correlationId, topic);
            }
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

        [LoggerMessage(Level = LogLevel.Debug, Message = "Rejected undecodable DO payload (ServiceProviderContractId={ServiceProviderContractId}, Topic={Topic})")]
        private partial void LogRejectedUndecodablePayload(ServiceProviderContractId serviceProviderContractId, string topic);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message =
                           "Dropped a DO state message labelled with another payload type; no value reached any block and this contract's input holds its last value (ServiceProviderContractId={ServiceProviderContractId}, Schema={Schema}, Topic={Topic})")]
        private partial void LogRejectedForeignSchema(ServiceProviderContractId serviceProviderContractId, string? schema, string topic);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message =
                           "Dropped a set DO command; the linked configuration maps this logic block contract to no service provider contract, so nothing reached the hardware (LogicBlockContractId={LogicBlockContractId})")]
        private partial void LogNoServiceProviderContractMappingFound(LogicBlockContractId logicBlockContractId);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Publishing DO request (Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogPublishingDoRequest(bool value, Guid correlationId, string topic);
    }
}