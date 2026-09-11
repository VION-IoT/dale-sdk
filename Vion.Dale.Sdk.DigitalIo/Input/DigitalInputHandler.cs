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
            if (message.Schema != nameof(DiStatePayload))
            {
                LogRejectedForeignSchema(message.ContractId, message.Schema, message.Topic);
                return;
            }

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

        [LoggerMessage(Level = LogLevel.Debug, Message = "Rejected unverifiable DI payload (ServiceProviderContractId={ServiceProviderContractId}, Topic={Topic})")]
        private partial void LogRejectedUnverifiablePayload(ServiceProviderContractId serviceProviderContractId, string topic);

        [LoggerMessage(Level = LogLevel.Warning,
                       Message =
                           "Dropped a DI state message labelled with another payload type; no value reached any block and this contract's input holds its last value (ServiceProviderContractId={ServiceProviderContractId}, Schema={Schema}, Topic={Topic})")]
        private partial void LogRejectedForeignSchema(ServiceProviderContractId serviceProviderContractId, string? schema, string topic);
    }
}