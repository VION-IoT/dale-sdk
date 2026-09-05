using System;
using Google.FlatBuffers;
using Microsoft.Extensions.Logging;
using Vion.Contracts.FlatBuffers.Hw.Di;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.DigitalIo.Input
{
    /// <summary>
    ///     Handles communication between logic block digital inputs and the HAL via MQTT.
    /// </summary>
    [InternalApi]
    [ScenarioWire(Inbound = typeof(DigitalInputChanged))]
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
            // An unverified buffer does not fail loudly: a truncated one reads a value out of whatever
            // survived the cut and forwards it as if a device had sent it, and an empty one throws out of
            // the handler. The generated DiStatePayload.VerifyDiStatePayload wrapper cannot be used — it
            // hardcodes an empty file identifier the runtime then rejects — so the verifier is driven
            // directly, with no identifier to check.
            var buffer = message.GetFlatBufferPayload();
            if (!new Verifier(buffer).VerifyBuffer(null, false, DiStatePayloadVerify.Verify))
            {
                LogRejectedUnverifiablePayload(message.ContractId, message.Topic);
                return;
            }

            var payload = DiStatePayload.GetRootAsDiStatePayload(buffer);
            LogReceivedStateChange(message.ContractId, payload.Value, message.CorrelationId, message.Topic);
            ForwardToLogicBlocks(message.ContractId, new DigitalInputChanged(payload.Value));
        }

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Received DI state change (ServiceProviderContractId={ServiceProviderContractId}, Value={Value}, CorrelationId={CorrelationId}, Topic={Topic})")]
        private partial void LogReceivedStateChange(ServiceProviderContractId serviceProviderContractId, bool value, Guid correlationId, string topic);

        [LoggerMessage(Level = LogLevel.Debug,
                       Message = "Rejected unverifiable DI payload (ServiceProviderContractId={ServiceProviderContractId}, Topic={Topic})")]
        private partial void LogRejectedUnverifiablePayload(ServiceProviderContractId serviceProviderContractId, string topic);
    }
}