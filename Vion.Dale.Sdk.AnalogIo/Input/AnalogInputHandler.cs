using System;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Hw;
using Vion.Contracts.Hw.Ai;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.AnalogIo.Input
{
    /// <summary>
    ///     Handles communication between logic block analog inputs and the HAL via MQTT.
    /// </summary>
    [InternalApi]
    [ScenarioWire(Inbound = typeof(AnalogInputChanged))]
    public partial class AnalogInputHandler : ServiceProviderHandlerBase
    {
        private readonly ILogger _logger;

        /// <summary>
        ///     Initializes a new instance of the <see cref="AnalogInputHandler" /> class.
        /// </summary>
        /// <param name="logger">The logger used for logging.</param>
        public AnalogInputHandler(ILogger<AnalogInputHandler> logger) : base(logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (Topics.Ai, [Topics.AiState]);
        }

        /// <summary>
        ///     Analog inputs are read-only — no contract messages from logic blocks.
        /// </summary>
        protected override void HandleContractMessage(IContractMessage message)
        {
        }

        /// <inheritdoc />
        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
            // The label the publisher sets is what separates this contract's payload from its sibling's,
            // and it is judged before the bytes so a foreign payload is refused by name rather than by
            // whatever the decode happens to make of it. Refusing is a warning because the block's input
            // then holds its last value, which nothing else reports.
            if (message.Schema != nameof(AiStatePayload))
            {
                LogRejectedForeignSchema(message.ContractId, message.Schema, message.Topic);
                return;
            }

            AiStatePayload payload;
            try
            {
                payload = message.GetJsonPayload(HwJsonContext.Default.AiStatePayload);
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
            ForwardToLogicBlocks(message.ContractId, new AnalogInputChanged(payload.Value));
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Received AI state change (ServiceProviderContractId={ServiceProviderContractId}, Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogReceivedStateChange(ServiceProviderContractId serviceProviderContractId, double value, Guid correlationId, string topic);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Rejected undecodable AI payload (ServiceProviderContractId={ServiceProviderContractId}, Topic={Topic})")]
        private partial void LogRejectedUndecodablePayload(ServiceProviderContractId serviceProviderContractId, string topic);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message =
                           "Dropped a AI state message labelled with another payload type; no value reached any block and this contract's input holds its last value (ServiceProviderContractId={ServiceProviderContractId}, Schema={Schema}, Topic={Topic})")]
        private partial void LogRejectedForeignSchema(ServiceProviderContractId serviceProviderContractId, string? schema, string topic);
    }
}