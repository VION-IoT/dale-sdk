using System;
using System.Globalization;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     Thrown when a request is dropped before execution — because the queue was full, or because the client was
    ///     disposed. The device was never contacted, so this says nothing about the link.
    /// </summary>
    [PublicApi]
    public class RequestDroppedException : Exception
    {
        /// <summary>
        ///     Gets the name of the dropped request.
        /// </summary>
        public string RequestName { get; }

        /// <summary>
        ///     Gets why the request was dropped.
        /// </summary>
        public RequestDropReason Reason { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RequestDroppedException" /> class for a request the full
        ///     queue evicted.
        /// </summary>
        /// <param name="requestName">The name of the request that was dropped.</param>
        /// <param name="capacity">The capacity the queue was configured with.</param>
        /// <param name="overflowPolicy">The overflow policy that decided the eviction.</param>
        public RequestDroppedException(string requestName, int capacity, QueueOverflowPolicy overflowPolicy) : base(QueueFullMessage(requestName, capacity, overflowPolicy))
        {
            RequestName = requestName;
            Reason = RequestDropReason.QueueFull;
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RequestDroppedException" /> class for a request enqueued
        ///     after the client was disposed.
        /// </summary>
        /// <param name="requestName">The name of the request that was dropped.</param>
        public RequestDroppedException(string requestName) : base(ClientDisposedMessage(requestName))
        {
            RequestName = requestName;
            Reason = RequestDropReason.ClientDisposed;
        }

        private static string QueueFullMessage(string requestName, int capacity, QueueOverflowPolicy overflowPolicy)
        {
            return string.Format(CultureInfo.InvariantCulture,
                                 "The '{0}' request was dropped before execution: the local request queue was full (capacity {1}, policy {2}); the device was not contacted.",
                                 requestName,
                                 capacity,
                                 overflowPolicy);
        }

        private static string ClientDisposedMessage(string requestName)
        {
            return string.Format(CultureInfo.InvariantCulture, "The '{0}' request was dropped before execution: the client was disposed.", requestName);
        }
    }
}