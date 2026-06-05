using System;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     Tracks the maximum of a value over a recent tumbling window, so an always-on "max" gauge reflects
    ///     recent behaviour instead of an actor's lifetime high-water mark (which on a long-running gateway
    ///     could be months stale). <see cref="Record" /> is single-writer (the actor thread); <see cref="Read" />
    ///     is side-effect-free and may run on any thread. Driven by the injected <see cref="TimeProvider" /> so
    ///     the TestKit can advance the window deterministically.
    /// </summary>
    internal sealed class WindowedMax<T>
        where T : struct, IComparable<T>
    {
        private readonly TimeProvider _timeProvider;

        private readonly TimeSpan _window;

        private T _current;

        private long _windowStart;

        public WindowedMax(TimeProvider timeProvider, TimeSpan window)
        {
            _timeProvider = timeProvider;
            _window = window;
            _windowStart = timeProvider.GetTimestamp();
        }

        /// <summary>Folds a sample into the current window, starting a fresh window if the old one elapsed.</summary>
        public void Record(T value)
        {
            if (_timeProvider.GetElapsedTime(_windowStart) >= _window)
            {
                _current = value;
                _windowStart = _timeProvider.GetTimestamp();
                return;
            }

            if (value.CompareTo(_current) > 0)
            {
                _current = value;
            }
        }

        /// <summary>The max within the current window, or <c>default</c> once the window has elapsed with no new samples (idle).</summary>
        public T Read()
        {
            return _timeProvider.GetElapsedTime(_windowStart) < _window ? _current : default;
        }
    }
}