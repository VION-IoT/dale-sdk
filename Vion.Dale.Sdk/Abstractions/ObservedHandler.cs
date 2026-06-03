using System;
using System.Threading.Tasks;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Runs an actor's message handler while measuring its duration and reporting the outcome to an
    ///     optional <see cref="IActorMessageObserver" />. Duration uses the injected <see cref="TimeProvider" />
    ///     (a cheap monotonic timestamp) so the TestKit can drive it deterministically. The handler's
    ///     exception is reported to the observer and then rethrown, leaving the caller's error handling
    ///     unchanged. A faulty observer is isolated — its exception never affects message handling.
    /// </summary>
    public static class ObservedHandler
    {
        public static async Task RunAsync(IActorMessageObserver? observer, string actorName, object? message, TimeProvider timeProvider, Func<Task> handler)
        {
            var startTimestamp = timeProvider.GetTimestamp();
            try
            {
                await handler();
            }
            catch (Exception exception)
            {
                Notify(observer, actorName, message, timeProvider, startTimestamp, exception);
                throw;
            }

            Notify(observer, actorName, message, timeProvider, startTimestamp, exception: null);
        }

        private static void Notify(IActorMessageObserver? observer, string actorName, object? message, TimeProvider timeProvider, long startTimestamp, Exception? exception)
        {
            if (observer == null || message == null)
            {
                return;
            }

            try
            {
                observer.OnHandled(actorName, message, timeProvider.GetElapsedTime(startTimestamp), exception);
            }
            catch
            {
                // A faulty observer must never affect message handling.
            }
        }
    }
}
