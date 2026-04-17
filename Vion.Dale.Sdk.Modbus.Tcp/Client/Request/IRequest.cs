using System;
using System.Threading;
using System.Threading.Tasks;

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
        /// <returns>A task that represents the asynchronous operation.</returns>
        Task ExecuteAsync(CancellationToken cancellationToken);

        /// <summary>
        ///     Handles a failed request by logging the error and invoking the error callback if specified.
        /// </summary>
        /// <param name="exception">The exception that caused the request to fail.</param>
        void HandleRequestFailed(Exception exception);
    }
}