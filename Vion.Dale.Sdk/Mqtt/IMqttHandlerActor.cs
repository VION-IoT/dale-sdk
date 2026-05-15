using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Mqtt
{
    /// <summary>
    ///     Marker interface for actors that can handle MQTT messages.
    /// </summary>
    /// <remarks>
    ///     Actors implementing this interface:
    ///     <list type="bullet">
    ///         <item>Are automatically discovered and initialized at application startup (no DI registration needed)</item>
    ///         <item>Must handle <see cref="RegisterMqttHandlerRequest" /> messages</item>
    ///         <item>
    ///             Can register themselves using <see cref="RegisterMqttHandler" /> (use extension methods in
    ///             <see cref="MqttHandlerActorExtensions" /> for easier registration)
    ///         </item>
    ///         <item>Must respond with <see cref="RegisterMqttHandlerResponse" /></item>
    ///         <item>Use <see cref="MqttMessageReceived" /> to receive MQTT messages</item>
    ///         <item>Use <see cref="PublishMqttMessage" /> for fire-and-forget publishing</item>
    ///         <item>
    ///             Use <see cref="PublishMqttMessageRequest" /> and <see cref="PublishMqttMessageResponse" /> to wait for
    ///             confirmation that publishing succeeded or failed
    ///         </item>
    ///     </list>
    /// </remarks>
    public interface IMqttHandlerActor : IActorReceiver;
}
