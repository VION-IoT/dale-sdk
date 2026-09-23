using System;
using System.Collections.Generic;
using System.Net;
using Vion.Dale.Sdk.Http.Server;

namespace Vion.Dale.Sdk.Http.Test.TestHelpers
{
    /// <summary>
    ///     A transport with no socket, standing where the server's socket transport stands: it records how the server started
    ///     and stopped it, and hands a test the server's own answer so a request can be sent without a wire.
    /// </summary>
    internal sealed class StubHttpServerTransport : IHttpServerTransport
    {
        private IHttpServerExchangeHandler? _handler;

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public IPAddress? LastListenAddress { get; private set; }

        public int? LastPort { get; private set; }

        /// <summary>When set, <see cref="Start" /> throws it instead of starting, the way a port already in use does.</summary>
        public Exception? ThrowOnStart { get; set; }

        public bool IsListening
        {
            get => _handler != null;
        }

        /// <summary>What the transport reports as the connections it is serving; set by a test.</summary>
        public int ActiveConnections { get; set; }

        public void Start(IPAddress listenAddress, int port, IHttpServerExchangeHandler handler)
        {
            if (ThrowOnStart != null)
            {
                throw ThrowOnStart;
            }

            StartCalls++;
            LastListenAddress = listenAddress;
            LastPort = port;
            _handler = handler;
        }

        public void Stop()
        {
            StopCalls++;
            _handler = null;
        }

        public void Dispose()
        {
            DisposeCalls++;
            _handler = null;
        }

        /// <summary>Sends one request to the server through the handler it started the transport with, and delivers its response.</summary>
        public HttpServerResponse Send(string method, string path, string query = "", byte[]? body = null)
        {
            var handler = _handler ?? throw new InvalidOperationException("The server has not started this transport.");
            var exchange = new HttpServerExchange(method, path, query, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), body ?? Array.Empty<byte>());
            var response = handler.Answer(exchange);
            handler.Delivered(exchange);

            return response;
        }

        /// <summary>Reports a refusal to the server, the way the socket transport does for a request it refuses itself.</summary>
        public void Refuse(HttpStatusCode status)
        {
            (_handler ?? throw new InvalidOperationException("The server has not started this transport.")).Refused(status);
        }

        /// <summary>Reports a connection refused at the connection limit to the server.</summary>
        public void Overload()
        {
            (_handler ?? throw new InvalidOperationException("The server has not started this transport.")).Overloaded();
        }
    }
}