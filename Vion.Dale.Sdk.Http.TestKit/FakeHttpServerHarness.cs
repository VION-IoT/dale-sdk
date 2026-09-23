using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Http.Server;

namespace Vion.Dale.Sdk.Http.TestKit
{
    /// <summary>
    ///     Hosts the SDK's real HTTP server over an in-memory transport, so the route table, the request log and the
    ///     configuration rules under test are the SDK's own — no socket, no free port.
    ///     <code>
    ///     using var harness = new FakeHttpServerHarness();
    ///     var sut = new MySimulatorBlock(harness.ServerFactory, logger);
    ///     sut.Tick();                                              // enables the server and publishes its documents
    ///     var response = harness.Client.Send(HttpMethod.Get, "/api/status.json");
    ///     Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
    ///     </code>
    /// </summary>
    /// <remarks>
    ///     The client view carries a request straight to the server, so what the socket transport decides on its own — the
    ///     framing, a <c>HEAD</c> response's missing body, the size caps, a malformed request, closing the connection, the
    ///     read bound, the connection limit, a response cut short — is not exercised through it: every request it sends is
    ///     answered and recorded. The server's own lifecycle is: a disposed server refuses to be enabled here exactly as on a
    ///     gateway.
    /// </remarks>
    [PublicApi]
    public sealed class FakeHttpServerHarness : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        /// <summary>The fully wired server to inject into the block under test, or hand out through <see cref="ServerFactory" />.</summary>
        public ILogicBlockHttpServer Server { get; }

        /// <summary>A factory handing out <see cref="Server" />, for a block that creates its server from a factory.</summary>
        public ILogicBlockHttpServerFactory ServerFactory { get; }

        /// <summary>The client-side view that sends requests to <see cref="Server" />.</summary>
        public FakeHttpServerClient Client { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FakeHttpServerHarness" /> class on a virtual clock nothing advances.
        /// </summary>
        public FakeHttpServerHarness() : this(new FakeTimeProvider())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FakeHttpServerHarness" /> class stamping requests from
        ///     <paramref name="timeProvider" />.
        /// </summary>
        /// <param name="timeProvider">The clock each request's arrival is stamped from — normally the test context's.</param>
        public FakeHttpServerHarness(TimeProvider timeProvider)
        {
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            var transport = new InMemoryHttpServerTransport();
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddSingleton(timeProvider);
            services.AddDaleHttpSdk();
            services.AddSingleton<IHttpServerTransport>(transport);

            _serviceProvider = services.BuildServiceProvider();
            Server = _serviceProvider.GetRequiredService<ILogicBlockHttpServer>();
            ServerFactory = new FixedServerFactory(Server);
            Client = new FakeHttpServerClient(transport);
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Server.Dispose();
            _serviceProvider.Dispose();
        }

        private sealed class FixedServerFactory : ILogicBlockHttpServerFactory
        {
            private readonly ILogicBlockHttpServer _server;

            public FixedServerFactory(ILogicBlockHttpServer server)
            {
                _server = server;
            }

            public ILogicBlockHttpServer Create()
            {
                return _server;
            }
        }
    }

    /// <summary>
    ///     Sends requests to a <see cref="FakeHttpServerHarness" />'s server and returns its answers, as a client on the
    ///     network
    ///     would see them.
    /// </summary>
    [PublicApi]
    public sealed class FakeHttpServerClient
    {
        private readonly InMemoryHttpServerTransport _transport;

        internal FakeHttpServerClient(InMemoryHttpServerTransport transport)
        {
            _transport = transport;
        }

        /// <summary>
        ///     Sends one request and returns the server's answer.
        /// </summary>
        /// <param name="method">The request method.</param>
        /// <param name="pathAndQuery">The request target: a path starting with <c>/</c>, optionally followed by a query.</param>
        /// <param name="body">The request body as UTF-8 text, or <c>null</c> for none.</param>
        /// <param name="headers">The request headers, or <c>null</c> for none.</param>
        /// <returns>The server's answer.</returns>
        /// <exception cref="ArgumentNullException">Thrown when <paramref name="method" /> is <c>null</c>.</exception>
        /// <exception cref="ArgumentException">Thrown when <paramref name="pathAndQuery" /> does not start with <c>/</c>.</exception>
        /// <exception cref="InvalidOperationException">Thrown when the server is not listening.</exception>
        public FakeHttpServerResponse Send(HttpMethod method, string pathAndQuery, string? body = null, IReadOnlyDictionary<string, string>? headers = null)
        {
            if (method == null)
            {
                throw new ArgumentNullException(nameof(method));
            }

            if (pathAndQuery == null || !pathAndQuery.StartsWith("/", StringComparison.Ordinal))
            {
                throw new ArgumentException($"'{pathAndQuery}' is not a request target: a target starts with '/'.", nameof(pathAndQuery));
            }

            var handler = _transport.Handler ?? throw new InvalidOperationException("The HTTP server is not listening: a block enables it before a client can reach it.");
            var queryStart = pathAndQuery.IndexOf('?');
            var exchange = new HttpServerExchange(method.Method,
                                                  queryStart < 0 ? pathAndQuery : pathAndQuery.Substring(0, queryStart),
                                                  queryStart < 0 ? string.Empty : pathAndQuery.Substring(queryStart + 1),
                                                  new Dictionary<string, string>(headers ?? new Dictionary<string, string>(), StringComparer.OrdinalIgnoreCase),
                                                  body == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body));
            var response = handler.Answer(exchange);

            // No wire can cut the response short, so every answer is delivered.
            handler.Delivered(exchange);

            return new FakeHttpServerResponse(response.StatusCode,
                                              response.ContentType,
                                              response.Headers.ToDictionary(header => header.Key, header => header.Value, StringComparer.OrdinalIgnoreCase),
                                              Encoding.UTF8.GetString(response.Body.ToArray()));
        }
    }

    /// <summary>
    ///     An answer a <see cref="FakeHttpServerHarness" />'s server gave.
    /// </summary>
    [PublicApi]
    public sealed class FakeHttpServerResponse
    {
        /// <summary>Gets the response status.</summary>
        public HttpStatusCode StatusCode { get; }

        /// <summary>Gets the content type of the body, or <c>null</c> when none was set.</summary>
        public string? ContentType { get; }

        /// <summary>Gets the headers the server added itself, such as the <c>Allow</c> of a 405, by case-insensitive name.</summary>
        public IReadOnlyDictionary<string, string> Headers { get; }

        /// <summary>Gets the response body as UTF-8 text.</summary>
        public string Body { get; }

        internal FakeHttpServerResponse(HttpStatusCode statusCode, string? contentType, IReadOnlyDictionary<string, string> headers, string body)
        {
            StatusCode = statusCode;
            ContentType = contentType;
            Headers = headers;
            Body = body;
        }
    }

    /// <summary>
    ///     A transport with no wire: it holds the server while listening, and the client view hands it each request directly.
    /// </summary>
    internal sealed class InMemoryHttpServerTransport : IHttpServerTransport
    {
        public IHttpServerExchangeHandler? Handler { get; private set; }

        public bool IsListening
        {
            get => Handler != null;
        }

        // A request is carried to the server and answered inside one call, so no connection is ever open between calls.
        public int ActiveConnections
        {
            get => 0;
        }

        public void Start(IPAddress listenAddress, int port, IHttpServerExchangeHandler handler)
        {
            Handler = handler;
        }

        public void Stop()
        {
            Handler = null;
        }

        public void Dispose()
        {
            Handler = null;
        }
    }
}