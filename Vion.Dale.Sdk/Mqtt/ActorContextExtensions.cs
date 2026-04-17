using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Mqtt
{
    /// <summary>
    ///     Extension methods for <see cref="IActorContext" />.
    /// </summary>
    public static class ActorContextExtensions
    {
        extension(IActorContext actorContext)
        {
            /// <summary>
            ///     Gets the MQTT response topic from the message headers.
            /// </summary>
            /// <returns>The response topic if present, otherwise null.</returns>
            /// <remarks>
            ///     The response topic is used in request/response patterns to indicate where the response should be published.
            /// </remarks>
            public string? GetResponseTopic()
            {
                return actorContext.Headers?.GetValueOrDefault(MqttConstants.ResponseTopic);
            }

            /// <summary>
            ///     Gets the MQTT correlation data from the message headers.
            /// </summary>
            /// <returns>The correlation data as a byte array if present, otherwise null.</returns>
            /// <remarks>
            ///     Correlation data is used to correlate requests with responses in MQTT request/response patterns.
            ///     The data is stored as Base64 in headers and converted back to bytes by this method.
            /// </remarks>
            public byte[]? GetCorrelationData()
            {
                var asBase64String = actorContext.Headers?.GetValueOrDefault(MqttConstants.CorrelationData);
                return asBase64String != null ? Convert.FromBase64String(asBase64String) : null;
            }
        }
    }
}