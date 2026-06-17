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
        // equality (reference) is exactly right and two distinct sends never collide.
        private readonly Dictionary<object, DateTimeOffset> _pending = new();

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
            if (token is null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            lock (_gate)
            {
                _pending[token] = dueUtc;
            }
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
                if (_pending.Count == 0)
                {
                    return null;
                }

                var min = DateTimeOffset.MaxValue;
                foreach (var due in _pending.Values)
                {
                    if (due < min)
                    {
                        min = due;
                    }
                }

                return min;
            }
        }
    }
}