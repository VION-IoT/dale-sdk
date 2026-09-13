using System;
using System.Collections.Generic;
using System.Net.Http;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.TestKit
{
    /// <summary>
    ///     A request a block issued through a <see cref="FakeHttpHarness" />, as the SDK composed it for the wire.
    /// </summary>
    [PublicApi]
    public sealed class FakeHttpRequest
    {
        /// <summary>Gets the request method.</summary>
        public HttpMethod Method { get; }

        /// <summary>Gets the absolute request URI.</summary>
        public Uri Uri { get; }

        /// <summary>
        ///     Gets the request and content headers as sent, by case-insensitive name, including the ones the SDK adds itself; a
        ///     header with several values carries them joined by <c>", "</c>.
        /// </summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>Gets the request body as UTF-8 text, or <c>null</c> when the request carried no content.</summary>
        public string? Body { get; }

        /// <summary>Gets the content type of the body, or <c>null</c> when there is none.</summary>
        public string? ContentType { get; }

        /// <summary>Gets the per-request timeout the block passed, or <c>null</c> when it passed none.</summary>
        public TimeSpan? Timeout { get; }

        internal FakeHttpRequest(HttpMethod method, Uri uri, IReadOnlyDictionary<string, string> headers, string? body, string? contentType, TimeSpan? timeout)
        {
            Method = method;
            Uri = uri;
            Headers = headers;
            Body = body;
            ContentType = contentType;
            Timeout = timeout;
        }
    }
}
