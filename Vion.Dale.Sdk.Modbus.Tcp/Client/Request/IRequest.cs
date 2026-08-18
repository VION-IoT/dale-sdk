using System;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     Represents a request that can be executed asynchronously.
    /// </summary>
    public interface IRequest
    {
        /// <summary>
        ///     Gets the unique identifier for this request instance.
        ///     This ID is used to correlate log entries for a single request throughout its lifecycle.
        /// </summary>
        Guid Id { get; }

        /// <summary>
        ///     Gets the name of the request for logging and diagnostics purposes.
        /// </summary>
        string Name { get; }

        /// <summary>
        ///     Executes the operation asynchronously.
        /// </summary>
        /// <param name="cancellationToken">The cancellation token to cancel the operation.</param>
        /// <param name="maxQueuedAge">
        ///     The maximum time the request may have waited before execution, or <c>null</c> to execute it however long
        ///     it waited. A request that has waited longer completes with a <see cref="RequestExpiredException" /> and
        ///     the operation is not run. The check lives here rather than in the queue so that every
        ///     <see cref="IRequestQueue" /> implementation applies it.
        /// </param>
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task ExecuteAsync(CancellationToken cancellationToken, TimeSpan? maxQueuedAge);

        /// <summary>
        ///     Handles a request that was dropped before execution by logging it and invoking the error callback if
        ///     specified.
        /// </summary>
        /// <param name="exception">The exception that caused the request to be dropped.</param>
        void HandleRequestFailed(Exception exception);
    }
}