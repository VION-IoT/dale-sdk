using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.Sdk.Http.TestKit.Test
{
    /// <summary>
    ///     A synchronization context only its own thread can run, the shape a UI test or a single-threaded async test runner
    ///     gives a test body: a continuation posted to it waits until that thread is free, so the thread blocking on such a
    ///     continuation never returns.
    /// </summary>
    internal sealed class ThreadOwnedSynchronizationContext : SynchronizationContext
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();

        /// <summary>
        ///     Runs <paramref name="body" /> on a dedicated thread owning a context of this kind, and reports whether it finished
        ///     within <paramref name="patience" />; a body blocked on its own thread never does.
        /// </summary>
        public static bool TryRun(Func<Task> body, TimeSpan patience, out Exception? failure)
        {
            Exception? caught = null;
            var context = new ThreadOwnedSynchronizationContext();
            var thread = new Thread(() =>
                                    {
                                        SetSynchronizationContext(context);
                                        var task = body();
                                        task.ContinueWith(_ => context._queue.CompleteAdding(), TaskScheduler.Default);
                                        foreach (var (callback, state) in context._queue.GetConsumingEnumerable())
                                        {
                                            callback(state);
                                        }

                                        caught = task.Exception?.GetBaseException();
                                    }) { IsBackground = true };
            thread.Start();
            var finished = thread.Join(patience);
            failure = caught;

            return finished;
        }

        public override void Post(SendOrPostCallback d, object? state)
        {
            _queue.Add((d, state));
        }

        public override void Send(SendOrPostCallback d, object? state)
        {
            throw new NotSupportedException("A thread-owned context only accepts posts.");
        }
    }
}
