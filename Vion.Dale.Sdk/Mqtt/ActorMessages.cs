using System;
using System.Buffers;
using System.Collections.Generic;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Mqtt
{
    /// <summary>
    ///     Message from runtime to handler requesting MQTT message handler registration.
    /// </summary>
    /// <remarks>
    ///     All types implementing <see cref="IMqttHandlerActor" /> must process this message and respond with
    ///     <see cref="RegisterMqttHandlerResponse" />.
    ///     The runtime waits for responses from all <see cref="IMqttHandlerActor" /> instances before proceeding.
    /// </remarks>
    public readonly record struct RegisterMqttHandlerRequest;

    /// <summary>
    ///     Message from handler to runtime confirming MQTT message handler registration.
    /// </summary>
    /// <remarks>
    ///     Must be sent by all <see cref="IMqttHandlerActor" /> implementations in response to
    ///     <see cref="RegisterMqttHandlerResponse" />.
    ///     The runtime waits for responses from all <see cref="IMqttHandlerActor" /> instances before proceeding.
    /// </remarks>
    public readonly record struct RegisterMqttHandlerResponse;

    /// <summary>
    ///     Message from handler to MQTT client to register message handling capabilities.
    /// </summary>
    /// <param name="HandlerName">The class name of the handler actor that will process messages.</param>
    /// <param name="TopicRoutingKey">
    ///     The routing key used to determine which messages this handler can process.
    ///     Any topic that contains this string will be routed to the handler.
    ///     Example: "/sw/property" will match topics like
    ///     "{installationTopic}/dale/{serviceIdentifier}/{propertyIdentifier}/sw/property/set"
    /// </param>
    /// <param name="TopicGroups">Groups of MQTT topics to subscribe to, organized by their prefix configuration</param>
    /// <remarks>
    ///     Registration will be aborted if:
    ///     <list type="bullet">
    ///         <item>Registration occurs after MQTT client initialization</item>
    ///         <item>The handler is already registered</item>
    ///         <item>The routing key conflicts with an existing handler's routing key</item>
    ///         <item>Any topic cannot be routed with the specified routing key</item>
    ///     </list>
    /// </remarks>
    public readonly record struct RegisterMqttHandler(string HandlerName, string TopicRoutingKey, MqttTopicGroup[] TopicGroups);

    /// <summary>
    ///     Registers an MQTT message to be published automatically each time a connection is established.
    /// </summary>
    /// <param name="PublishMqttMessage">The MQTT message to publish on connect</param>
    /// <param name="Recurring">
    ///     If <c>true</c> (default), the message is re-sent on every connect.
    ///     If <c>false</c>, the message is sent once on the next connect and then removed.
    /// </param>
    public readonly record struct RegisterMessageToSendOnConnect(PublishMqttMessage PublishMqttMessage, bool Recurring = true);

    /// <summary>
    ///     Represents a group of MQTT topics that share the same prefix configuration
    /// </summary>
    /// <param name="Topics">
    ///     The MQTT topic patterns to subscribe to (supports MQTT wildcards).
    ///     Example: "/sw/property/set/+/+" subscribes to all set property topics with two wildcard levels
    /// </param>
    /// <param name="TopicPrefix">
    ///     The prefix to prepend to all topics in this group.
    ///     If null (default), the installation topic prefix will be used automatically.
    ///     If an empty string, no prefix will be applied to the topics.
    ///     If a custom value, that value will be used as the prefix.
    /// </param>
    public readonly record struct MqttTopicGroup(string[] Topics, string? TopicPrefix = null);

    /// <summary>
    ///     Message from the MQTT client to handler actors
    /// </summary>
    public readonly record struct MqttMessageReceived(
        string Topic,
        ReadOnlySequence<byte> Payload,
        byte[]? CorrelationData,
        string? ResponseTopic,
        List<MqttUserProperty> UserProperties);

    /// <summary>
    ///     Message from handler actors to the MQTT client
    /// </summary>
    public readonly record struct PublishMqttMessage(
        string Topic,
        byte[]? Payload = null,
        string? ContentType = MessageMimeTypes.FlatBuffer,
        byte[]? CorrelationData = null,
        string? ResponseTopic = null,
        List<MqttUserProperty>? UserProperties = null,
        bool Retain = false,
        int AttemptNumber = 1)
    {
        /// <summary>
        ///     Converts to a request for acknowledgement-based publishing.
        /// </summary>
        public PublishMqttMessageRequest ToRequest()
        {
            return new PublishMqttMessageRequest(Topic,
                                                 Payload,
                                                 ContentType,
                                                 CorrelationData,
                                                 ResponseTopic,
                                                 UserProperties,
                                                 Retain);
        }
    }

    /// <summary>
    ///     Request message for publishing with acknowledgement.
    ///     Use with
    ///     <see
    ///         cref="IActorSystem.SendAndWaitForAcknowledgementAsync{TRequestMessage, TAcknowledgementMessage}(Dictionary{IActorReference, TRequestMessage}, TimeSpan)" />
    ///     to ensure the message is published before continuing.
    /// </summary>
    public readonly record struct PublishMqttMessageRequest(
        string Topic,
        byte[]? Payload = null,
        string? ContentType = MessageMimeTypes.FlatBuffer,
        byte[]? CorrelationData = null,
        string? ResponseTopic = null,
        List<MqttUserProperty>? UserProperties = null,
        bool Retain = false)
    {
        /// <summary>
        ///     Converts to a fire-and-forget publish message.
        /// </summary>
        public PublishMqttMessage ToMessage()
        {
            return new PublishMqttMessage(Topic,
                                          Payload,
                                          ContentType,
                                          CorrelationData,
                                          ResponseTopic,
                                          UserProperties,
                                          Retain);
        }
    }

    /// <summary>
    ///     Response message acknowledging that a PublishMqttMessageRequest was processed.
    /// </summary>
    public readonly record struct PublishMqttMessageResponse(bool Success, string? ErrorMessage = null);

    public readonly record struct RegisterServiceProvider;
}
