using System;
using System.Collections.Generic;
using System.Net;

namespace Vion.Dale.Sdk.Http.Server
{
    /// <summary>
    ///     What carries requests to a hosted HTTP server and its responses back: the socket in production, an in-memory
    ///     stand-in in the test kit. The server owns the answer; the transport owns the wire.
    /// </summary>
    internal interface IHttpServerTransport : IDisposable
    {
        /// <summary>
        ///     Gets a value indicating whether the transport is accepting requests.
        /// </summary>
        bool IsListening { get; }

        /// <summary>
        ///     Starts accepting requests on the address and port, answering each through <paramref name="answer" />. A bind
        ///     failure propagates to the caller.
        /// </summary>
        void Start(IPAddress listenAddress, int port, Func<HttpServerExchange, HttpServerResponse> answer);

        /// <summary>
        ///     Stops accepting requests; a request already being answered finishes or is abandoned with its connection.
        /// </summary>
        void Stop();
    }

    /// <summary>
    ///     One request as a transport read it, before the server stamps and records it.
    /// </summary>
    internal sealed class HttpServerExchange
    {
        public string Method { get; }

        public string Path { get; }

        public string Query { get; }

        public IReadOnlyDictionary<string, string> Headers { get; }

        public byte[] Body { get; }

        public HttpServerExchange(string method, string path, string query, IReadOnlyDictionary<string, string> headers, byte[] body)
        {
            Method = method;
            Path = path;
            Query = query;
            Headers = headers;
            Body = body;
        }
    }
}