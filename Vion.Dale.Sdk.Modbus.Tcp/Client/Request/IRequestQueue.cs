using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     Manages a queue of asynchronous requests, processing them sequentially in the order they are enqueued.
    /// </summary>
    public interface IRequestQueue : IDisposable
    {
        /// <summary>
        ///     Gets the number of requests currently waiting in the queue to be processed.
        /// </summary>
        int QueuedRequestCount { get; }

        /// <summary>
        ///     Initializes the request queue with the specified queue capacity and overflow policy.
        /// </summary>
        /// <param name="capacity">The maximum number of requests that can be queued.</param>
        /// <param name="overflowPolicy">The policy to apply when the queue is full.</param>
        /// <exception cref="InvalidOperationException">Thrown when the queue is already initialized.</exception>
        void Initialize(int capacity, QueueOverflowPolicy overflowPolicy);

        /// <summary>
        ///     Enqueues a request that returns an array result.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the result array.</typeparam>
        /// <param name="requestName">The name of the request for logging and diagnostics purposes.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="operation">The asynchronous operation to execute.</param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        void Enqueue<T>(string requestName,
                        IActorDispatcher dispatcher,
                        Func<CancellationToken, Task<T[]>> operation,
                        Action<T[]> successCallback,
                        Action<Exception>? errorCallback)
            where T : unmanaged;

        /// <summary>
        ///     Enqueues a request that returns a single value result.
        /// </summary>
        /// <typeparam name="T">The type of the result value.</typeparam>
        /// <param name="requestName">The name of the request for logging and diagnostics purposes.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="operation">The asynchronous operation to execute.</param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        void Enqueue<T>(string requestName, IActorDispatcher dispatcher, Func<CancellationToken, Task<T>> operation, Action<T> successCallback, Action<Exception>? errorCallback);

        /// <summary>
        ///     Enqueues a request that does not return a result.
        /// </summary>
        /// <param name="requestName">The name of the request for logging and diagnostics purposes.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="operation">The asynchronous operation to execute.</param>
        /// <param name="successCallback">
        ///     The callback invoked when the operation succeeds.
        /// </param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        void Enqueue(string requestName, IActorDispatcher dispatcher, Func<CancellationToken, Task> operation, Action? successCallback, Action<Exception>? errorCallback);
    }
}