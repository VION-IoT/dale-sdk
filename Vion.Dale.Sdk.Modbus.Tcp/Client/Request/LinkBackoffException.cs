using System;
using System.Globalization;
using System.Net;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Tcp.Client.Request
{
    /// <summary>
    ///     Thrown when a request arrives while the client is waiting out a connect backoff. The device was never
    ///     contacted, so this says nothing about the link beyond the failed connects that armed the backoff.
    /// </summary>
    /// <remarks>
    ///     The backoff is armed from the second consecutive failed connect and is ended by a successful connect or by a
    ///     changed IP address or port. Until then every request fails this way immediately instead of paying its own
    ///     connection attempt.
    /// </remarks>
    [PublicApi]
    public class LinkBackoffException : Exception
    {
        /// <summary>
        ///     Gets when the client will attempt to connect again.
        /// </summary>
        public DateTime NextAttemptAt { get; }

        /// <summary>
        ///     Gets how many connection attempts have failed in a row.
        /// </summary>
        public int ConsecutiveConnectFailures { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="LinkBackoffException" /> class.
        /// </summary>
        /// <param name="ipAddress">The address the failed attempts targeted.</param>
        /// <param name="port">The port the failed attempts targeted.</param>
        /// <param name="consecutiveConnectFailures">How many connection attempts have failed in a row.</param>
        /// <param name="nextAttemptAt">When the client will attempt to connect again.</param>
        /// <param name="untilNextAttempt">How much of the backoff is left.</param>
        public LinkBackoffException(IPAddress ipAddress, int port, int consecutiveConnectFailures, DateTime nextAttemptAt, TimeSpan untilNextAttempt) :
            base(BackingOffMessage(ipAddress, port, consecutiveConnectFailures, untilNextAttempt))
        {
            NextAttemptAt = nextAttemptAt;
            ConsecutiveConnectFailures = consecutiveConnectFailures;
        }

        private static string BackingOffMessage(IPAddress ipAddress, int port, int consecutiveConnectFailures, TimeSpan untilNextAttempt)
        {
            return string.Format(CultureInfo.InvariantCulture,
                                 "The request was not attempted: the client is backing off after {0} consecutive failed connects to {1}:{2} " +
                                 "(next attempt in {3:0.#} s); the device was not contacted.",
                                 consecutiveConnectFailures,
                                 ipAddress,
                                 port,
                                 untilNextAttempt.TotalSeconds);
        }
    }
}