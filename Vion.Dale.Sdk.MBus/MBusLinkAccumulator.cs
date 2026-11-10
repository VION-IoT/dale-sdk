using System;

namespace Vion.Dale.Sdk.MBus
{
    /// <summary>
    ///     Accumulates the receipts of one M-Bus contract's reads into an <see cref="MBusLinkSummary" />. A copy of the
    ///     Modbus link accumulator for M-Bus's outcomes, kept free of any Modbus package.
    /// </summary>
    /// <remarks>
    ///     Every field is guarded by one lock and <see cref="Snapshot" /> copies out under it, so a reader on another
    ///     thread never sees half a record. The lock is held for a handful of field writes, or one pass over the window's
    ///     slots, and never across a callback.
    /// </remarks>
    internal sealed class MBusLinkAccumulator
    {
        // The current, partial slot plus fifteen whole ones: a read covers at least the last fifteen minutes and less
        // than sixteen.
        private const int WindowSlotCount = 16;

        private static readonly TimeSpan WindowSlotLength = TimeSpan.FromMinutes(1);

        private readonly TimeProvider _clock;

        private readonly long _createdAt;

        private readonly object _gate = new();

        private readonly long[] _outcomeCounts = new long[Enum.GetValues(typeof(MBusOutcome)).Length];

        private readonly WindowSlot[] _slots = new WindowSlot[WindowSlotCount];

        private long _busyCount;

        private DateTime? _lastContactAt;

        private DateTime? _lastFailureAt;

        private MBusOutcome? _lastFailureOutcome;

        private TimeSpan? _maxQueuedWait;

        private DateTime? _maxQueuedWaitAt;

        private TimeSpan? _maxRoundTrip;

        private DateTime? _maxRoundTripAt;

        private MBusLinkState _state;

        public MBusLinkAccumulator(TimeProvider clock)
        {
            _clock = clock;
            _createdAt = clock.GetTimestamp();
        }

        /// <summary>Records one completed read, with its outcome as the logic block receives it.</summary>
        public void Record(MBusReceipt receipt)
        {
            lock (_gate)
            {
                _outcomeCounts[(int)receipt.Outcome]++;
                switch (receipt.Outcome)
                {
                    case MBusOutcome.Success:
                        _state = MBusLinkState.Online;
                        _lastContactAt = receipt.ReceivedAt;
                        break;
                    case MBusOutcome.NoResponse:
                    case MBusOutcome.Garbled:
                    case MBusOutcome.LineUnavailable:
                    case MBusOutcome.Timeout:
                    case MBusOutcome.ProtocolError:
                        _state = MBusLinkState.Faulted;
                        break;
                }

                if (receipt.Outcome != MBusOutcome.Success)
                {
                    _lastFailureAt = receipt.ReceivedAt;
                    _lastFailureOutcome = receipt.Outcome;
                }

                // The clock is read inside the lock so that a slot is never reset to an older minute by a writer that
                // read it outside and then stalled.
                ref var slot = ref CurrentSlot();

                if (WasAnswered(receipt.Outcome))
                {
                    slot.RoundTrip.Add(receipt.RoundTrip);
                    if (_maxRoundTrip is not { } max || receipt.RoundTrip > max)
                    {
                        _maxRoundTrip = receipt.RoundTrip;
                        _maxRoundTripAt = receipt.ReceivedAt;
                    }
                }

                // An Invalid read may have been refused before it left the block, with a wait that never happened.
                if (receipt.Outcome != MBusOutcome.Invalid)
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

        /// <summary>Counts a read dropped because the binding's previous read was still pending.</summary>
        public void RecordBusy()
        {
            lock (_gate)
            {
                _busyCount++;
            }
        }

        /// <summary>A consistent copy of everything recorded so far.</summary>
        public MBusLinkSummary Snapshot()
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

                return new MBusLinkSummary(_state,
                                           _lastContactAt,
                                           _lastFailureAt,
                                           _lastFailureOutcome,
                                           Count(MBusOutcome.Success),
                                           Count(MBusOutcome.NoResponse),
                                           Count(MBusOutcome.Garbled),
                                           Count(MBusOutcome.LineUnavailable),
                                           Count(MBusOutcome.Timeout),
                                           Count(MBusOutcome.ProtocolError),
                                           Count(MBusOutcome.Expired),
                                           Count(MBusOutcome.Dropped),
                                           Count(MBusOutcome.Invalid),
                                           _busyCount,
                                           roundTrip.Count,
                                           roundTrip.Mean,
                                           roundTrip.Max,
                                           _maxRoundTrip,
                                           _maxRoundTripAt,
                                           queuedWait.Count,
                                           queuedWait.Mean,
                                           queuedWait.Max,
                                           _maxQueuedWait,
                                           _maxQueuedWaitAt);
            }
        }

        /// <summary>
        ///     Whether the provider answered the read, so its round trip is a real one. A timed-out read was published but
        ///     never answered; an Invalid one may never have been published.
        /// </summary>
        private static bool WasAnswered(MBusOutcome outcome)
        {
            return outcome is MBusOutcome.Success or MBusOutcome.NoResponse or MBusOutcome.Garbled or MBusOutcome.LineUnavailable or MBusOutcome.ProtocolError;
        }

        private long Count(MBusOutcome outcome)
        {
            return _outcomeCounts[(int)outcome];
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
