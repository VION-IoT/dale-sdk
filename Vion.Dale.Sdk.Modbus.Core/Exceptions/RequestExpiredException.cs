using System;
using System.Globalization;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Client;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Thrown when a request waited longer than <see cref="IModbusClient.MaxQueuedAge" /> before its turn came, so
    ///     it was completed without contacting the device. This says nothing about the link — it means the client could
    ///     not keep up with the rate requests were issued at, or was held up earlier by a fault.
    /// </summary>
    [PublicApi]
    public class RequestExpiredException : Exception
    {
        /// <summary>
        ///     Gets the name of the expired request.
        /// </summary>
        public string RequestName { get; }

        /// <summary>
        ///     Gets how long the request had been waiting when it was discarded.
        /// </summary>
        public TimeSpan QueuedWait { get; }

        /// <summary>
        ///     Gets the maximum queued age that was in force.
        /// </summary>
        public TimeSpan MaxQueuedAge { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="RequestExpiredException" /> class.
        /// </summary>
        /// <param name="requestName">The name of the request that expired.</param>
        /// <param name="queuedWait">How long the request had been waiting.</param>
        /// <param name="maxQueuedAge">The maximum queued age that was in force.</param>
        public RequestExpiredException(string requestName, TimeSpan queuedWait, TimeSpan maxQueuedAge) : base(ExpiredMessage(requestName, queuedWait, maxQueuedAge))
        {
            RequestName = requestName;
            QueuedWait = queuedWait;
            MaxQueuedAge = maxQueuedAge;
        }

        private static string ExpiredMessage(string requestName, TimeSpan queuedWait, TimeSpan maxQueuedAge)
        {
            return string.Format(CultureInfo.InvariantCulture,
                                 "The '{0}' request expired in the local request queue after waiting {1:0.#} s (MaxQueuedAge {2:0.#} s); the device was not contacted.",
                                 requestName,
                                 queuedWait.TotalSeconds,
                                 maxQueuedAge.TotalSeconds);
        }
    }
}