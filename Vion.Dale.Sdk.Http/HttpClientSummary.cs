using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http
{
    /// <summary>
    ///     A point-in-time summary of one HTTP client's requests: when a server last answered, the last failure, lifetime
    ///     counts per outcome, and round trips over a recent window and since the client started.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read it whenever you want it — every read returns a consistent snapshot. Every field is a
    ///         service-property-legal type, so the whole summary can be published as one <c>[ServiceProperty]</c>. It
    ///         changes with every request, so a member publishing it whole sends a new value once per interval for as
    ///         long as the client is in use: once every 30 s by default, or at the <c>MinInterval</c> the member assigns.
    ///         Where a signal's every edge matters, take it from the request's receipt instead.
    ///     </para>
    ///     <para>
    ///         Each client instance keeps its own summary. A block that calls two services and wants them apart injects
    ///         two clients.
    ///     </para>
    ///     <para>
    ///         There is no up/down verdict: one client may call many servers, and only the caller knows its own cadence.
    ///         Build one from <c>LastResponseAt</c> and the last failure.
    ///     </para>
    ///     <para>
    ///         The <c>Recent</c> figures cover a window of the last 15 minutes on the client's clock — at least 15 and less
    ///         than 16, or the client's whole life where that is shorter. An empty window reads its mean and max as
    ///         <c>null</c> and its count as zero. Round trips are taken only from requests a server answered: successes,
    ///         client errors, server errors and content errors.
    ///     </para>
    ///     <para>
    ///         The counts and the <c>since start</c> maximum are for the lifetime of the client instance and are never
    ///         reset. A call refused at the caller, before any request exists, is not counted.
    ///     </para>
    /// </remarks>
    /// <param name="LastResponseAt">When a server last answered, with any status.</param>
    /// <param name="LastFailureAt">When the last request that did not succeed ended.</param>
    /// <param name="LastFailureOutcome">How that request ended.</param>
    /// <param name="LastFailureStatusCode">The status that request was answered with, or <c>null</c> when no response arrived.</param>
    /// <param name="SuccessCount">Requests answered with a 2xx status whose content was read.</param>
    /// <param name="ClientErrorCount">Requests answered with a status outside 2xx and below 500.</param>
    /// <param name="ServerErrorCount">Requests answered with a status of 500 or above.</param>
    /// <param name="ContentErrorCount">Requests answered with a 2xx status whose body was absent, malformed or null.</param>
    /// <param name="TimeoutCount">Requests the request's own timeout, or the client's, ended.</param>
    /// <param name="TransportErrorCount">Requests whose connection, TLS handshake or response stream failed.</param>
    /// <param name="InvalidCount">Requests the client could not build or refused before sending.</param>
    /// <param name="RecentRoundTripCount">Requests a server answered in the last 15 minutes.</param>
    /// <param name="RecentMeanRoundTrip">The mean round trip over the last 15 minutes.</param>
    /// <param name="RecentMaxRoundTrip">The longest round trip in the last 15 minutes.</param>
    /// <param name="MaxRoundTrip">The longest round trip since the client started.</param>
    /// <param name="MaxRoundTripAt">When the request that set <paramref name="MaxRoundTrip" /> ended.</param>
    /// <param name="InFlightCount">Requests issued and not yet ended.</param>
    [PublicApi]
    [DefaultMinInterval("30s")]
    public readonly record struct HttpClientSummary(
        [StructField(Title = "Last response", Description = "When a server last answered, with any status (UTC).")]
        DateTime? LastResponseAt,
        [StructField(Title = "Last failure", Description = "When the last request that did not succeed ended (UTC).")]
        DateTime? LastFailureAt,
        [StructField(Title = "Last failure outcome", Description = "How that last request ended.")]
        HttpOutcome? LastFailureOutcome,
        [StructField(Title = "Last failure status", Description = "The status that last request was answered with; empty when no response arrived.")]
        int? LastFailureStatusCode,
        [StructField(Title = "Successes", Description = "Answered with a 2xx status, content read.")]
        long SuccessCount,
        [StructField(Title = "Client errors", Description = "Answered with a status outside 2xx and below 500 — the request was not what the server accepts.")]
        long ClientErrorCount,
        [StructField(Title = "Server errors", Description = "Answered with a status of 500 or above.")]
        long ServerErrorCount,
        [StructField(Title = "Content errors", Description = "Answered with a 2xx status whose body was absent, malformed or null.")]
        long ContentErrorCount,
        [StructField(Title = "Timeouts", Description = "No response before the request's own timeout, or the client's.")]
        long TimeoutCount,
        [StructField(Title = "Transport errors", Description = "The connection, the TLS handshake or the response stream failed.")]
        long TransportErrorCount,
        [StructField(Title = "Invalid", Description = "Not sent: the client could not be built, or refused the request.")]
        long InvalidCount,
        [StructField(Title = "Round trips (15 min)", Description = "Requests a server answered in the last 15 minutes, which the two round-trip figures beside it rest on.")]
        long RecentRoundTripCount,
        [StructField(Title = "Round trip (mean, 15 min)", Description = "Mean time from sending to the outcome over the last 15 minutes; empty when no server answered.")]
        TimeSpan? RecentMeanRoundTrip,
        [StructField(Title = "Round trip (max, 15 min)", Description = "Longest time from sending to the outcome in the last 15 minutes; empty when no server answered.")]
        TimeSpan? RecentMaxRoundTrip,
        [StructField(Title = "Round trip (max since start)", Description = "The longest time from sending to the outcome since the client started.")]
        TimeSpan? MaxRoundTrip,
        [StructField(Title = "Round trip (max since start) at", Description = "When the longest round trip since the client started ended (UTC).")]
        DateTime? MaxRoundTripAt,
        [StructField(Title = "In flight", Description = "Requests issued and not yet ended.")]
        int InFlightCount);
}