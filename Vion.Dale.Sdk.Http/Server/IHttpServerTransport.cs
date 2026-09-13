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
        ///     Starts accepting requests on the address and port, answering each through <paramref name="handler" />. A bind
        ///     failure propagates to the caller.
        /// </summary>
        void Start(IPAddress listenAddress, int port, IHttpServerExchangeHandler handler);

        /// <summary>
        ///     Stops accepting requests and closes every connection still open: a response not yet written in full is abandoned
        ///     with its connection, and its request is never reported delivered.
        /// </summary>
        void Stop();
    }

    /// <summary>
    ///     What a transport asks of the server for each request it has read.
    /// </summary>
    internal interface IHttpServerExchangeHandler
    {
        /// <summary>
        ///     Returns the response to send, stamping the request's arrival on the exchange.
        /// </summary>
        HttpServerResponse Answer(HttpServerExchange exchange);

        /// <summary>
        ///     Reports that the response <see cref="Answer" /> returned for <paramref name="exchange" /> has been written in
        ///     full. A transport calls it at most once per exchange, and never for a response it could not write.
        /// </summary>
        void Delivered(HttpServerExchange exchange);
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

        /// <summary>When the server answered the request, on its clock; set by <see cref="IHttpServerExchangeHandler.Answer" />.</summary>
        public DateTimeOffset ReceivedAt { get; set; }

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