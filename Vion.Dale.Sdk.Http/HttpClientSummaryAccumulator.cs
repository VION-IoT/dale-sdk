using System;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     Accumulates the receipts of one client's requests into an <see cref="HttpClientSummary" />. One instance per
    ///     request executor, which is transient with its client, so each client instance has its own.
    /// </summary>
    /// <remarks>
    ///     Requests end on pool threads while the block reads the summary on its actor, so every field is guarded by one lock
    ///     and <see cref="Snapshot" /> copies out under it. The lock is held for a handful of field writes, or for one pass over
    ///     the window's slots, and is never held across a callback. The window is the Modbus link summary's.
    /// </remarks>
    internal sealed class HttpClientSummaryAccumulator
    {
        // The current, partial slot plus fifteen whole ones: a read covers at least the last fifteen minutes and less
        // than sixteen.
        private const int WindowSlotCount = 16;

        private static readonly TimeSpan WindowSlotLength = TimeSpan.FromMinutes(1);

        private readonly TimeProvider _clock;

        private readonly long _createdAt;

        private readonly object _gate = new();

        // Allocated once here so that recording a request never allocates.
        private readonly WindowSlot[] _slots = new WindowSlot[WindowSlotCount];

        private long _clientErrorCount;

        private long _contentErrorCount;

        private int _inFlightCount;

        private long _invalidCount;

        private DateTime? _lastFailureAt;

        private HttpOutcome? _lastFailureOutcome;

        private int? _lastFailureStatusCode;

        private DateTime? _lastResponseAt;

        private TimeSpan? _maxRoundTrip;

        private DateTime? _maxRoundTripAt;

        private long _serverErrorCount;

        private long _successCount;

        private long _timeoutCount;

        private long _transportErrorCount;

        /// <summary>Creates an accumulator whose window runs on the executor's registered clock.</summary>
        public HttpClientSummaryAccumulator(TimeProvider clock)
        {
            _clock = clock;
            _createdAt = clock.GetTimestamp();
        }

        /// <summary>Counts one request as issued, until <see cref="Record" /> ends it.</summary>
        public void Issue()
        {
            lock (_gate)
            {
                _inFlightCount++;
            }
        }

        /// <summary>Records the end of one issued request. Called when its receipt is stamped, before any callback runs.</summary>
        public void Record(HttpReceipt receipt)
        {
            lock (_gate)
            {
                _inFlightCount--;
                switch (receipt.Outcome)
                {
                    case HttpOutcome.Success:
                        _successCount++;
                        break;
                    case HttpOutcome.ClientError:
                        _clientErrorCount++;
                        break;
                    case HttpOutcome.ServerError:
                        _serverErrorCount++;
                        break;
                    case HttpOutcome.ContentError:
                        _contentErrorCount++;
                        break;
                    case HttpOutcome.Timeout:
                        _timeoutCount++;
                        break;
                    case HttpOutcome.TransportError:
                        _transportErrorCount++;
                        break;
                    case HttpOutcome.Invalid:
                        _invalidCount++;
                        break;
                }

                if (receipt.Outcome != HttpOutcome.Success)
                {
                    _lastFailureAt = receipt.ReceivedAt;
                    _lastFailureOutcome = receipt.Outcome;
                    _lastFailureStatusCode = (int?)receipt.StatusCode;
                }

                // A status is on the receipt exactly when a response arrived, including a 2xx whose body then broke.
                if (receipt.StatusCode != null)
                {
                    _lastResponseAt = receipt.ReceivedAt;
                }

                if (!ServerAnswered(receipt.Outcome))
                {
                    return;
                }

                // The clock is read inside the lock so that a slot is never reset to an older minute: a writer that read it
                // outside and then stalled for a whole ring span would wipe a slot already reused for a newer minute.
                CurrentSlot().RoundTrip.Add(receipt.RoundTrip);
                if (_maxRoundTrip is not { } max || receipt.RoundTrip > max)
                {
                    _maxRoundTrip = receipt.RoundTrip;
                    _maxRoundTripAt = receipt.ReceivedAt;
                }
            }
        }

        /// <summary>A consistent copy of everything recorded so far.</summary>
        public HttpClientSummary Snapshot()
        {
            lock (_gate)
            {
                var currentSlotId = CurrentSlotId();
                var roundTrip = default(LatencyTotals);
                foreach (var slot in _slots)
                {
                    // A slot left behind by an idle period keeps its old id, which is what drops it out of the window.
                    if (slot.Id > currentSlotId - WindowSlotCount)
                    {
                        roundTrip.Merge(slot.RoundTrip);
                    }
                }

                return new HttpClientSummary(_lastResponseAt,
                                             _lastFailureAt,
                                             _lastFailureOutcome,
                                             _lastFailureStatusCode,
                                             _successCount,
                                             _clientErrorCount,
                                             _serverErrorCount,
                                             _contentErrorCount,
                                             _timeoutCount,
                                             _transportErrorCount,
                                             _invalidCount,
                                             roundTrip.Count,
                                             roundTrip.Mean,
                                             roundTrip.Max,
                                             _maxRoundTrip,
                                             _maxRoundTripAt,
                                             _inFlightCount);
            }
        }

        /// <summary>
        ///     Whether a server answered: only then is the round trip how long a server took, rather than a bound that elapsed
        ///     or a connection that was refused at once.
        /// </summary>
        private static bool ServerAnswered(HttpOutcome outcome)
        {
            return outcome is HttpOutcome.Success or HttpOutcome.ClientError or HttpOutcome.ServerError or HttpOutcome.ContentError;
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
