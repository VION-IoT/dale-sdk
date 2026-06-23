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
        private readonly ThrottlePolicy _policy;

        private DateTimeOffset _lastEmitAt;

        private object? _pendingValue;

        public bool HasEmitted { get; private set; }

        public object? LastEmitted { get; private set; }

        public bool HasPending { get; private set; }

        public DateTimeOffset PendingDeadline { get; private set; }

        public Throttler(ThrottlePolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public OfferResult Offer(object? value, DateTimeOffset now)
        {
            // 1. Value-equality floor: an offered value equal to the last emitted is always dropped.
            if (HasEmitted && Equals(LastEmitted, value))
            {
                return OfferResult.Drop;
            }

            // 2. Immediate bypasses throttle + deadband (but not the floor above).
            if (_policy.Immediate)
            {
                MarkEmitted(value, now);
                return OfferResult.Emit;
            }

            // 3. Deadband: drop sub-threshold changes relative to the last emitted value.
            if (_policy.Threshold != null && HasEmitted && !_policy.Threshold.Exceeds(LastEmitted, value, _policy.MinChange!))
            {
                return OfferResult.Drop;
            }

            // 4. Leading edge: first emission ever, or the interval has elapsed.
            if (!HasEmitted || now - _lastEmitAt >= _policy.MinInterval)
            {
                MarkEmitted(value, now);
                return OfferResult.Emit;
            }

            // 5. Within the interval: hold the latest value until the deadline (latest-wins).
            HasPending = true;
            _pendingValue = value;
            PendingDeadline = _lastEmitAt + _policy.MinInterval;
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