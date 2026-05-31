namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Optional observer of messages received by actors. When an implementation is registered in the
    ///     actor system's service provider, the actor middleware notifies it for every received message;
    ///     when none is registered (the default, including the production runtime), the middleware does
    ///     nothing extra. Used by DevHost's headless control surface to tap inter-block traffic for tests
    ///     and agents (RFC 0003) without coupling the runtime to that feature.
    /// </summary>
    public interface IActorMessageObserver
    {
        /// <summary>Invoked when an actor receives a message, before the message is dispatched.</summary>
        /// <param name="actorName">The receiving actor's identifier (its registered PID id).</param>
        /// <param name="message">The received message.</param>
        void OnReceived(string actorName, object message);
    }
}
