using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     Per-property emission gate. Pure: the caller supplies <c>now</c> on every <see cref="Offer" />
    ///     so behavior is fully deterministic under a virtual clock. Implements the RFC 0004 five-step
    ///     decision: value-equality floor, immediate bypass, deadband, leading-edge interval, trailing-edge hold.
    /// </summary>
    internal sealed class Throttler
    {
        private DateTimeOffset _lastEmitAt;

        private object? _pendingValue;

        /// <summary>
        ///     The policy this gate was built from — used to reconstruct a fresh gate on a value-clear (the gate has no
        ///     in-place reset).
        /// </summary>
        public ThrottlePolicy Policy { get; }

        public bool HasEmitted { get; private set; }

        public object? LastEmitted { get; private set; }

        public bool HasPending { get; private set; }

        public DateTimeOffset PendingDeadline { get; private set; }

        public Throttler(ThrottlePolicy policy)
        {
            Policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public OfferResult Offer(object? value, DateTimeOffset now)
        {
            // 1. Value-equality floor: an offered value equal to the last emitted is always dropped.
            if (HasEmitted && Equals(LastEmitted, value))
            {
                return OfferResult.Drop;
            }

            // 2. Immediate bypasses throttle + deadband (but not the floor above).
            if (Policy.Immediate)
            {
                MarkEmitted(value, now);
                return OfferResult.Emit;
            }

            // 3. Deadband: drop sub-threshold changes relative to the last emitted value.
            if (Policy.Threshold != null && HasEmitted && !Policy.Threshold.Exceeds(LastEmitted, value, Policy.MinChange!))
            {
                return OfferResult.Drop;
            }

            // 4. Leading edge: first emission ever, or the interval has elapsed.
            if (!HasEmitted || now - _lastEmitAt >= Policy.MinInterval)
            {
                MarkEmitted(value, now);
                return OfferResult.Emit;
            }

            // 5. Within the interval: hold the latest value until the deadline (latest-wins).
            HasPending = true;
            _pendingValue = value;
            PendingDeadline = _lastEmitAt + Policy.MinInterval;
            return OfferResult.Hold(PendingDeadline);
        }

        public bool TryFlush(DateTimeOffset now, out object? value)
        {
            if (HasPending)
            {
                value = _pendingValue;
                MarkEmitted(_pendingValue, now);
                return true;
            }

            value = null;
            return false;
        }

        private void MarkEmitted(object? value, DateTimeOffset now)
        {
            HasEmitted = true;
            LastEmitted = value;
            _lastEmitAt = now;
            HasPending = false;
            _pendingValue = null;
            PendingDeadline = default;
        }
    }
}