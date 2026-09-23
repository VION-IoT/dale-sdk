using System;
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
    ///         arrived. It is what tells a server's answer from a failure to reach it: a refused connection and a 404 both
    ///         arrive as an <see cref="System.Net.Http.HttpRequestException" />, and only the 404 carries a status here.
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
    [PublicApi]
    public readonly record struct HttpReceipt(DateTime ReceivedAt, long ReceivedTimestamp, TimeSpan RoundTrip, HttpOutcome Outcome, HttpStatusCode? StatusCode);
}
