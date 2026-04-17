using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Vion.Contracts.FlatBuffers.Hw.Ai;
using Vion.Contracts.Mqtt;

namespace Vion.Dale.Sdk.AnalogIo.Input
{
    /// <summary>
    ///     Handles communication between logic block analog inputs and the HAL via MQTT.
    /// </summary>
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
            var payload = AiStatePayload.GetRootAsAiStatePayload(message.GetFlatBufferPayload());
            LogReceivedStateChange(message.ContractId, payload.Value, message.CorrelationId, message.Topic);
            ForwardToLogicBlocks(message.ContractId, new AnalogInputChanged(payload.Value));
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Received AI state change (ServiceProviderContractId={ServiceProviderContractId}, Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogReceivedStateChange(ServiceProviderContractId serviceProviderContractId, double value, Guid correlationId, string topic);
    }
}