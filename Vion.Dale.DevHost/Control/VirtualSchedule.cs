using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     DevHost's opt-in <see cref="IVirtualSchedule" /> — a lock-guarded map of pending delayed-send
    ///     tokens to their virtual due-times. Registered only by DevHost (the same opt-in pattern as
    ///     <see cref="MessageTap" /> / <see cref="DevHostRunControl" /> / <see cref="InFlightActivityMonitor" />),
    ///     so the production runtime, which registers none, is unaffected.
    ///     <para>
    ///         Every <see cref="IActorContext.SendToSelfAfter" /> registers its <c>now + delay</c> due-time and
    ///         unregisters it as the send fires; the two internal ack/stop timeout waits do the same. The
    ///         next-event stepper reads <see cref="NextDue" /> to advance the fake clock to the next scheduled
    ///         event instead of by a caller-supplied fixed interval, so a <c>[Timer(1)]</c> fires the right
    ///         number of times with no drift even when reschedule delays are dynamic.
    ///     </para>
    /// </summary>
    internal sealed class VirtualSchedule : IVirtualSchedule
    {
        private readonly object _gate = new();

        // Reference-identity keyed: tokens are fresh `new object()` per delayed send, so the default
        // equality (reference) is exactly right and two distinct sends never collide. Each entry carries a
        // monotonic sequence (assigned at registration) so the stepper can break same-due-time ties
        // deterministically, and an optional delivery action (stepper-driven sends; null for Task.Delay-backed
        // entries such as the ack/stop timeouts).
        private readonly Dictionary<object, Entry> _pending = new();

        // Monotonic registration counter — the deterministic tie-break for entries due at the same virtual
        // instant. Registration order is itself deterministic (block spawn order + deterministic handler logic).
        private long _nextSequence;

        /// <summary>The number of pending entries — a test hook for schedule-hygiene assertions (no token leaks).</summary>
        public int PendingCount
        {
            get
            {
                lock (_gate)
                {
                    return _pending.Count;
                }
            }
        }

        /// <inheritdoc />
        public void Register(object token, DateTimeOffset dueUtc)
        {
            Add(token, dueUtc, null);
        }

        /// <inheritdoc />
        public void RegisterDelivery(object token, DateTimeOffset dueUtc, Action deliver)
        {
            Add(token, dueUtc, deliver ?? throw new ArgumentNullException(nameof(deliver)));
        }

        /// <inheritdoc />
        public void Unregister(object token)
        {
            if (token is null)
            {
                return;
            }

            lock (_gate)
            {
                _pending.Remove(token);
            }
        }

        /// <inheritdoc />
        public DateTimeOffset? NextDue()
        {
            lock (_gate)
            {
                return _pending.Count == 0 ? null : Min().Value.Due;
            }
        }

        /// <inheritdoc />
        public bool TryTakeNext(out DateTimeOffset dueUtc, out Action? deliver)
        {
            lock (_gate)
            {
                if (_pending.Count == 0)
                {
                    dueUtc = default;
                    deliver = null;
                    return false;
                }

                var min = Min();
                _pending.Remove(min.Key);
                dueUtc = min.Value.Due;
                deliver = min.Value.Deliver;
                return true;
            }
        }

        private void Add(object token, DateTimeOffset dueUtc, Action? deliver)
        {
            if (token is null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            lock (_gate)
            {
                _pending[token] = new Entry(dueUtc, _nextSequence++, deliver);
            }
        }

        // The minimum entry by (Due, Sequence) — the next event to fire. Caller holds _gate.
        private KeyValuePair<object, Entry> Min()
        {
            var min = default(KeyValuePair<object, Entry>);
            var first = true;
            foreach (var entry in _pending)
            {
                if (first || entry.Value.Due < min.Value.Due || (entry.Value.Due == min.Value.Due && entry.Value.Sequence < min.Value.Sequence))
                {
                    min = entry;
                    first = false;
                }
            }

            return min;
        }

        private readonly struct Entry
        {
            public Entry(DateTimeOffset due, long sequence, Action? deliver)
            {
                Due = due;
                Sequence = sequence;
                Deliver = deliver;
            }

            public DateTimeOffset Due { get; }

            public long Sequence { get; }

            public Action? Deliver { get; }
        }
    }
}