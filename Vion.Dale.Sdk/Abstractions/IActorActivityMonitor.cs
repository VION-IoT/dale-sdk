namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Optional, opt-in monitor of in-flight actor handler executions. When an implementation is
    ///     registered in the actor system's service provider, the actor middleware brackets every message
    ///     handler with <see cref="EnterHandler" /> / <see cref="ExitHandler" />; when none is registered
    ///     (the default, including the production runtime), the middleware does nothing extra. Used by
    ///     DevHost's deterministic-stepping barrier (RFC 0003) to know whether ANY user handler is currently
    ///     executing — the exact complement to the mailbox-depth signal.
    ///     <para>
    ///         The contract that makes the barrier exact: <see cref="EnterHandler" /> is called BEFORE the
    ///         handler body runs and <see cref="ExitHandler" /> AFTER it returns (in a <c>finally</c>). A
    ///         handler that posts a follow-up message to another actor therefore does so while its in-flight
    ///         count is already &gt; 0 — so <c>Σ MailboxDepth == 0 AND InFlight == 0</c> can never be observed
    ///         while a cascade is still live (the dequeued-but-not-yet-posted window the depth signal alone
    ///         cannot see). This is the same opt-in pattern as <see cref="IActorMessageObserver" /> (RFC 0003's
    ///         message tap) and <see cref="IDelayedSendGate" /> (the pause gate).
    ///     </para>
    /// </summary>
    public interface IActorActivityMonitor
    {
        /// <summary>The number of actor handlers currently executing (entered but not yet exited).</summary>
        long InFlight { get; }

        /// <summary>Invoked immediately before an actor's message handler runs.</summary>
        void EnterHandler();

        /// <summary>Invoked immediately after an actor's message handler returns or throws (in a <c>finally</c>).</summary>
        void ExitHandler();
    }
}