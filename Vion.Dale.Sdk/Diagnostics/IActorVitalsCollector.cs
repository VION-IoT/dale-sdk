namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     The write surface of the vitals core used by the actor system at spawn time: registering an
    ///     actor's identity and feeding its mailbox-depth counters. Separate from the read surface
    ///     (IRuntimeDiagnostics, injected by blocks) and the per-message
    ///     <see cref="Vion.Dale.Sdk.Abstractions.IActorMessageObserver" />.
    /// </summary>
    public interface IActorVitalsCollector
    {
        /// <summary>Records an actor's identity (category + dimensions), resolved at spawn time.</summary>
        void Register(string actorName, ActorIdentity identity);

        /// <summary>Records a message being posted to an actor's mailbox.</summary>
        void OnMessagePosted(string actorName);

        /// <summary>Records a message being taken off an actor's mailbox for handling.</summary>
        void OnMessageReceived(string actorName);
    }
}
