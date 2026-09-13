using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Text;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <summary>
    ///     A response a hosted HTTP server sends: a status, an optional content type and a body.
    /// </summary>
    [PublicApi]
    public sealed class HttpServerResponse
    {
        private static readonly IReadOnlyList<KeyValuePair<string, string>> NoHeaders = Array.Empty<KeyValuePair<string, string>>();

        /// <summary>
        ///     Gets the status code sent.
        /// </summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>
        ///     Gets the <c>Content-Type</c> sent with the body, or <c>null</c> for none.
        /// </summary>
        public string? ContentType { get; }

        /// <summary>
        ///     Gets the body sent.
        /// </summary>
        public ReadOnlyMemory<byte> Body { get; }

        /// <summary>
        ///     The headers the server adds itself, such as the <c>Allow</c> of a 405. Never set by a block.
        /// </summary>
        internal IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="HttpServerResponse" /> class.
        /// </summary>
        /// <param name="statusCode">The status code, from 100 to 599.</param>
        /// <param name="contentType">The <c>Content-Type</c> of the body, or <c>null</c> for none.</param>
        /// <param name="body">The body; copied, so later changes to the array are not sent.</param>
        public HttpServerResponse(HttpStatusCode statusCode, string? contentType = null, byte[]? body = null) : this(statusCode, contentType, body, NoHeaders)
        {
        }

        private HttpServerResponse(HttpStatusCode statusCode, string? contentType, byte[]? body, IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            if ((int)statusCode is < 100 or > 599)
            {
                throw new ArgumentOutOfRangeException(nameof(statusCode),
                                                      statusCode,
                                                      string.Format(CultureInfo.InvariantCulture, "An HTTP status code lies from 100 to 599; {0} does not.", (int)statusCode));
            }

            StatusCode = statusCode;
            ContentType = contentType;
            Body = body == null ? ReadOnlyMemory<byte>.Empty : (byte[])body.Clone();
            Headers = headers;
        }

        /// <summary>
        ///     Creates a response carrying <paramref name="json" /> as UTF-8 with the <c>application/json</c> content type.
        /// </summary>
        /// <param name="json">The JSON document to send.</param>
        /// <param name="statusCode">The status code; 200 by default.</param>
        /// <returns>The response.</returns>
        public static HttpServerResponse Json(string json, HttpStatusCode statusCode = HttpStatusCode.OK)
        {
            if (json == null)
            {
                throw new ArgumentNullException(nameof(json));
            }

            return new HttpServerResponse(statusCode, "application/json", Encoding.UTF8.GetBytes(json));
        }

        /// <summary>
        ///     The empty 404 the server sends for a path nothing is published on.
        /// </summary>
        internal static HttpServerResponse NotFound()
        {
            return new HttpServerResponse(HttpStatusCode.NotFound);
        }

        /// <summary>
        ///     The empty 405 the server sends for a path published only under other methods, naming those methods.
        /// </summary>
        internal static HttpServerResponse MethodNotAllowed(IEnumerable<string> allowedMethods)
        {
            return new HttpServerResponse(HttpStatusCode.MethodNotAllowed, null, null, new[] { new KeyValuePair<string, string>("Allow", string.Join(", ", allowedMethods)) });
        }

        /// <summary>
        ///     A refusal the transport answers itself, before a request reaches the route table.
        /// </summary>
        internal static HttpServerResponse Refusal(HttpStatusCode statusCode)
        {
            return new HttpServerResponse(statusCode);
        }
    }
}
