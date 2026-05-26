using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit
{
    /// <summary>
    ///     A drop-in <see cref="IRequestQueue" /> that runs each request synchronously on the calling
    ///     thread instead of dispatching to a background consumer. Pairs with the TestKit's
    ///     <c>LogicBlockTestContext</c>: the request's success / error callback flows through
    ///     <c>IActorDispatcher.InvokeSynchronized</c> just as in production, lands in
    ///     <c>_pendingActions</c>, and drains on the next <c>FlushPendingActions()</c>.
    ///     <para>
    ///         This is the key piece that makes Modbus TCP testing fit the unit-test drive-flush-assert
    ///         rhythm: no background thread means no race between the SUT's callback landing in the
    ///         pending queue and the test thread's flush.
    ///     </para>
    /// </summary>
    [PublicApi]
    public sealed class SynchronousRequestQueue : IRequestQueue
    {
        private readonly IRequestFactory _requestFactory;

        private readonly ILogger _logger;

        public SynchronousRequestQueue(IRequestFactory requestFactory, ILogger? logger = null)
        {
            _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>Always zero — the synchronous queue never holds queued work.</summary>
        public int QueuedRequestCount
        {
            get => 0;
        }

        /// <summary>No-op. Capacity / overflow policy don't apply to the synchronous executor.</summary>
        public void Initialize(int capacity, QueueOverflowPolicy overflowPolicy)
        {
        }

        public void Enqueue<T>(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task<T[]>> operation,
                               Action<T[]> successCallback,
                               Action<Exception>? errorCallback)
            where T : unmanaged
        {
            var request = _requestFactory.Create(requestName, dispatcher, operation, successCallback, errorCallback, _logger);
            ExecuteSynchronously(request);
        }

        public void Enqueue<T>(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task<T>> operation,
                               Action<T> successCallback,
                               Action<Exception>? errorCallback)
        {
            var request = _requestFactory.Create(requestName, dispatcher, operation, successCallback, errorCallback, _logger);
            ExecuteSynchronously(request);
        }

        public void Enqueue(string requestName,
                            IActorDispatcher dispatcher,
                            Func<CancellationToken, Task> operation,
                            Action? successCallback,
                            Action<Exception>? errorCallback)
        {
            var request = _requestFactory.Create(requestName, dispatcher, operation, successCallback, errorCallback, _logger);
            ExecuteSynchronously(request);
        }

        public void Dispose()
        {
            // No unmanaged resources — the queue holds no state between calls.
        }

        private static void ExecuteSynchronously(IRequest request)
        {
            // Production's RequestQueue runs ExecuteAsync on its consumer thread; here we run it on the
            // calling (test) thread. The request's own try/catch funnels success and failure into the
            // dispatcher's InvokeSynchronized, so the contract for callers is unchanged.
            request.ExecuteAsync(CancellationToken.None).GetAwaiter().GetResult();
        }
    }
}
