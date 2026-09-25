using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <summary>
    ///     A point-in-time summary of what a hosted HTTP server has answered and refused: counts of requests answered and
    ///     refused, the last request and the last refusal, and the connections it is serving now.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Read it whenever you want it, without a <c>Sync</c> callback — every read returns a consistent snapshot. Every
    ///         field is a service-property-legal type, so the whole summary can be published as one <c>[ServiceProperty]</c>.
    ///         It changes with every request, so a member publishing it whole sends a new value once per interval for as
    ///         long as requests arrive: once every 30 s by default, or at the <c>MinInterval</c> the member assigns.
    ///     </para>
    ///     <para>
    ///         A request is counted as answered once its response has been written in full — the moment it is also
    ///         recorded for the block to take. A route the block published itself with a 404 counts as answered; only a
    ///         404 or 405 the server sent because nothing was published counts as unmatched.
    ///     </para>
    ///     <para>
    ///         The counts are for the lifetime of the server instance, across disabling and enabling, and are never reset.
    ///         <c>DroppedCount</c> is the lifetime total; the snapshot's <c>DroppedRequestCount</c> counts only since the
    ///         block last took the requests.
    ///     </para>
    /// </remarks>
    /// <param name="LastRequestAt">The latest arrival among the requests the server has recorded.</param>
    /// <param name="AnsweredCount">Requests answered from a response the block published.</param>
    /// <param name="UnmatchedCount">Requests answered 404 or 405 because nothing was published for them.</param>
    /// <param name="RefusedCount">Requests the server refused itself: malformed, chunked, or over a size cap.</param>
    /// <param name="OverloadedCount">Connections refused with 503 because the connection limit was reached.</param>
    /// <param name="AbandonedCount">Connections closed with neither a response written in full nor a refusal.</param>
    /// <param name="DroppedCount">Recorded requests dropped from the log before the block took them.</param>
    /// <param name="LastRefusalAt">When the server last refused a request or a connection.</param>
    /// <param name="LastRefusalStatus">The status it refused with.</param>
    /// <param name="ActiveConnections">Connections the server is serving now.</param>
    [PublicApi]
    [DefaultMinInterval("30s")]
    public readonly record struct HttpServerSummary(
        [StructField(Title = "Last request", Description = "The latest arrival among the requests the server has recorded (UTC).")]
        DateTime? LastRequestAt,
        [StructField(Title = "Answered", Description = "Requests answered from a response the block published.")]
        long AnsweredCount,
        [StructField(Title = "Unmatched", Description = "Requests answered 404 or 405 because nothing was published for them.")]
        long UnmatchedCount,
        [StructField(Title = "Refused", Description = "Requests the server refused itself: malformed, chunked, or over a size cap.")]
        long RefusedCount,
        [StructField(Title = "Overloaded", Description = "Connections refused with 503 because the connection limit was reached.")]
        long OverloadedCount,
        [StructField(Title = "Abandoned", Description = "Connections closed with neither a response written in full nor a refusal.")]
        long AbandonedCount,
        [StructField(Title = "Dropped", Description = "Recorded requests dropped from the log before the block took them.")]
        long DroppedCount,
        [StructField(Title = "Last refusal", Description = "When the server last refused a request or a connection (UTC).")]
        DateTime? LastRefusalAt,
        [StructField(Title = "Last refusal status", Description = "The status the server last refused with.")]
        int? LastRefusalStatus,
        [StructField(Title = "Active connections", Description = "Connections the server is serving now.")]
        int ActiveConnections);
}