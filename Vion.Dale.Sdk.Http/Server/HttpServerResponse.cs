using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Text;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <summary>
    ///     A response a hosted HTTP server sends: a status, an optional content type, headers and a body.
    /// </summary>
    /// <remarks>
    ///     A response is immutable: <see cref="WithHeader" /> returns a new one, so a header reaches a response however it was
    ///     built — <c>HttpServerResponse.Json(body, HttpStatusCode.TooManyRequests).WithHeader("Retry-After", "120")</c>. The
    ///     server writes <c>Content-Type</c>, <c>Content-Length</c> and <c>Connection</c> itself, and refuses each of them
    ///     and <c>Transfer-Encoding</c> as a header.
    /// </remarks>
    [PublicApi]
    public sealed class HttpServerResponse
    {
        private static readonly IReadOnlyList<KeyValuePair<string, string>> NoHeaders = Array.Empty<KeyValuePair<string, string>>();

        private static readonly string[] FramingHeaders = { "Content-Type", "Content-Length", "Connection", "Transfer-Encoding" };

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
        ///     Gets the headers sent besides the content type, in the order added; a name added twice is sent twice.
        /// </summary>
        public IReadOnlyList<KeyValuePair<string, string>> Headers { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="HttpServerResponse" /> class.
        /// </summary>
        /// <param name="statusCode">The status code of a final response, from 200 to 599.</param>
        /// <param name="contentType">
        ///     The <c>Content-Type</c> of the body, or <c>null</c> for none: printable ASCII and tabs only, since it is written
        ///     into the response's header block as given.
        /// </param>
        /// <param name="body">The body; copied, so later changes to the array are not sent.</param>
        public HttpServerResponse(HttpStatusCode statusCode, string? contentType = null, byte[]? body = null) : this(statusCode, contentType, body, NoHeaders)
        {
        }

        private HttpServerResponse(HttpStatusCode statusCode, string? contentType, byte[]? body, IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            // A 1xx is an interim response: a client that receives one keeps waiting for the final response, which this
            // server, closing the connection after one response, never sends.
            if ((int)statusCode is < 200 or > 599)
            {
                throw new ArgumentOutOfRangeException(nameof(statusCode),
                                                      statusCode,
                                                      string.Format(CultureInfo.InvariantCulture, "A final HTTP status code lies from 200 to 599; {0} does not.", (int)statusCode));
            }

            // The content type is written into the header block verbatim, so a line break would end the header there and
            // let whatever follows it — a block deriving the value from a client's request — add headers of its choosing.
            if (contentType != null && !IsHeaderText(contentType))
            {
                throw new ArgumentException($"'{contentType}' is not a content type the header block can carry: it holds only printable ASCII and tabs.", nameof(contentType));
            }

            StatusCode = statusCode;
            ContentType = contentType;
            Body = body == null ? ReadOnlyMemory<byte>.Empty : (byte[])body.Clone();
            Headers = headers;
        }

        private HttpServerResponse(HttpServerResponse original, IReadOnlyList<KeyValuePair<string, string>> headers)
        {
            StatusCode = original.StatusCode;
            ContentType = original.ContentType;
            Body = original.Body;
            Headers = headers;
        }

        /// <summary>
        ///     Returns a copy of this response that also sends the header <paramref name="name" /> with <paramref name="value" />.
        /// </summary>
        /// <param name="name">
        ///     The header name: a token, and none of <c>Content-Type</c>, <c>Content-Length</c>, <c>Connection</c> and
        ///     <c>Transfer-Encoding</c> in any case.
        /// </param>
        /// <param name="value">
        ///     The header value: printable ASCII and tabs only, since it is written into the response's header block as given.
        /// </param>
        /// <returns>The new response; this one is unchanged.</returns>
        /// <exception cref="ArgumentException">Thrown when the header block cannot carry the name or the value.</exception>
        public HttpServerResponse WithHeader(string name, string value)
        {
            // The name is written into the header block verbatim, so a space or a colon in it writes a line no client parses
            // as the header meant, and a line break lets a block deriving it from a client's request add headers of the
            // client's choosing. A token is what the server itself accepts as a request header's name.
            if (name == null || !TcpHttpServerTransport.IsToken(name))
            {
                throw new ArgumentException($"'{name}' is not a header name: a name is printable ASCII with no separator.", nameof(name));
            }

            // These frame what the server writes: a second Content-Length, or a Transfer-Encoding beside the one it sends,
            // leaves a client unable to tell where the body ends, and the server closes the connection whatever a block's
            // Connection says.
            if (FramingHeaders.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                throw new ArgumentException($"'{name}' frames the response, and the server sets it itself.", nameof(name));
            }

            if (value == null || !IsHeaderText(value))
            {
                throw new ArgumentException($"'{value}' is not a header value the header block can carry: it holds only printable ASCII and tabs.", nameof(value));
            }

            return new HttpServerResponse(this, Headers.Append(new KeyValuePair<string, string>(name, value)).ToArray());
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

        private static bool IsHeaderText(string value)
        {
            return value.All(character => (character >= ' ' || character == '\t') && character <= '~');
        }
    }
}