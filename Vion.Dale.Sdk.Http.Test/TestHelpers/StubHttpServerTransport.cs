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
        private Func<HttpServerExchange, HttpServerResponse>? _answer;

        public int StartCalls { get; private set; }

        public int StopCalls { get; private set; }

        public int DisposeCalls { get; private set; }

        public IPAddress? LastListenAddress { get; private set; }

        public int? LastPort { get; private set; }

        /// <summary>When set, <see cref="Start" /> throws it instead of starting, the way a port already in use does.</summary>
        public Exception? ThrowOnStart { get; set; }

        public bool IsListening
        {
            get => _answer != null;
        }

        public void Start(IPAddress listenAddress, int port, Func<HttpServerExchange, HttpServerResponse> answer)
        {
            if (ThrowOnStart != null)
            {
                throw ThrowOnStart;
            }

            StartCalls++;
            LastListenAddress = listenAddress;
            LastPort = port;
            _answer = answer;
        }

        public void Stop()
        {
            StopCalls++;
            _answer = null;
        }

        public void Dispose()
        {
            DisposeCalls++;
            _answer = null;
        }

        /// <summary>Sends one request to the server through the answer it started the transport with.</summary>
        public HttpServerResponse Send(string method, string path, string query = "", byte[]? body = null)
        {
            var answer = _answer ?? throw new InvalidOperationException("The server has not started this transport.");

            return answer(new HttpServerExchange(method, path, query, new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase), body ?? Array.Empty<byte>()));
        }
    }
}