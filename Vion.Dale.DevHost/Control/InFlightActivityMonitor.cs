using System.Threading;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     DevHost's opt-in <see cref="IActorActivityMonitor" /> — a single interlocked counter of user
    ///     handlers currently executing across the whole actor system. Registered only by DevHost (the same
    ///     opt-in pattern as <see cref="MessageTap" /> / <see cref="DevHostRunControl" />), so the production
    ///     runtime, which registers none, is unaffected.
    ///     <para>
    ///         The actor middleware brackets every handler with <see cref="EnterHandler" /> before and
    ///         <see cref="ExitHandler" /> after (in a <c>finally</c>). Because the enter happens before the
    ///         handler body — and therefore before the handler can post a follow-up to the next hop — the
    ///         <see cref="QuiescenceBarrier" /> can read <c>Σ MailboxDepth == 0 AND InFlight == 0</c> as an
    ///         EXACT quiescence predicate: it cannot be satisfied while any cascade is still live, closing the
    ///         dequeued-but-not-yet-posted window the mailbox-depth signal alone cannot see.
    ///     </para>
    /// </summary>
    internal sealed class InFlightActivityMonitor : IActorActivityMonitor
    {
        private long _inFlight;

        /// <inheritdoc />
        public long InFlight
        {
            get => Interlocked.Read(ref _inFlight);
        }

        /// <inheritdoc />
        public void EnterHandler()
        {
            Interlocked.Increment(ref _inFlight);
        }

        /// <inheritdoc />
        public void ExitHandler()
        {
            Interlocked.Decrement(ref _inFlight);
        }
    }
}