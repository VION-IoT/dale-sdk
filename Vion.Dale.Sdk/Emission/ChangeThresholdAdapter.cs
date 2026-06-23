using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     Wraps a strongly-typed <see cref="IChangeThreshold{T}" /> and exposes it through the
    ///     non-generic <see cref="IChangeThresholdAdapter" /> contract by unboxing the supplied values.
    /// </summary>
    /// <typeparam name="T">The value type the inner threshold compares.</typeparam>
    internal sealed class ChangeThresholdAdapter<T> : IChangeThresholdAdapter
    {
        private readonly IChangeThreshold<T> _inner;

        public ChangeThresholdAdapter(IChangeThreshold<T> inner)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
        }

        public bool Exceeds(object? last, object? candidate, string threshold)
        {
            if (last is null || candidate is null)
            {
                // A missing endpoint cannot be compared by magnitude; treat as "exceeds" so the
                // gate does not suppress the first real value after a null.
                return true;
            }

            var typedLast = (T)last;
            var typedCandidate = (T)candidate;
            return _inner.Exceeds(typedLast, typedCandidate, threshold);
        }
    }
}