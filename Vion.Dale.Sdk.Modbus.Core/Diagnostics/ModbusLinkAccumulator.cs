using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Core.Diagnostics
{
    /// <summary>
    ///     Accumulates the receipts of one client's transactions into a <see cref="ModbusLinkSummary" />.
    ///     One instance per client; the client owns it and hands it to the parts that complete transactions.
    /// </summary>
    /// <remarks>
    ///     Writer and reader are different threads on Modbus TCP — a request completes on the queue consumer while the
    ///     block reads the summary on its actor thread — so every field is guarded by one lock and
    ///     <see cref="Snapshot" /> copies out under it. The lock is held for a handful of field writes and is never
    ///     held across a callback.
    /// </remarks>
    [InternalApi]
    public sealed class ModbusLinkAccumulator
    {
        private readonly object _gate = new();

        private long _backedOffCount;

        private long _deviceErrorCount;

        private long _droppedCount;

        private long _expiredCount;

        private DateTime? _lastContactAt;

        private DateTime? _lastFailureAt;

        private ModbusOutcome? _lastFailureOutcome;

        private TimeSpan? _lastQueuedWait;

        private TimeSpan? _lastRoundTrip;

        private TimeSpan? _maxQueuedWait;

        private TimeSpan? _maxRoundTrip;

        private TimeSpan? _minRoundTrip;

        private long _protocolErrorCount;

        private ModbusLinkState _state;

        private long _successCount;

        private long _timeoutCount;

        private long _transportErrorCount;

        /// <summary>Records one completed transaction. Called at the point the receipt is stamped.</summary>
        public void Record(ModbusReceipt receipt)
        {
            lock (_gate)
            {
                switch (receipt.Outcome)
                {
                    case ModbusOutcome.Success:
                        _successCount++;
                        _state = ModbusLinkState.Online;
                        _lastContactAt = receipt.ReceivedAt;
                        break;
                    case ModbusOutcome.DeviceError:
                        _deviceErrorCount++;
                        _state = ModbusLinkState.Online;
                        _lastContactAt = receipt.ReceivedAt;
                        break;
                    case ModbusOutcome.Timeout:
                        _timeoutCount++;
                        _state = ModbusLinkState.Faulted;
                        break;
                    case ModbusOutcome.TransportError:
                        _transportErrorCount++;
                        _state = ModbusLinkState.Faulted;
                        break;
                    case ModbusOutcome.ProtocolError:
                        _protocolErrorCount++;
                        _state = ModbusLinkState.Faulted;
                        break;
                    case ModbusOutcome.BackedOff:
                        _backedOffCount++;
                        break;
                    case ModbusOutcome.Expired:
                        _expiredCount++;
                        break;
                    case ModbusOutcome.Dropped:
                        _droppedCount++;
                        break;
                }

                if (receipt.Outcome != ModbusOutcome.Success)
                {
                    _lastFailureAt = receipt.ReceivedAt;
                    _lastFailureOutcome = receipt.Outcome;
                }

                // Only a transaction that reached the wire has a meaningful round trip; a locally decided one
                // carries TimeSpan.Zero and would drag MinRoundTrip to zero for the rest of the client's life.
                if (ReachedTheWire(receipt.Outcome))
                {
                    _lastRoundTrip = receipt.RoundTrip;
                    _minRoundTrip = _minRoundTrip is { } min && min <= receipt.RoundTrip ? min : receipt.RoundTrip;
                    _maxRoundTrip = _maxRoundTrip is { } max && max >= receipt.RoundTrip ? max : receipt.RoundTrip;
                }

                // Every outcome but Invalid describes a request that was queued, so its wait is real even when it
                // never reached the wire. An Invalid one was refused before it was queued and carries a zero wait,
                // which would clear the gauge a block reads to see congestion.
                if (receipt.Outcome != ModbusOutcome.Invalid)
                {
                    _lastQueuedWait = receipt.QueuedWait;
                    _maxQueuedWait = _maxQueuedWait is { } maxWait && maxWait >= receipt.QueuedWait ? maxWait : receipt.QueuedWait;
                }
            }
        }

        /// <summary>A consistent copy of everything recorded so far, with the caller's current queue depth folded in.</summary>
        public ModbusLinkSummary Snapshot(int queueDepth)
        {
            lock (_gate)
            {
                return new ModbusLinkSummary(_state,
                                             _lastContactAt,
                                             _lastFailureAt,
                                             _lastFailureOutcome,
                                             _successCount,
                                             _deviceErrorCount,
                                             _timeoutCount,
                                             _transportErrorCount,
                                             _protocolErrorCount,
                                             _backedOffCount,
                                             _expiredCount,
                                             _droppedCount,
                                             _lastRoundTrip,
                                             _minRoundTrip,
                                             _maxRoundTrip,
                                             _lastQueuedWait,
                                             _maxQueuedWait,
                                             queueDepth);
            }
        }

        private static bool ReachedTheWire(ModbusOutcome outcome)
        {
            return outcome is ModbusOutcome.Success or ModbusOutcome.DeviceError or ModbusOutcome.Timeout or ModbusOutcome.TransportError or ModbusOutcome.ProtocolError;
        }
    }
}