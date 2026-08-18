using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Diagnostics
{
    /// <summary>
    ///     Accumulates one Modbus TCP client's connect and disconnect events into a
    ///     <see cref="ModbusTcpConnectionSummary" />. One instance per client; the client owns it and hands it to its
    ///     wrapper.
    /// </summary>
    /// <remarks>
    ///     Written on the queue consumer (connections are established inside operations) and read on the block's actor
    ///     thread, so every field is guarded by one lock and <see cref="Snapshot" /> copies out under it.
    /// </remarks>
    [InternalApi]
    public sealed class ModbusTcpConnectionAccumulator
    {
        private readonly object _gate = new();

        private long _connectAttemptCount;

        private long _connectFailureCount;

        private int _consecutiveConnectFailures;

        private TimeSpan? _currentBackoff;

        private bool _isConnected;

        private TimeSpan? _lastConnectDuration;

        private DateTime? _lastConnectFailureAt;

        private DateTime? _lastConnectedAt;

        private DateTime? _nextAttemptAt;

        private TimeProvider _timeProvider = TimeProvider.System;

        /// <summary>Failed connection attempts since the last successful one or the last configuration change.</summary>
        public int ConsecutiveConnectFailures
        {
            get
            {
                lock (_gate)
                {
                    return _consecutiveConnectFailures;
                }
            }
        }

        /// <summary>
        ///     Supplies the clock <see cref="Snapshot" /> compares against to tell a backoff that is still running from
        ///     one that has elapsed.
        /// </summary>
        /// <param name="timeProvider">The owning client's clock, as held by its wrapper.</param>
        /// <remarks>
        ///     The client constructs this accumulator before the container has given anything a clock, so the wrapper
        ///     hands its own over at the same seam it receives the accumulator. Until then the system clock is used.
        /// </remarks>
        public void UseClock(TimeProvider timeProvider)
        {
            lock (_gate)
            {
                _timeProvider = timeProvider;
            }
        }

        /// <summary>Records that a connection attempt is about to be made.</summary>
        public void RecordConnectAttempt()
        {
            lock (_gate)
            {
                _connectAttemptCount++;
            }
        }

        /// <summary>Records a completed handshake.</summary>
        /// <param name="connectedAt">The UTC instant the socket came up.</param>
        /// <param name="duration">How long the handshake took.</param>
        public void RecordConnected(DateTime connectedAt, TimeSpan duration)
        {
            lock (_gate)
            {
                _isConnected = true;
                _lastConnectedAt = connectedAt;
                _lastConnectDuration = duration;
                _consecutiveConnectFailures = 0;
                _currentBackoff = null;
                _nextAttemptAt = null;
            }
        }

        /// <summary>Records a connection attempt that did not produce a socket.</summary>
        /// <param name="failedAt">The UTC instant the attempt failed.</param>
        public void RecordConnectFailed(DateTime failedAt)
        {
            lock (_gate)
            {
                _isConnected = false;
                _connectFailureCount++;
                _consecutiveConnectFailures++;
                _lastConnectFailureAt = failedAt;
            }
        }

        /// <summary>Records that the socket was closed.</summary>
        public void RecordDisconnected()
        {
            lock (_gate)
            {
                _isConnected = false;
            }
        }

        /// <summary>Records the backoff the client has just armed between connection attempts.</summary>
        /// <param name="backoff">How long the client waits before it tries again.</param>
        /// <param name="nextAttemptAt">The UTC instant the next attempt becomes due.</param>
        public void RecordConnectBackoff(TimeSpan backoff, DateTime nextAttemptAt)
        {
            lock (_gate)
            {
                _currentBackoff = backoff;
                _nextAttemptAt = nextAttemptAt;
            }
        }

        /// <summary>
        ///     Clears the armed backoff and the consecutive-failure run, so the next attempt is made without waiting.
        ///     Called when a configuration change supersedes the failures that armed it.
        /// </summary>
        public void ResetConnectBackoff()
        {
            lock (_gate)
            {
                _consecutiveConnectFailures = 0;
                _currentBackoff = null;
                _nextAttemptAt = null;
            }
        }

        /// <summary>A consistent copy of everything recorded so far.</summary>
        public ModbusTcpConnectionSummary Snapshot()
        {
            lock (_gate)
            {
                // An armed backoff whose instant has passed is not reported as BackingOff: the socket really is
                // closed and the next request will attempt a connection. The two fields stay filled until that
                // attempt resolves, so a reader can still see what the client is waiting on.
                var isBackingOff = _nextAttemptAt is { } nextAttemptAt && _timeProvider.GetUtcNow().UtcDateTime < nextAttemptAt;

                return new ModbusTcpConnectionSummary(ConnectionState(isBackingOff),
                                                      _lastConnectedAt,
                                                      _lastConnectFailureAt,
                                                      _connectAttemptCount,
                                                      _connectFailureCount,
                                                      _consecutiveConnectFailures,
                                                      _lastConnectDuration,
                                                      _currentBackoff,
                                                      _nextAttemptAt);
            }
        }

        private ModbusTcpConnectionState ConnectionState(bool isBackingOff)
        {
            if (isBackingOff)
            {
                return ModbusTcpConnectionState.BackingOff;
            }

            return _isConnected ? ModbusTcpConnectionState.Connected : ModbusTcpConnectionState.Disconnected;
        }
    }
}