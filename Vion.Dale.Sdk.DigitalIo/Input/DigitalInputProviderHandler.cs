using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.DigitalIo.Input
{
    /// <summary>
    ///     Services the <see cref="DigitalInputProvider" /> contract. Provider faces are development surface:
    ///     the handler subscribes to no MQTT topic and moves no message, because the only host that routes a
    ///     provider contract is the development host, which stands in for this handler.
    /// </summary>
    [InternalApi]
    [ScenarioWire(Outbound = typeof(DigitalInputChanged))]
    public class DigitalInputProviderHandler : ServiceProviderHandlerBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="DigitalInputProviderHandler" /> class.
        /// </summary>
        /// <param name="logger">The logger used for logging.</param>
        public DigitalInputProviderHandler(ILogger<DigitalInputProviderHandler> logger) : base(logger)
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
        ///     Driven values are delivered by the development host's stand-in — nothing to publish here.
        /// </summary>
        protected override void HandleContractMessage(IContractMessage message)
        {
        }
    }
}