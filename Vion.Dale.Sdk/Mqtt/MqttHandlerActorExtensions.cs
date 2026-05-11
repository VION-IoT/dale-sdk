using System.Linq;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Mqtt
{
    /// <summary>
    ///     Extension methods for <see cref="IMqttHandlerActor" />.
    /// </summary>
    public static class MqttHandlerActorExtensions
    {
        extension(IMqttHandlerActor handlerActor)
        {
            /// <summary>
            ///     Registers the MQTT handler with the MQTT client for the specified topics.
            ///     The handler name is automatically derived from the actor's class name.
            /// </summary>
            /// <param name="routingKey">The routing key used to identify and route messages to this handler.</param>
            /// <param name="topics">Array of MQTT topics to subscribe to.</param>
            /// <param name="actorContext">
            ///     The actor context used to send the registration message to the MQTT client and respond to
            ///     the sender.
            /// </param>
            /// <param name="logger">Logger to log registration information.</param>
            /// <remarks>
            ///     This is a convenience method that automatically applies the installation topic prefix to all topics.
            ///     For different prefix behavior (no prefix or custom prefix), the overload with <see cref="MqttTopicGroup" /> array
            ///     parameter
            ///     should be used instead.
            /// </remarks>
            public void RegisterWithMqttClient(string routingKey, string[] topics, IActorContext actorContext, ILogger logger)
            {
                MqttTopicGroup[] topicGroups = [new(topics)];
                handlerActor.RegisterWithMqttClient(routingKey, topicGroups, actorContext, logger);
            }

            // ReSharper disable once MemberCanBePrivate.Global
            /// <summary>
            ///     Registers the MQTT handler with the MQTT client for the specified topic groups.
            ///     The handler name is automatically derived from the actor's class name.
            /// </summary>
            /// <param name="routingKey">The routing key used to identify and route messages to this handler.</param>
            /// <param name="topicGroups">
            ///     Array of MQTT topic groups to subscribe to, where each group can have different topic prefix
            ///     settings.
            /// </param>
            /// <param name="actorContext">
            ///     The actor context used to send the registration message to the MQTT client and respond to
            ///     the sender.
            /// </param>
            /// <param name="logger">Logger to log registration information.</param>
            public void RegisterWithMqttClient(string routingKey, MqttTopicGroup[] topicGroups, IActorContext actorContext, ILogger logger)
            {
                var handlerName = handlerActor.GetType().Name;
                logger.LogInformation("Registering MQTT handler {HandlerName} with routing key {RoutingKey}", handlerName, routingKey);
                if (logger.IsEnabled(LogLevel.Information))
                {
                    logger.LogInformation("Registering topics for subscription: {Topics}", string.Join(", ", topicGroups.SelectMany(topicGroup => topicGroup.Topics)));
                }

                var registerMqttHandler = new RegisterMqttHandler(handlerName, routingKey, topicGroups);
                actorContext.SendTo(actorContext.LookupByName(MqttConstants.MqttClientName), registerMqttHandler);
                actorContext.RespondToSender(new RegisterMqttHandlerResponse());
            }
        }
    }
}
