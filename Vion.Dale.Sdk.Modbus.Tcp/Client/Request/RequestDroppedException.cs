using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     Exception thrown when a request is dropped from the queue.
    ///     This occurs when the queue is full and the overflow policy rejects the request,
    ///     or when attempting to enqueue a request after the queue has been disposed.
    /// </summary>
    [PublicApi]
    public class RequestDroppedException : Exception
    {
        /// <summary>
        ///     Gets the name of the dropped request.
        /// </summary>
        public string RequestName { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RequestDroppedException" /> class.
        /// </summary>
        /// <param name="requestName">The name of the request that was dropped.</param>
        /// <param name="reason">The reason the request was dropped (e.g., "queue full", "queue disposed").</param>
        public RequestDroppedException(string requestName, string reason) : base($"The '{requestName}' request was dropped reason: {reason}.")
        {
            RequestName = requestName;
        }
    }
}