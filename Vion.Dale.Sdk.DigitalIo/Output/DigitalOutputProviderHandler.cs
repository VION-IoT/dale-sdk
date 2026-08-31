using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.DigitalIo.Output
{
    /// <summary>
    ///     Services the <see cref="DigitalOutputProvider" /> contract. Provider faces are development surface:
    ///     the handler subscribes to no MQTT topic and moves no message, because the only host that routes a
    ///     provider contract is the development host, which stands in for this handler.
    /// </summary>
    [InternalApi]
    [ScenarioWire(Inbound = typeof(SetDigitalOutput), Outbound = typeof(DigitalOutputChanged))]
    public class DigitalOutputProviderHandler : ServiceProviderHandlerBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DigitalOutputProviderHandler" /> class.
        /// </summary>
        /// <param name="logger">The logger used for logging.</param>
        public DigitalOutputProviderHandler(ILogger<DigitalOutputProviderHandler> logger) : base(logger)
        {
        }

        /// <summary>
        ///     Provider faces carry no hardware transport — no routing key, no topics.
        /// </summary>
        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (string.Empty, Array.Empty<string>());
        }

        /// <summary>
        ///     No topics are subscribed, so no MQTT message reaches this handler.
        /// </summary>
        protected override void HandleMqttMessage(ServiceProviderMqttMessage message)
        {
        }

        /// <summary>
        ///     Confirmations are delivered by the development host's stand-in — nothing to publish here.
        /// </summary>
        protected override void HandleContractMessage(IContractMessage message)
        {
        }
    }
}