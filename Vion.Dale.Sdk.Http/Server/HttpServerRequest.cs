using System;
using System.Collections.Generic;
using System.Net;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <summary>
    ///     A request a hosted HTTP server answered, as the client sent it.
    /// </summary>
    [PublicApi]
    public sealed class HttpServerRequest
    {
        /// <summary>
        ///     Gets the request method, as the client spelled it.
        /// </summary>
        public string Method { get; }

        /// <summary>
        ///     Gets the request path, without the query string.
        /// </summary>
        public string Path { get; }

        /// <summary>
        ///     Gets the query string without its leading <c>?</c>, or an empty string when there is none.
        /// </summary>
        public string Query { get; }

        /// <summary>
        ///     Gets the request headers by case-insensitive name; a header sent more than once carries its values joined by
        ///     <c>", "</c>.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>
        ///     Gets the request body.
        /// </summary>
        public ReadOnlyMemory<byte> Body { get; }

        /// <summary>
        ///     Gets when the request arrived, on the clock the server was composed with.
        /// </summary>
        public DateTimeOffset ReceivedAt { get; }

        /// <summary>
        ///     Gets the status the server answered the request with: the published response's, or the 404 or 405 it sent
        ///     when nothing was published for the request.
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        internal HttpServerRequest(string method,
                                   string path,
                                   string query,
                                   IReadOnlyDictionary<string, string> headers,
                                   byte[] body,
                                   DateTimeOffset receivedAt,
                                   HttpStatusCode statusCode)
        {
            Method = method;
            Path = path;
            Query = query;
            Headers = headers;
            Body = body;
            ReceivedAt = receivedAt;
            StatusCode = statusCode;
        }
    }
}