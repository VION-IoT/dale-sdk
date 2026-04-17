using System;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using Vion.Contracts.FlatBuffers.Hw.Di;
using Vion.Contracts.Mqtt;

namespace Vion.Dale.Sdk.DigitalIo.Input
{
    /// <summary>
    ///     Handles communication between logic block digital inputs and the HAL via MQTT.
    /// </summary>
    public partial class DigitalInputHandler : ServiceProviderHandlerBase
    {
        private readonly ILogger _logger;

        /// <summary>
        ///     Initializes a new instance of the <see cref="DigitalInputHandler" /> class.
        /// </summary>
        /// <param name="logger">The logger used for logging.</param>
        public DigitalInputHandler(ILogger<DigitalInputHandler> logger) : base(logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (Topics.Di, [Topics.DiState]);
        }

        /// <summary>
        ///     Digital inputs are read-only — no contract messages from logic blocks.
        /// </summary>
        protected override void HandleContractMessage(IContractMessage message)
        {
        }

        /// <inheritdoc />
        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
            var payload = DiStatePayload.GetRootAsDiStatePayload(message.GetFlatBufferPayload());
            LogReceivedStateChange(message.ContractId, payload.Value, message.CorrelationId, message.Topic);
            ForwardToLogicBlocks(message.ContractId, new DigitalInputChanged(payload.Value));
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Received DI state change (ServiceProviderContractId={ServiceProviderContractId}, Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogReceivedStateChange(ServiceProviderContractId serviceProviderContractId, bool value, Guid correlationId, string topic);
    }
}
