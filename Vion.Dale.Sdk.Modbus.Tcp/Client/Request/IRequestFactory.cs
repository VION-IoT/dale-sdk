using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     Factory for creating request objects.
    /// </summary>
    public interface IRequestFactory
    {
        /// <summary>
        ///     Creates a request that returns an array result.
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
        /// <param name="accumulator">The client's link accumulator, fed with the request's receipt when it completes.</param>
        /// <param name="logger">The logger used for logging request execution and errors.</param>
        IRequest Create<T>(string requestName,
                           IActorDispatcher dispatcher,
                           Func<CancellationToken, Task<T[]>> operation,
                           Action<T[], ModbusReceipt> successCallback,
                           Action<Exception, ModbusReceipt>? errorCallback,
                           ModbusLinkAccumulator accumulator,
                           ILogger logger)
            where T : unmanaged;

        /// <summary>
        ///     Creates a request that returns a single value result.
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
        /// <param name="accumulator">The client's link accumulator, fed with the request's receipt when it completes.</param>
        /// <param name="logger">The logger used for logging request execution and errors.</param>
        IRequest Create<T>(string requestName,
                           IActorDispatcher dispatcher,
                           Func<CancellationToken, Task<T>> operation,
                           Action<T, ModbusReceipt> successCallback,
                           Action<Exception, ModbusReceipt>? errorCallback,
                           ModbusLinkAccumulator accumulator,
                           ILogger logger);

        /// <summary>
        ///     Creates a request that does not return a result.
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
        /// <param name="accumulator">The client's link accumulator, fed with the request's receipt when it completes.</param>
        /// <param name="logger">The logger used for logging request execution and errors.</param>
        IRequest Create(string requestName,
                        IActorDispatcher dispatcher,
                        Func<CancellationToken, Task> operation,
                        Action<ModbusReceipt>? successCallback,
                        Action<Exception, ModbusReceipt>? errorCallback,
                        ModbusLinkAccumulator accumulator,
                        ILogger logger);

        /// <summary>
        ///     Creates a request that operates on the client rather than on a device.
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
        /// <param name="logger">The logger used for logging request execution and errors.</param>
        /// <remarks>
        ///     Control operations carry no receipt, do not contribute to the link summary, and are never expired by the
        ///     queued-age check.
        /// </remarks>
        IRequest CreateControlOperation(string requestName,
                                        IActorDispatcher dispatcher,
                                        Func<CancellationToken, Task> operation,
                                        Action? successCallback,
                                        Action<Exception>? errorCallback,
                                        ILogger logger);
    }
}