using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     In-memory sink that captures the DevHost's <see cref="ILogger" /> output into a bounded
    ///     scrollback buffer plus a live fan-out to subscribers. Registered as an
    ///     <see cref="ILoggerProvider" /> alongside the console provider, so console output is unchanged;
    ///     this provider simply also captures. Backs <see cref="IDevHostControl.SubscribeLogs" /> /
    ///     <see cref="IDevHostControl.RecentLogs" />.
    /// </summary>
    public sealed class DevHostLogSink
    {
        private const int DefaultCapacity = 2000;

        private readonly int _capacity;

        private readonly ConcurrentQueue<LogLine> _buffer = new();

        private readonly object _subscribersLock = new();

        private readonly List<Action<LogLine>> _subscribers = new();

        public DevHostLogSink(int capacity = DefaultCapacity)
        {
            _capacity = capacity;
        }

        internal void Emit(LogLine line)
        {
            _buffer.Enqueue(line);
            while (_buffer.Count > _capacity && _buffer.TryDequeue(out _))
            {
                // Trim to capacity — bounded scrollback.
            }

            Action<LogLine>[] snapshot;
            lock (_subscribersLock)
            {
                if (_subscribers.Count == 0)
                {
                    return;
                }

                snapshot = _subscribers.ToArray();
            }

            foreach (var subscriber in snapshot)
            {
                try
                {
                    subscriber(line);
                }
                catch
                {
                    // A faulty subscriber must not break logging or other subscribers.
                }
            }
        }

        /// <summary>Subscribe to live log lines. Dispose the returned token to unsubscribe.</summary>
        public IDisposable Subscribe(Action<LogLine> sink)
        {
            if (sink is null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            lock (_subscribersLock)
            {
                _subscribers.Add(sink);
            }

            return new Unsubscriber(this, sink);
        }

        /// <summary>The most recent up-to-<paramref name="max" /> captured lines (scrollback), oldest first.</summary>
        public IReadOnlyList<LogLine> Recent(int max)
        {
            if (max <= 0)
            {
                return Array.Empty<LogLine>();
            }

            var all = _buffer.ToArray();
            return all.Length <= max ? all : all[^max..];
        }

        private void Unsubscribe(Action<LogLine> sink)
        {
            lock (_subscribersLock)
            {
                _subscribers.Remove(sink);
            }
        }

        private sealed class Unsubscriber : IDisposable
        {
            private readonly DevHostLogSink _sink;

            private Action<LogLine>? _callback;

            public Unsubscriber(DevHostLogSink sink, Action<LogLine> callback)
            {
                _sink = sink;
                _callback = callback;
            }

            public void Dispose()
            {
                if (_callback is null)
                {
                    return;
                }

                _sink.Unsubscribe(_callback);
                _callback = null;
            }
        }
    }

    /// <summary>
    ///     <see cref="ILoggerProvider" /> that feeds every log message into a <see cref="DevHostLogSink" />.
    ///     Additive — registered next to the console provider; does not replace or alter it.
    /// </summary>
    public sealed class DevHostLogSinkProvider : ILoggerProvider
    {
        private readonly DevHostLogSink _sink;

        public DevHostLogSinkProvider(DevHostLogSink sink)
        {
            _sink = sink;
        }

        public ILogger CreateLogger(string categoryName)
        {
            return new SinkLogger(_sink, categoryName);
        }

        public void Dispose()
        {
            // The sink outlives individual loggers; nothing provider-scoped to dispose.
        }

        private sealed class SinkLogger : ILogger
        {
            private readonly DevHostLogSink _sink;

            private readonly string _category;

            public SinkLogger(DevHostLogSink sink, string category)
            {
                _sink = sink;
                _category = category;
            }

            public IDisposable BeginScope<TState>(TState state)
                where TState : notnull
            {
                return NullScope.Instance;
            }

            public bool IsEnabled(LogLevel logLevel)
            {
                return logLevel != LogLevel.None;
            }

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
            {
                if (!IsEnabled(logLevel))
                {
                    return;
                }

                var message = formatter(state, exception);
                _sink.Emit(new LogLine(logLevel, _category, DateTimeOffset.UtcNow, message, exception?.ToString()));
            }

            private sealed class NullScope : IDisposable
            {
                public static readonly NullScope Instance = new();

                public void Dispose()
                {
                }
            }
        }
    }
}
