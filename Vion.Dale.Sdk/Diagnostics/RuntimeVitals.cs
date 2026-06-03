using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     In-proc, per-actor vitals aggregate. Fed by the actor middleware (handler outcomes) and the
    ///     mailbox-statistics hook; read via <see cref="Snapshot" />. All timing uses the injected
    ///     <see cref="TimeProvider" /> so the TestKit can drive it deterministically.
    /// </summary>
    public sealed class RuntimeVitals : IActorMessageObserver, IActorVitalsCollector
    {
        private readonly TimeProvider _timeProvider;
        private readonly ConcurrentDictionary<string, ActorState> _actors = new ConcurrentDictionary<string, ActorState>();

        public RuntimeVitals(TimeProvider timeProvider)
        {
            _timeProvider = timeProvider;
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
            var state = _actors.GetOrAdd(actorName, _ => new ActorState());
            state.RecordHandled(elapsed, exception, _timeProvider.GetUtcNow());
        }

        /// <summary>Records an actor's identity (category + dimensions), resolved at spawn time.</summary>
        public void Register(string actorName, ActorIdentity identity)
        {
            var state = _actors.GetOrAdd(actorName, _ => new ActorState());
            state.SetIdentity(identity);
        }

        /// <summary>Records a message being posted to an actor's mailbox (mailbox-depth numerator).</summary>
        public void OnMessagePosted(string actorName)
        {
            _actors.GetOrAdd(actorName, _ => new ActorState()).OnMessagePosted();
        }

        /// <summary>Records a message being taken off an actor's mailbox for handling.</summary>
        public void OnMessageReceived(string actorName)
        {
            _actors.GetOrAdd(actorName, _ => new ActorState()).OnMessageReceived();
        }

        /// <summary>A point-in-time copy of every tracked actor's vitals.</summary>
        public IReadOnlyList<ActorVitals> Snapshot()
        {
            return _actors.Select(entry => entry.Value.ToSnapshot(entry.Key)).ToList();
        }

        private sealed class ActorState
        {
            private long _messagesHandled;
            private long _errors;
            private long _messagesPosted;
            private long _messagesReceived;
            private TimeSpan _handlerDurationMax;
            private DateTimeOffset _lastActivityUtc;
            private ActorIdentity? _identity;

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
                Interlocked.Increment(ref _messagesReceived);
            }

            public void RecordHandled(TimeSpan elapsed, Exception? exception, DateTimeOffset now)
            {
                _messagesHandled++;
                if (exception != null)
                {
                    _errors++;
                }

                if (elapsed > _handlerDurationMax)
                {
                    _handlerDurationMax = elapsed;
                }

                _lastActivityUtc = now;
            }

            public ActorVitals ToSnapshot(string actorName)
            {
                var mailboxDepth = (int)Math.Max(0L, Interlocked.Read(ref _messagesPosted) - Interlocked.Read(ref _messagesReceived));
                return new ActorVitals(actorName, _identity, _messagesHandled, _errors, _handlerDurationMax, mailboxDepth, _lastActivityUtc);
            }
        }
    }
}
