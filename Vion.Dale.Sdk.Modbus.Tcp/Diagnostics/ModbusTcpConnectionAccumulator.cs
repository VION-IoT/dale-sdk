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

        private bool _isConnected;

        private TimeSpan? _lastConnectDuration;

        private DateTime? _lastConnectFailureAt;

        private DateTime? _lastConnectedAt;

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

        /// <summary>A consistent copy of everything recorded so far.</summary>
        public ModbusTcpConnectionSummary Snapshot()
        {
            lock (_gate)
            {
                return new ModbusTcpConnectionSummary(_isConnected ? ModbusTcpConnectionState.Connected : ModbusTcpConnectionState.Disconnected,
                                                      _lastConnectedAt,
                                                      _lastConnectFailureAt,
                                                      _connectAttemptCount,
                                                      _connectFailureCount,
                                                      _consecutiveConnectFailures,
                                                      _lastConnectDuration,
                                                      null,
                                                      null);
            }
        }
    }
}