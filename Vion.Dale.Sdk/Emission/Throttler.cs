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

        private bool _hasEmitted;
        private object? _lastEmitted;
        private DateTimeOffset _lastEmitAt;

        private bool _hasPending;
        private object? _pendingValue;
        private DateTimeOffset _pendingDeadline;

        public Throttler(ThrottlePolicy policy)
        {
            _policy = policy ?? throw new ArgumentNullException(nameof(policy));
        }

        public bool HasEmitted => _hasEmitted;

        public object? LastEmitted => _lastEmitted;

        public bool HasPending => _hasPending;

        public DateTimeOffset PendingDeadline => _pendingDeadline;

        public OfferResult Offer(object? value, DateTimeOffset now)
        {
            // 1. Value-equality floor: an offered value equal to the last emitted is always dropped.
            if (_hasEmitted && object.Equals(_lastEmitted, value))
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
            if (_policy.Threshold != null
                && _hasEmitted
                && !_policy.Threshold.Exceeds(_lastEmitted, value, _policy.MinChange!))
            {
                return OfferResult.Drop;
            }

            // 4. Leading edge: first emission ever, or the interval has elapsed.
            if (!_hasEmitted || now - _lastEmitAt >= _policy.MinInterval)
            {
                MarkEmitted(value, now);
                return OfferResult.Emit;
            }

            // 5. Within the interval: hold the latest value until the deadline (latest-wins).
            _hasPending = true;
            _pendingValue = value;
            _pendingDeadline = _lastEmitAt + _policy.MinInterval;
            return OfferResult.Hold(_pendingDeadline);
        }

        public bool TryFlush(DateTimeOffset now, out object? value)
        {
            if (_hasPending)
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
            _hasEmitted = true;
            _lastEmitted = value;
            _lastEmitAt = now;
            _hasPending = false;
            _pendingValue = null;
            _pendingDeadline = default;
        }
    }
}
