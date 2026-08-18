using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

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
        ///     Gets or sets how long a request may wait in the queue before it is discarded instead of executed, or
        ///     <c>null</c> to execute every request however long it waited.
        /// </summary>
        /// <remarks>
        ///     Read at dequeue and passed to the request, so a change applies to requests already waiting. Control
        ///     operations are exempt.
        /// </remarks>
        TimeSpan? MaxQueuedAge { get; set; }

        /// <summary>
        ///     Initializes the request queue with the specified queue capacity and overflow policy.
        /// </summary>
        /// <param name="capacity">The maximum number of requests that can be queued.</param>
        /// <param name="overflowPolicy">The policy to apply when the queue is full.</param>
        /// <param name="accumulator">The owning client's link accumulator, fed with each request's receipt.</param>
        /// <exception cref="InvalidOperationException">Thrown when the queue is already initialized.</exception>
        void Initialize(int capacity, QueueOverflowPolicy overflowPolicy, ModbusLinkAccumulator accumulator);

        /// <summary>
        ///     Enqueues a request that returns an array result.
        /// </summary>
        /// <typeparam name="T">The unmanaged element type of the result array.</typeparam>
        /// <param name="requestName">The name of the request for logging and diagnostics purposes.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="operation">The asynchronous operation to execute.</param>
        /// <param name="successCallback">
        ///     The callback invoked with the result and the transaction's receipt when the operation
        ///     succeeds.
        /// </param>
        /// <param name="errorCallback">
        ///     The callback invoked with the failure and the transaction's receipt when the operation fails.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        void Enqueue<T>(string requestName,
                        IActorDispatcher dispatcher,
                        Func<CancellationToken, Task<T[]>> operation,
                        Action<T[], ModbusReceipt> successCallback,
                        Action<Exception, ModbusReceipt>? errorCallback)
            where T : unmanaged;

        /// <summary>
        ///     Enqueues a request that returns a single value result.
        /// </summary>
        /// <typeparam name="T">The type of the result value.</typeparam>
        /// <param name="requestName">The name of the request for logging and diagnostics purposes.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="operation">The asynchronous operation to execute.</param>
        /// <param name="successCallback">
        ///     The callback invoked with the result and the transaction's receipt when the operation
        ///     succeeds.
        /// </param>
        /// <param name="errorCallback">
        ///     The callback invoked with the failure and the transaction's receipt when the operation fails.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        void Enqueue<T>(string requestName,
                        IActorDispatcher dispatcher,
                        Func<CancellationToken, Task<T>> operation,
                        Action<T, ModbusReceipt> successCallback,
                        Action<Exception, ModbusReceipt>? errorCallback);

        /// <summary>
        ///     Enqueues a request that does not return a result.
        /// </summary>
        /// <param name="requestName">The name of the request for logging and diagnostics purposes.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="operation">The asynchronous operation to execute.</param>
        /// <param name="successCallback">The callback invoked with the transaction's receipt when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked with the failure and the transaction's receipt when the operation fails.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        void Enqueue(string requestName,
                     IActorDispatcher dispatcher,
                     Func<CancellationToken, Task> operation,
                     Action<ModbusReceipt>? successCallback,
                     Action<Exception, ModbusReceipt>? errorCallback);

        /// <summary>
        ///     Enqueues a request that operates on the client rather than on a device.
        /// </summary>
        /// <param name="requestName">The name of the request for logging and diagnostics purposes.</param>
        /// <param name="dispatcher">
        ///     The dispatcher that will invoke the callbacks.
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic
        ///     block).
        /// </param>
        /// <param name="operation">The asynchronous operation to execute.</param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <remarks>
        ///     Control operations carry no receipt, do not contribute to the link summary, and are exempt from
        ///     <see cref="MaxQueuedAge" />.
        /// </remarks>
        void EnqueueControlOperation(string requestName,
                                     IActorDispatcher dispatcher,
                                     Func<CancellationToken, Task> operation,
                                     Action? successCallback,
                                     Action<Exception>? errorCallback);
    }
}