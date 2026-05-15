using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Microsoft.Extensions.Logging;

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
        ///     Pass the logic block that should handle the callbacks (typically <c>this</c> when calling from within a logic block).
        /// </param>
        /// <param name="operation">The asynchronous operation to execute.</param>
        /// <param name="successCallback">The callback invoked when the operation succeeds.</param>
        /// <param name="errorCallback">
        ///     The callback invoked when the operation fails.
        ///     Errors are always logged, regardless of whether an error callback is specified.
        /// </param>
        /// <param name="logger">The logger used for logging request execution and errors.</param>
        IRequest Create<T>(string requestName,
                           IActorDispatcher dispatcher,
                           Func<CancellationToken, Task<T[]>> operation,
                           Action<T[]> successCallback,
                           Action<Exception>? errorCallback,
                           ILogger logger)
            where T : unmanaged;

        /// <summary>
        ///     Creates a request that returns a single value result.
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
        /// <param name="logger">The logger used for logging request execution and errors.</param>
        IRequest Create<T>(string requestName,
                           IActorDispatcher dispatcher,
                           Func<CancellationToken, Task<T>> operation,
                           Action<T> successCallback,
                           Action<Exception>? errorCallback,
                           ILogger logger);

        /// <summary>
        ///     Creates a request that does not return a result.
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
        /// <param name="logger">The logger used for logging request execution and errors.</param>
        IRequest Create(string requestName,
                        IActorDispatcher dispatcher,
                        Func<CancellationToken, Task> operation,
                        Action? successCallback,
                        Action<Exception>? errorCallback,
                        ILogger logger);
    }
}