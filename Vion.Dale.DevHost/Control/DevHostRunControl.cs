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

        private bool _honoursTopologySwitch;

        private bool _paused;

        private bool? _requestedClockMode;

        private string? _requestedTopology;

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

        /// <summary>
        ///     True when the attached supervisor rebuilds from the topology a switch names. A supervisor that
        ///     builds every generation the same way (<c>DevHostWebRunner.RunAsync(Func&lt;IDevHost&gt;, …)</c>)
        ///     can recycle but not re-topologise, and saying so is what keeps a client from being told a switch
        ///     took while the host comes back on the topology it was already on.
        /// </summary>
        public bool CanSwitchTopology
        {
            get
            {
                lock (_gate)
                {
                    return _resetHandler is not null && _honoursTopologySwitch;
                }
            }
        }

        /// <summary>
        ///     The clock mode (true = deterministic stepping, false = real wall-clock) the latest clock-mode
        ///     switch requested, for the supervisor to read when the reset fires — null means keep the current
        ///     mode.
        /// </summary>
        public bool? RequestedClockMode
        {
            get
            {
                lock (_gate)
                {
                    return _requestedClockMode;
                }
            }
        }

        /// <summary>
        ///     The topology id the latest switch requested, for the supervisor to read when the reset
        ///     fires — null means recycle into the same topology.
        /// </summary>
        public string? RequestedTopology
        {
            get
            {
                lock (_gate)
                {
                    return _requestedTopology;
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
        ///     Attach the supervisor's reset handler. Returns a token that detaches that handler — the
        ///     supervisor re-attaches per host generation, and each generation's run control is its own.
        ///     <para>
        ///         One handler at a time: a second attach is refused rather than silently replacing the first.
        ///         Replacing it made the host answer a recycle request with success while nothing recycled, and
        ///         composing two would invent semantics for which supervisor owns the rebuild.
        ///     </para>
        /// </summary>
        /// <param name="handler">Invoked when a recycle is requested.</param>
        /// <param name="honoursTopologySwitch">
        ///     True when this supervisor rebuilds from <see cref="RequestedTopology" />. Left false, the host
        ///     reports itself unable to switch and refuses a switch request rather than answering it with a
        ///     recycle onto the same topology.
        /// </param>
        /// <exception cref="InvalidOperationException">A reset handler is already attached.</exception>
        public IDisposable OnResetRequested(Action handler, bool honoursTopologySwitch = false)
        {
            if (handler is null)
            {
                throw new ArgumentNullException(nameof(handler));
            }

            lock (_gate)
            {
                if (_resetHandler is not null)
                {
                    throw new
                        InvalidOperationException("A reset handler is already attached to this host generation. One supervisor owns the rebuild; dispose the first subscription before attaching another.");
                }

                _resetHandler = handler;
                _honoursTopologySwitch = honoursTopologySwitch;
            }

            return new DetachToken(this, handler);
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

        /// <summary>
        ///     Request a recycle into a different clock mode — rides the same reset signal; the supervisor
        ///     reads <see cref="RequestedClockMode" /> and rebuilds the next generation stepped or real.
        ///     Returns false when no supervisor is attached. A supervisor that always builds the same graph
        ///     still honours this: the recycle loop reads the requested mode whichever factory shape it was
        ///     given, so the mode turns on the supervisor existing and not on it being topology-aware.
        /// </summary>
        public bool TryRequestClockMode(bool stepped)
        {
            lock (_gate)
            {
                if (_resetHandler is null)
                {
                    return false;
                }

                _requestedClockMode = stepped;
            }

            return TryRequestReset();
        }

        /// <summary>
        ///     Request a recycle into a different topology — rides the same reset signal;
        ///     a topology-aware supervisor (<c>DevHostWebRunner.RunAsync(Func&lt;string?, IDevHost&gt;, …)</c>)
        ///     reads <see cref="RequestedTopology" /> and builds the next generation from it. Returns false
        ///     when no supervisor is attached, and when the attached one does not honour a topology switch —
        ///     recycling it onto the topology it is already on would report a switch that never happened.
        /// </summary>
        public bool TryRequestTopologySwitch(string topologyId)
        {
            lock (_gate)
            {
                if (_resetHandler is null || !_honoursTopologySwitch)
                {
                    return false;
                }

                _requestedTopology = topologyId;
            }

            return TryRequestReset();
        }

        private sealed class DetachToken : IDisposable
        {
            private readonly Action _handler;

            private readonly DevHostRunControl _owner;

            public DetachToken(DevHostRunControl owner, Action handler)
            {
                _owner = owner;
                _handler = handler;
            }

            /// <summary>
            ///     Detaches the handler THIS token was issued for, and nothing else. Clearing whatever handler
            ///     was current let a stale token silently unsupervise a host a later subscriber owns — the host
            ///     then reports itself unresettable with no supervisor change to explain it.
            /// </summary>
            public void Dispose()
            {
                lock (_owner._gate)
                {
                    if (ReferenceEquals(_owner._resetHandler, _handler))
                    {
                        _owner._resetHandler = null;
                        _owner._honoursTopologySwitch = false;
                    }
                }
            }
        }
    }
}