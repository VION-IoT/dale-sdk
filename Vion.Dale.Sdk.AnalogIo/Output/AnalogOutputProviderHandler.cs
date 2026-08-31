using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.AnalogIo.Output
{
    /// <summary>
    ///     Services the <see cref="AnalogOutputProvider" /> contract. Provider faces are development surface: the only
    ///     host that routes a provider contract is the development host, which stands in for this handler.
    ///     <para>
    ///         A production host must not register it. The handler is not inert if one does — it would claim
    ///         an empty MQTT routing key, which poisons a routing table matched by prefix or substring. The
    ///         <see cref="DevelopmentOnlyHandlerAttribute" /> is what makes that exclusion checkable.
    ///     </para>
    /// </summary>
    [InternalApi]
    [DevelopmentOnlyHandler]
    [ScenarioWire(Inbound = typeof(SetAnalogOutput), Outbound = typeof(AnalogOutputChanged))]
    public class AnalogOutputProviderHandler : ServiceProviderHandlerBase
    {
        /// <summary>
        ///     Initializes a new instance of the <see cref="AnalogOutputProviderHandler" /> class.
        /// </summary>
        /// <param name="logger">The logger used for logging.</param>
        public AnalogOutputProviderHandler(ILogger<AnalogOutputProviderHandler> logger) : base(logger)
        {
        }

        /// <summary>
        ///     Provider faces carry no hardware transport, so there is nothing to subscribe to. The empty
        ///     routing key this returns is why a production host must skip the handler entirely.
        /// </summary>
        protected override (string RoutingKey, string[] ActionPaths) GetMqttRegistration()
        {
            return (string.Empty, Array.Empty<string>());
        }

        /// <summary>
        ///     No topic is subscribed, so no MQTT message reaches this handler.
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