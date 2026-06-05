using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Fans an actor-message notification out to several <see cref="IActorMessageObserver" />s so the
    ///     DevHost message tap (RFC 0003) and the vitals collector (RFC 0005) can coexist on the single
    ///     middleware observer slot. A faulty observer is isolated — its exception never affects the other
    ///     observers or message delivery.
    /// </summary>
    public sealed class CompositeActorMessageObserver : IActorMessageObserver
    {
        private readonly IActorMessageObserver[] _observers;

        public CompositeActorMessageObserver(IEnumerable<IActorMessageObserver> observers)
        {
            _observers = observers is IActorMessageObserver[] array ? array : new List<IActorMessageObserver>(observers).ToArray();
        }

        public void OnReceived(string actorName, object message)
        {
            foreach (var observer in _observers)
            {
                try
                {
                    observer.OnReceived(actorName, message);
                }
                catch
                {
                    // A faulty observer must never affect the other observers or message delivery.
                }
            }
        }

        public void OnHandled(string actorName, object message, TimeSpan elapsed, Exception? exception)
        {
            foreach (var observer in _observers)
            {
                try
                {
                    observer.OnHandled(actorName, message, elapsed, exception);
                }
                catch
                {
                    // A faulty observer must never affect the other observers or message delivery.
                }
            }
        }

        /// <summary>
        ///     Combines the registered observers into one slot: <c>null</c> when none (the production
        ///     default), the single instance unwrapped when exactly one (no fan-out overhead — preserves the
        ///     prior single-observer behaviour), and a <see cref="CompositeActorMessageObserver" /> when several.
        /// </summary>
        public static IActorMessageObserver? Combine(IEnumerable<IActorMessageObserver> observers)
        {
            var list = observers as IReadOnlyList<IActorMessageObserver> ?? new List<IActorMessageObserver>(observers);
            if (list.Count == 0)
            {
                return null;
            }

            if (list.Count == 1)
            {
                return list[0];
            }

            return new CompositeActorMessageObserver(list);
        }
    }
}