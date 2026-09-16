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
    ///     <see cref="Snapshot" /> copies out under it. The lock is held for a handful of field writes, or for one pass
    ///     over the window's slots, and is never held across a callback.
    /// </remarks>
    [InternalApi]
    public sealed class ModbusLinkAccumulator
    {
        // The current, partial slot plus fifteen whole ones: a read covers at least the last fifteen minutes and less
        // than sixteen.
        private const int WindowSlotCount = 16;

        private static readonly TimeSpan WindowSlotLength = TimeSpan.FromMinutes(1);

        private readonly TimeProvider _clock;

        private readonly long _createdAt;

        private readonly object _gate = new();

        // Allocated once here so that recording a transaction never allocates.
        private readonly WindowSlot[] _slots = new WindowSlot[WindowSlotCount];

        private long _backedOffCount;

        private long _deviceErrorCount;

        private long _droppedCount;

        private long _expiredCount;

        private DateTime? _lastContactAt;

        private DateTime? _lastFailureAt;

        private ModbusOutcome? _lastFailureOutcome;

        private TimeSpan? _maxQueuedWait;

        private DateTime? _maxQueuedWaitAt;

        private TimeSpan? _maxRoundTrip;

        private DateTime? _maxRoundTripAt;

        private long _protocolErrorCount;

        private ModbusLinkState _state;

        private long _successCount;

        private long _timeoutCount;

        private long _transportErrorCount;

        /// <summary>Creates an accumulator whose window runs on the owning client's clock.</summary>
        /// <param name="clock">The clock the client stamps its receipts with.</param>
        public ModbusLinkAccumulator(TimeProvider clock)
        {
            _clock = clock;
            _createdAt = clock.GetTimestamp();
        }

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

                // The clock is read inside the lock so that slot ids reach the ring in order: two writers reading it
                // outside could land a later id first, and the earlier one would then reset a slot still in use.
                ref var slot = ref CurrentSlot();

                // Only a transaction that reached the wire has a meaningful round trip; a locally decided one carries
                // TimeSpan.Zero and would pull the mean down with requests the device never saw.
                if (ReachedTheWire(receipt.Outcome))
                {
                    slot.RoundTrip.Add(receipt.RoundTrip);
                    if (_maxRoundTrip is not { } max || receipt.RoundTrip > max)
                    {
                        _maxRoundTrip = receipt.RoundTrip;
                        _maxRoundTripAt = receipt.ReceivedAt;
                    }
                }

                // Every outcome but Invalid describes a request that was queued, so its wait is real even when it
                // never reached the wire. An Invalid one was refused before it was queued and carries a zero wait,
                // which would pull the mean down with a wait that never happened.
                if (receipt.Outcome != ModbusOutcome.Invalid)
                {
                    slot.QueuedWait.Add(receipt.QueuedWait);
                    if (_maxQueuedWait is not { } maxWait || receipt.QueuedWait > maxWait)
                    {
                        _maxQueuedWait = receipt.QueuedWait;
                        _maxQueuedWaitAt = receipt.ReceivedAt;
                    }
                }
            }
        }

        /// <summary>A consistent copy of everything recorded so far, with the caller's current queue depth folded in.</summary>
        public ModbusLinkSummary Snapshot(int queueDepth)
        {
            lock (_gate)
            {
                var currentSlotId = CurrentSlotId();
                var roundTrip = default(LatencyTotals);
                var queuedWait = default(LatencyTotals);
                foreach (var slot in _slots)
                {
                    // A slot left behind by an idle period keeps its old id, which is what drops it out of the window.
                    if (slot.Id > currentSlotId - WindowSlotCount)
                    {
                        roundTrip.Merge(slot.RoundTrip);
                        queuedWait.Merge(slot.QueuedWait);
                    }
                }

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
                                             roundTrip.Count,
                                             roundTrip.Mean,
                                             roundTrip.Max,
                                             _maxRoundTrip,
                                             _maxRoundTripAt,
                                             queuedWait.Count,
                                             queuedWait.Mean,
                                             queuedWait.Max,
                                             _maxQueuedWait,
                                             _maxQueuedWaitAt,
                                             queueDepth);
            }
        }

        private static bool ReachedTheWire(ModbusOutcome outcome)
        {
            return outcome is ModbusOutcome.Success or ModbusOutcome.DeviceError or ModbusOutcome.Timeout or ModbusOutcome.TransportError or ModbusOutcome.ProtocolError;
        }

        private ref WindowSlot CurrentSlot()
        {
            var slotId = CurrentSlotId();
            ref var slot = ref _slots[slotId % WindowSlotCount];
            if (slot.Id != slotId)
            {
                slot = new WindowSlot { Id = slotId };
            }

            return ref slot;
        }

        // Elapsed time rather than raw timestamps: a timestamp counts in the clock's own TimestampFrequency, which is
        // not TimeSpan ticks on every platform.
        private long CurrentSlotId()
        {
            return _clock.GetElapsedTime(_createdAt).Ticks / WindowSlotLength.Ticks;
        }

        private struct WindowSlot
        {
            public long Id;

            public LatencyTotals RoundTrip;

            public LatencyTotals QueuedWait;
        }

        private struct LatencyTotals
        {
            public long Count;

            private long _sumTicks;

            private long _maxTicks;

            public TimeSpan? Mean
            {
                get => Count == 0 ? null : TimeSpan.FromTicks(_sumTicks / Count);
            }

            public TimeSpan? Max
            {
                get => Count == 0 ? null : TimeSpan.FromTicks(_maxTicks);
            }

            public void Add(TimeSpan value)
            {
                Count++;
                _sumTicks += value.Ticks;
                _maxTicks = Math.Max(_maxTicks, value.Ticks);
            }

            public void Merge(LatencyTotals other)
            {
                Count += other.Count;
                _sumTicks += other._sumTicks;
                _maxTicks = Math.Max(_maxTicks, other._maxTicks);
            }
        }
    }
}