using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     In-proc, per-actor vitals aggregate. Fed by the actor middleware (handler outcomes) and the
    ///     mailbox-statistics hook; read via <see cref="Snapshot" />. All timing uses the injected
    ///     <see cref="TimeProvider" /> so the TestKit can drive it deterministically.
    /// </summary>
    public sealed class RuntimeVitals : IActorMessageObserver
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

        /// <summary>A point-in-time copy of every tracked actor's vitals.</summary>
        public IReadOnlyList<ActorVitals> Snapshot()
        {
            return _actors.Select(entry => entry.Value.ToSnapshot(entry.Key)).ToList();
        }

        private sealed class ActorState
        {
            private long _messagesHandled;
            private long _errors;
            private TimeSpan _handlerDurationMax;
            private DateTimeOffset _lastActivityUtc;
            private ActorIdentity? _identity;

            public void SetIdentity(ActorIdentity identity)
            {
                _identity = identity;
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
                return new ActorVitals(actorName, _identity, _messagesHandled, _errors, _handlerDurationMax, _lastActivityUtc);
            }
        }
    }
}
