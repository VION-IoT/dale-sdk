using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     In-proc, per-actor vitals aggregate. Fed by the actor middleware (handler outcomes), the Proto
    ///     mailbox-statistics hook, the spawn-time registry, and the <c>[Timer]</c> watchdog; read via
    ///     <see cref="Snapshot" />. All timing uses the injected <see cref="TimeProvider" /> so the TestKit
    ///     can drive it deterministically. The <c>*Max</c> vitals are tracked over a recent window (default
    ///     one minute) rather than the actor's lifetime.
    /// </summary>
    public sealed class RuntimeVitals : IActorMessageObserver, IActorVitalsCollector, IRuntimeDiagnostics
    {
        private static readonly TimeSpan DefaultWindow = TimeSpan.FromMinutes(1);

        private readonly ConcurrentDictionary<string, ActorState> _actors = new();

        private readonly TimeProvider _timeProvider;

        private readonly TimeSpan _window;

        public RuntimeVitals(TimeProvider timeProvider) : this(timeProvider, DefaultWindow)
        {
        }

        public RuntimeVitals(TimeProvider timeProvider, TimeSpan window)
        {
            _timeProvider = timeProvider;
            _window = window;
        }

        /// <summary>
        ///     Not used by the vitals core — rate, handler duration and errors are captured in
        ///     <see cref="OnHandled" />. Present only to satisfy <see cref="IActorMessageObserver" />.
        /// </summary>
        public void OnReceived(string actorName, object message)
        {
        }

        /// <summary>Records the outcome of an actor handling a single message.</summary>
        public void OnHandled(string actorName, object message, TimeSpan elapsed, Exception? exception)
        {
            GetOrAddState(actorName).RecordHandled(elapsed, exception, _timeProvider.GetUtcNow());
        }

        /// <summary>Records an actor's identity (category + dimensions), resolved at spawn time.</summary>
        public void Register(string actorName, ActorIdentity identity)
        {
            GetOrAddState(actorName).SetIdentity(identity);
        }

        /// <summary>Records a message being posted to an actor's mailbox (mailbox-depth numerator).</summary>
        public void OnMessagePosted(string actorName)
        {
            GetOrAddState(actorName).OnMessagePosted();
        }

        /// <summary>Records a message being taken off an actor's mailbox for handling.</summary>
        public void OnMessageReceived(string actorName)
        {
            GetOrAddState(actorName).OnMessageReceived();
        }

        /// <summary>Records a <c>[Timer]</c> callback's execution duration and scheduler jitter for an actor.</summary>
        public void OnTimerCallback(string actorName, TimeSpan callbackDuration, TimeSpan jitter)
        {
            GetOrAddState(actorName).RecordTimerCallback(callbackDuration, jitter);
        }

        /// <summary>A point-in-time copy of every tracked actor's vitals.</summary>
        public IReadOnlyList<ActorVitals> Snapshot()
        {
            return _actors.Select(entry => entry.Value.ToSnapshot(entry.Key)).ToList();
        }

        // Static factory + 'this' arg so the per-message GetOrAdd allocates no closure.
        private ActorState GetOrAddState(string actorName)
        {
            return _actors.GetOrAdd(actorName, static (_, self) => new ActorState(self._timeProvider, self._window), this);
        }

        private sealed class ActorState
        {
            private readonly WindowedMax<TimeSpan> _handlerDurationMax;

            private readonly WindowedMax<int> _mailboxDepthMax;

            private readonly WindowedMax<TimeSpan> _timerCallbackDurationMax;

            private readonly WindowedMax<TimeSpan> _timerJitterMax;

            private long _errors;

            private TimeSpan _handlerDurationTotal;

            private ActorIdentity? _identity;

            private DateTimeOffset _lastActivityUtc;

            private long _messagesHandled;

            private long _messagesPosted;

            private long _messagesReceived;

            public ActorState(TimeProvider timeProvider, TimeSpan window)
            {
                _handlerDurationMax = new WindowedMax<TimeSpan>(timeProvider, window);
                _timerCallbackDurationMax = new WindowedMax<TimeSpan>(timeProvider, window);
                _timerJitterMax = new WindowedMax<TimeSpan>(timeProvider, window);
                _mailboxDepthMax = new WindowedMax<int>(timeProvider, window);
            }

            public void SetIdentity(ActorIdentity identity)
            {
                _identity = identity;
            }

            public void OnMessagePosted()
            {
                Interlocked.Increment(ref _messagesPosted);
            }

            public void OnMessageReceived()
            {
                // Backlog at the moment of dequeue (including this message) is the peak the window should
                // catch; computed before incrementing received. Runs on the actor thread (single writer).
                var depth = (int)Math.Max(0L, Interlocked.Read(ref _messagesPosted) - Interlocked.Read(ref _messagesReceived));
                _mailboxDepthMax.Record(depth);
                Interlocked.Increment(ref _messagesReceived);
            }

            public void RecordHandled(TimeSpan elapsed, Exception? exception, DateTimeOffset now)
            {
                _messagesHandled++;
                if (exception != null)
                {
                    _errors++;
                }

                _handlerDurationMax.Record(elapsed);
                _handlerDurationTotal += elapsed;
                _lastActivityUtc = now;
            }

            public void RecordTimerCallback(TimeSpan callbackDuration, TimeSpan jitter)
            {
                _timerCallbackDurationMax.Record(callbackDuration);
                _timerJitterMax.Record(jitter.Duration());
            }

            public ActorVitals ToSnapshot(string actorName)
            {
                var mailboxDepth = (int)Math.Max(0L, Interlocked.Read(ref _messagesPosted) - Interlocked.Read(ref _messagesReceived));
                return new ActorVitals(actorName,
                                       _identity,
                                       _messagesHandled,
                                       _errors,
                                       _handlerDurationMax.Read(),
                                       _handlerDurationTotal,
                                       mailboxDepth,
                                       _mailboxDepthMax.Read(),
                                       _timerCallbackDurationMax.Read(),
                                       _timerJitterMax.Read(),
                                       _lastActivityUtc);
            }
        }
    }
}