using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     DevHost's opt-in <see cref="IActorActivityMonitor" /> and <see cref="IExchangeActivityMonitor" /> — the live
    ///     counts the <see cref="QuiescenceBarrier" /> reads beside mailbox depth. Registered only by DevHost (the same
    ///     opt-in pattern as <see cref="MessageTap" /> / <see cref="DevHostRunControl" />), so the production runtime,
    ///     which registers none, is unaffected.
    ///     <para>
    ///         The actor middleware brackets every handler with <see cref="EnterHandler" /> before and
    ///         <see cref="ExitHandler" /> after (in a <c>finally</c>). Because the enter happens before the handler body —
    ///         and therefore before the handler can post a follow-up to the next hop — the barrier can read
    ///         <c>Σ MailboxDepth == 0 AND InFlight == 0</c> as an EXACT quiescence predicate: it cannot be satisfied while
    ///         any cascade is still live, closing the dequeued-but-not-yet-posted window the mailbox-depth signal alone
    ///         cannot see.
    ///     </para>
    ///     <para>
    ///         An open exchange adds to <see cref="Busy" />, the same count a handler adds to, rather than to a third
    ///         counter: reading three counters one after another is not one observation — an exchange could close, its
    ///         callback run and open the next exchange between two of the reads. An exchange posts its callback, which
    ///         raises the count, before it closes, so <see cref="Busy" /> never touches zero while a round trip is still
    ///         on its way back. <see cref="InFlight" /> stays handlers only, for the teardown drain that must not wait on
    ///         sockets.
    ///     </para>
    /// </summary>
    internal sealed class InFlightActivityMonitor : IActorActivityMonitor, IExchangeActivityMonitor
    {
        private readonly ConcurrentDictionary<long, string> _openExchanges = new();

        private long _busy;

        private long _inFlight;

        private long _nextExchangeId;

        private TaskCompletionSource<bool>? _zeroReached;

        /// <summary>Handlers executing plus exchanges open — what the stepper's barrier reads.</summary>
        public long Busy
        {
            get => Interlocked.Read(ref _busy);
        }

        /// <summary>What every open exchange was opened as, for a failure to name.</summary>
        public IReadOnlyList<string> OpenExchanges
        {
            get => _openExchanges.OrderBy(entry => entry.Key).Select(entry => entry.Value).ToList();
        }

        /// <inheritdoc />
        public long InFlight
        {
            get => Interlocked.Read(ref _inFlight);
        }

        /// <inheritdoc />
        public void EnterHandler()
        {
            Interlocked.Increment(ref _busy);
            Interlocked.Increment(ref _inFlight);
        }

        /// <inheritdoc />
        public void ExitHandler()
        {
            var inFlight = Interlocked.Decrement(ref _inFlight);
            var busy = Interlocked.Decrement(ref _busy);
            if (inFlight == 0 || busy == 0)
            {
                SignalZero();
            }
        }

        /// <inheritdoc />
        public IDisposable OpenExchange(string description)
        {
            var id = Interlocked.Increment(ref _nextExchangeId);
            _openExchanges[id] = description;
            Interlocked.Increment(ref _busy);

            return new Exchange(this, id);
        }

        /// <summary>
        ///     A task that completes the next time either count reaches zero. Taken before the predicate is evaluated, so a
        ///     count reaching zero between the evaluation and the await completes it instead of passing unseen.
        /// </summary>
        public Task WhenACountReachesZero()
        {
            while (true)
            {
                var current = Volatile.Read(ref _zeroReached);
                if (current != null)
                {
                    return current.Task;
                }

                var armed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
                if (Interlocked.CompareExchange(ref _zeroReached, armed, null) == null)
                {
                    return armed.Task;
                }
            }
        }

        private void CloseExchange(long id)
        {
            _openExchanges.TryRemove(id, out _);
            if (Interlocked.Decrement(ref _busy) == 0)
            {
                SignalZero();
            }
        }

        private void SignalZero()
        {
            Interlocked.Exchange(ref _zeroReached, null)?.TrySetResult(true);
        }

        private sealed class Exchange : IDisposable
        {
            private readonly long _id;

            private readonly InFlightActivityMonitor _monitor;

            private int _closed;

            public Exchange(InFlightActivityMonitor monitor, long id)
            {
                _monitor = monitor;
                _id = id;
            }

            public void Dispose()
            {
                if (Interlocked.Exchange(ref _closed, 1) == 0)
                {
                    _monitor.CloseExchange(_id);
                }
            }
        }
    }
}
