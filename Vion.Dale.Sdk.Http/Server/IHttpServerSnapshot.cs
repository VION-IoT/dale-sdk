using System.Collections.Generic;
using System.Net.Http;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <summary>
    ///     The view of a hosted HTTP server a <c>Sync</c> callback receives: the responses it serves and the requests it has
    ///     answered. Valid only while that callback runs.
    /// </summary>
    [PublicApi]
    public interface IHttpServerSnapshot
    {
        /// <summary>
        ///     Gets how many answered requests were dropped, oldest first, because more arrived than the server keeps between
        ///     two calls to <see cref="TakeReceivedRequests" />.
        /// </summary>
        int DroppedRequestCount { get; }

        /// <summary>
        ///     Serves <paramref name="response" /> for <paramref name="method" /> on <paramref name="path" />, replacing any
        ///     response already set for that pair. The path starts with <c>/</c>, carries no query, and matches case-sensitively.
        /// </summary>
        /// <param name="method">The request method to answer.</param>
        /// <param name="path">The request path to answer, without a query string.</param>
        /// <param name="response">The response to send.</param>
        void SetResponse(HttpMethod method, string path, HttpServerResponse response);

        /// <summary>
        ///     Stops serving <paramref name="method" /> on <paramref name="path" />.
        /// </summary>
        /// <param name="method">The request method.</param>
        /// <param name="path">The request path.</param>
        /// <returns><c>true</c> when a response was set for that pair.</returns>
        bool RemoveResponse(HttpMethod method, string path);

        /// <summary>
        ///     Stops serving every response, so every request answers 404.
        /// </summary>
        void ClearResponses();

        /// <summary>
        ///     Returns the requests answered since the last call, oldest first, and resets <see cref="DroppedRequestCount" />.
        /// </summary>
        /// <returns>The answered requests, each returned once.</returns>
        IReadOnlyList<HttpServerRequest> TakeReceivedRequests();
    }
}