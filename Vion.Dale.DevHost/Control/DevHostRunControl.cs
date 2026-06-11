using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     The DevHost's run-control state: the pause gate for delayed self-sends and the reset signal a
    ///     supervisor (<c>DevHostWebRunner.RunAsync(hostFactory, …)</c>) subscribes to. Registered as the
    ///     host's <see cref="IDelayedSendGate" />, making pause purely opt-in dev tooling — production hosts
    ///     never register a gate.
    ///     <para>
    ///         <b>Pause semantics (deliberate, documented):</b> pausing holds NEW timer ticks and
    ///         <c>InvokeSynchronizedAfter</c> callbacks in a queue; already-scheduled fires still deliver, so
    ///         each timer may tick at most once more after <see cref="Pause" />. Message processing (property
    ///         sets, contract messages) continues — the world stands still but remains pokeable. Wall-clock
    ///         keeps running: blocks computing from the current time will observe the gap. Resume replays the
    ///         held schedules with their original delays, so self-rescheduling chains survive.
    ///     </para>
    /// </summary>
    public sealed class DevHostRunControl : IDelayedSendGate
    {
        private readonly object _gate = new();

        private readonly List<Action> _held = new();

        private bool _paused;

        private Action? _resetHandler;

        /// <summary>True while delayed self-sends are being held.</summary>
        public bool IsPaused
        {
            get
            {
                lock (_gate)
                {
                    return _paused;
                }
            }
        }

        /// <summary>True when a supervisor capable of recycling the host has attached a reset handler.</summary>
        public bool CanReset
        {
            get
            {
                lock (_gate)
                {
                    return _resetHandler is not null;
                }
            }
        }

        /// <inheritdoc />
        public bool TryHold(Action scheduleNow)
        {
            lock (_gate)
            {
                if (!_paused)
                {
                    return false;
                }

                _held.Add(scheduleNow);
                return true;
            }
        }

        /// <summary>Hold all new delayed self-sends from now on.</summary>
        public void Pause()
        {
            lock (_gate)
            {
                _paused = true;
            }
        }

        /// <summary>Stop holding and replay everything held, in order, with the original delays.</summary>
        public void Resume()
        {
            Action[] drained;
            lock (_gate)
            {
                _paused = false;
                drained = _held.ToArray();
                _held.Clear();
            }

            foreach (var scheduleNow in drained)
            {
                scheduleNow();
            }
        }

        /// <summary>
        ///     Attach the supervisor's reset handler. Returns a token that detaches it — the supervisor
        ///     re-attaches per host generation.
        /// </summary>
        public IDisposable OnResetRequested(Action handler)
        {
            lock (_gate)
            {
                _resetHandler = handler;
            }

            return new DetachToken(this);
        }

        /// <summary>
        ///     Request a host recycle. Returns false when no supervisor is attached (the host was run
        ///     without a factory, so nothing can rebuild it).
        /// </summary>
        public bool TryRequestReset()
        {
            Action? handler;
            lock (_gate)
            {
                handler = _resetHandler;
            }

            if (handler is null)
            {
                return false;
            }

            handler();
            return true;
        }

        private sealed class DetachToken : IDisposable
        {
            private readonly DevHostRunControl _owner;

            public DetachToken(DevHostRunControl owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                lock (_owner._gate)
                {
                    _owner._resetHandler = null;
                }
            }
        }
    }
}