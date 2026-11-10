using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     The facts of one HTTP request, handed to its success and its error callback alongside the value or the exception.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         <c>StatusCode</c> is the status of the response the client judged, and is <c>null</c> when no response
    ///         arrived. It is what tells a server's answer from a failure to reach one: a refused connection and a 404 both
    ///         arrive as an <see cref="System.Net.Http.HttpRequestException" />, and only the 404 carries a status here.
    ///     </para>
    ///     <para>
    ///         <c>Headers</c> are that response's headers, its content headers included, on a refusal as on a success, and
    ///         are <c>null</c> exactly when <c>StatusCode</c> is. A header the platform understands, such as a date, reads
    ///         in the platform's canonical form. <c>RetryAfter</c> is the delay the response's <c>Retry-After</c> asks for,
    ///         or <c>null</c> when it sends none in either standard form; what to do with it is the block's decision.
    ///     </para>
    ///     <para>
    ///         The instant is taken when the outcome was observed, before the callback is handed to the block, so it is
    ///         unaffected by how long the block takes to run it. <c>ReceivedAt</c> is wall clock and is what you publish;
    ///         <c>ReceivedTimestamp</c> is the same instant on the monotonic scale — age a value with
    ///         <c>TimeProvider.GetElapsedTime(receipt.ReceivedTimestamp)</c>.
    ///     </para>
    ///     <para>
    ///         <c>RoundTrip</c> runs from handing the request to the client until the outcome was observed: for the members
    ///         that return a value it includes reading and deserializing the body; for <c>SendRequest</c> it ends at the
    ///         response headers, because the body is the callback's to read.
    ///     </para>
    ///     <para>
    ///         Ignoring a receipt is a discard: <c>(value, _) =&gt; Temperature = value.Current</c>.
    ///     </para>
    /// </remarks>
    /// <param name="ReceivedAt">The UTC wall clock instant the outcome was observed.</param>
    /// <param name="ReceivedTimestamp">The same instant on the monotonic timestamp scale, for ageing the value.</param>
    /// <param name="RoundTrip">The time from handing the request to the client until the outcome was observed.</param>
    /// <param name="Outcome">How the request ended.</param>
    /// <param name="StatusCode">The status of the response, or <c>null</c> when no response arrived.</param>
    /// <param name="Headers">
    ///     The headers of the response and of its content by case-insensitive name, a header sent more than once carrying its
    ///     values joined by <c>", "</c>; <c>null</c> when no response arrived.
    /// </param>
    [PublicApi]
    public readonly record struct HttpReceipt(
        DateTime ReceivedAt,
        long ReceivedTimestamp,
        TimeSpan RoundTrip,
        HttpOutcome Outcome,
        HttpStatusCode? StatusCode,
        IReadOnlyDictionary<string, string>? Headers = null)
    {
        private const string HttpDateFormat = "ddd, dd MMM yyyy HH:mm:ss 'GMT'";

        /// <summary>
        ///     Gets the delay the response's <c>Retry-After</c> asks for, or <c>null</c> when it sends none in either form.
        /// </summary>
        public TimeSpan? RetryAfter
        {
            get
            {
                if (Headers == null || !Headers.TryGetValue("Retry-After", out var value))
                {
                    return null;
                }

                if (value.Length > 0 && value.All(character => character is >= '0' and <= '9'))
                {
                    // A value past what an int holds saturates rather than reading as absent: it is the standard form,
                    // only longer than anything a block can mean, which is how RFC 9111 has a cache read delta-seconds.
                    return TimeSpan.FromSeconds(long.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var seconds) && seconds <= int.MaxValue ? seconds :
                                                    int.MaxValue);
                }

                if (!TryParseHttpDate(value, out var retryAt))
                {
                    return null;
                }

                // Measured against the server's own clock where it states one, so a gateway whose clock is off waits what the
                // server meant rather than that plus the skew.
                var now = Headers.TryGetValue("Date", out var date) && TryParseHttpDate(date, out var serverNow) ? serverNow : ReceivedAt;
                var delay = retryAt - now;

                return delay > TimeSpan.Zero ? delay : TimeSpan.Zero;
            }
        }

        /// <summary>
        ///     Reads IMF-fixdate alone. The two obsolete forms an HTTP-date may also take never reach it: the platform parses a
        ///     date header and renders every form it understands as IMF-fixdate before the package can read the header.
        /// </summary>
        private static bool TryParseHttpDate(string value, out DateTime instant)
        {
            // The exact parse matches day and month names in any case, and an HTTP-date is case-sensitive; rendering the
            // instant back and comparing ordinally is what refuses `fri, 31 dec`.
            return DateTime.TryParseExact(value, HttpDateFormat, CultureInfo.InvariantCulture, DateTimeStyles.AdjustToUniversal | DateTimeStyles.AssumeUniversal, out instant) &&
                   string.Equals(instant.ToString(HttpDateFormat, CultureInfo.InvariantCulture), value, StringComparison.Ordinal);
        }
    }
}