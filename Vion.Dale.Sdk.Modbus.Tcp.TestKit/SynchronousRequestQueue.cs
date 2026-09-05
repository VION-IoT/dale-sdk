using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit
{
    /// <summary>
    ///     Drop-in <see cref="IRequestQueue" /> that runs each request synchronously on the calling thread
    ///     instead of dispatching to a background consumer. Success / error callbacks flow through
    ///     <c>IActorDispatcher.InvokeSynchronized</c> just as in production and drain on the next
    ///     <c>LogicBlockTestContext.FlushPendingActions()</c>.
    /// </summary>
    /// <remarks>
    ///     Set <see cref="Hold" /> to buffer enqueued requests instead of running them, advance the test's clock, then
    ///     call <see cref="Drain" /> to run them in order. That is how a test puts virtual time between enqueue and
    ///     execution — which is what <c>MaxQueuedAge</c> measures. The age check itself lives in the request, so this
    ///     queue applies it with no logic of its own.
    /// </remarks>
    [PublicApi]
    public sealed class SynchronousRequestQueue : IRequestQueue
    {
        private readonly List<IRequest> _heldRequests = new();

        private readonly ILogger _logger;

        private readonly IRequestFactory _requestFactory;

        private ModbusLinkAccumulator? _accumulator;

        private TimeSpan? _maxQueuedAge;

        /// <summary>
        ///     When <c>true</c>, enqueued requests are buffered instead of executed until <see cref="Drain" /> is
        ///     called. Default is <c>false</c> — every request runs on the calling thread.
        /// </summary>
        public bool Hold { get; set; }

        public SynchronousRequestQueue(IRequestFactory requestFactory, ILogger? logger = null)
        {
            _requestFactory = requestFactory ?? throw new ArgumentNullException(nameof(requestFactory));
            _logger = logger ?? NullLogger.Instance;
        }

        /// <summary>The number of requests buffered by <see cref="Hold" /> and not yet drained.</summary>
        public int QueuedRequestCount
        {
            get => _heldRequests.Count;
        }

        /// <inheritdoc />
        public TimeSpan? MaxQueuedAge
        {
            get => _maxQueuedAge;

            set
            {
                // The same refusal RequestQueue makes, so the two shipped IRequestQueue implementations agree.
                // Without it the fake kept a negative age and expired every request it then drained, while the
                // inherited documentation promised this exception.
                if (value is { } age && age <= TimeSpan.Zero)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), age, $"{nameof(MaxQueuedAge)} must be greater than zero, or null to disable the check.");
                }

                _maxQueuedAge = value;
            }
        }

        /// <summary>Records the accumulator. Capacity / overflow policy don't apply to the synchronous executor.</summary>
        public void Initialize(int capacity, QueueOverflowPolicy overflowPolicy, ModbusLinkAccumulator accumulator)
        {
            _accumulator = accumulator;
        }

        public void Enqueue<T>(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task<T[]>> operation,
                               Action<T[], ModbusReceipt> successCallback,
                               Action<Exception, ModbusReceipt>? errorCallback)
            where T : unmanaged
        {
            Accept(_requestFactory.Create(requestName,
                                          dispatcher,
                                          operation,
                                          successCallback,
                                          errorCallback,
                                          RequireAccumulator(),
                                          _logger));
        }

        public void Enqueue<T>(string requestName,
                               IActorDispatcher dispatcher,
                               Func<CancellationToken, Task<T>> operation,
                               Action<T, ModbusReceipt> successCallback,
                               Action<Exception, ModbusReceipt>? errorCallback)
        {
            Accept(_requestFactory.Create(requestName,
                                          dispatcher,
                                          operation,
                                          successCallback,
                                          errorCallback,
                                          RequireAccumulator(),
                                          _logger));
        }

        public void Enqueue(string requestName,
                            IActorDispatcher dispatcher,
                            Func<CancellationToken, Task> operation,
                            Action<ModbusReceipt>? successCallback,
                            Action<Exception, ModbusReceipt>? errorCallback)
        {
            Accept(_requestFactory.Create(requestName,
                                          dispatcher,
                                          operation,
                                          successCallback,
                                          errorCallback,
                                          RequireAccumulator(),
                                          _logger));
        }

        public void EnqueueControlOperation(string requestName,
                                            IActorDispatcher dispatcher,
                                            Func<CancellationToken, Task> operation,
                                            Action? successCallback,
                                            Action<Exception>? errorCallback)
        {
            Accept(_requestFactory.CreateControlOperation(requestName,
                                                          dispatcher,
                                                          operation,
                                                          successCallback,
                                                          errorCallback,
                                                          _logger));
        }

        public void Dispose()
        {
            _heldRequests.Clear();
        }

        /// <summary>
        ///     Executes every request buffered while <see cref="Hold" /> was set, in the order they were enqueued, and
        ///     clears the buffer. Requests enqueued during the drain are buffered again if <see cref="Hold" /> is still
        ///     set.
        /// </summary>
        public void Drain()
        {
            var pending = _heldRequests.ToArray();
            _heldRequests.Clear();
            foreach (var request in pending)
            {
                ExecuteSynchronously(request, MaxQueuedAge);
            }
        }

        private ModbusLinkAccumulator RequireAccumulator()
        {
            return _accumulator ?? throw new InvalidOperationException($"{nameof(SynchronousRequestQueue)} is not initialized — enable the client before issuing operations.");
        }

        private void Accept(IRequest request)
        {
            if (Hold)
            {
                _heldRequests.Add(request);
                return;
            }

            ExecuteSynchronously(request, MaxQueuedAge);
        }

        private static void ExecuteSynchronously(IRequest request, TimeSpan? maxQueuedAge)
        {
            // Production's RequestQueue runs ExecuteAsync on its consumer thread; here we run it on the
            // calling (test) thread. The request's own try/catch funnels success and failure into the
            // dispatcher's InvokeSynchronized, so the contract for callers is unchanged.
            request.ExecuteAsync(CancellationToken.None, maxQueuedAge).GetAwaiter().GetResult();
        }
    }
}