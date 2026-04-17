using System;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.Sdk.Mqtt
{
    public static partial class MessageLogging
    {
        [LoggerMessage(Level = LogLevel.Debug, Message = "Handling MQTT message (CorrelationId={CorrelationId}, Topic={Topic})")]
        public static partial void LogHandlingMqttMessage(this ILogger logger, Guid correlationId, string topic);

        [LoggerMessage(Level = LogLevel.Warning, Message = "Unhandled MQTT topic (CorrelationId={CorrelationId}, Topic={Topic})")]
        public static partial void LogUnhandledMqttTopic(this ILogger logger, Guid correlationId, string topic);
    }
}