using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Http.TestKit
{
    /// <summary>
    ///     Drives a block's HTTP calls against answers the test scripts: the SDK's real registration, client, serializer and
    ///     error mapping run, and only the innermost message handler is replaced, so every request is recorded and held until
    ///     the test answers it. Nothing opens a socket.
    ///     <code>
    ///     var harness = new FakeHttpHarness(ctx.TimeProvider);
    ///     var sut = new MyBlock(harness.Client, logger);   // built through the TestKit context builder
    ///     sut.Poll();
    ///     Assert.AreEqual("http://device/status", harness.Requests[0].Uri.ToString());
    ///     harness.Respond(HttpStatusCode.NotFound);
    ///     ctx.FlushPendingActions();                         // the block's error callback runs
    ///     </code>
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Requests are answered oldest first, and an answer returns only once the SDK has handed the block's callback to
    ///         the block's dispatcher, so the next drive of the block's context runs it. A per-request timeout is measured on the
    ///         harness's clock: pass the context's clock and advancing it expires a held request exactly as the SDK would.
    ///         Without a clock the harness uses a virtual one nothing advances, so a held request never times out on its own.
    ///     </para>
    ///     <para>
    ///         Replacing the innermost handler takes what the platform's handler does with it: a redirect is not followed (a
    ///         scripted 3xx reaches the block as a non-success status), no cookie is kept, no body is decompressed, and a
    ///         <c>Content-Length</c> is not checked against its body. The client's own timeout is not applied to a held request.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public sealed class FakeHttpHarness : IDisposable
    {
        private readonly HeldExchanges _exchanges = new();

        private readonly ServiceProvider _serviceProvider;

        /// <summary>
        ///     Initializes a new instance of the <see cref="FakeHttpHarness" /> class on a virtual clock nothing advances.
        /// </summary>
        public FakeHttpHarness() : this(new FakeTimeProvider())
        {
        }

        /// <summary>
        ///     Initializes a new instance of the <see cref="FakeHttpHarness" /> class measuring per-request timeouts on
        ///     <paramref name="timeProvider" />.
        /// </summary>
        /// <param name="timeProvider">The clock a per-request timeout elapses on — normally the test context's.</param>
        public FakeHttpHarness(TimeProvider timeProvider)
        {
            if (timeProvider == null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            // HttpRequestExecutor is internal to its assembly, so AddDaleHttpSdk() builds the production graph and only two
            // things are replaced: the innermost handler of the package's own named client, and the executor registration,
            // with a wrapper around a real executor that keeps each call's task. The client's timeout is lifted because it is
            // a wall-clock timer inside the platform, which would fail a held request behind the test's back.
            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));

            // Registered before AddDaleHttpSdk so its TryAddSingleton(TimeProvider.System) keeps this clock.
            services.AddSingleton(timeProvider);
            services.AddDaleHttpSdk();
            services.AddHttpClient(HttpRequestExecutor.HttpClientName)
                    .ConfigurePrimaryHttpMessageHandler(() => new HoldingHttpMessageHandler(_exchanges))
                    .ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan)
                    .SetHandlerLifetime(Timeout.InfiniteTimeSpan);
            services.AddTransient<IHttpRequestExecutor>(serviceProvider => new TrackingHttpRequestExecutor(ActivatorUtilities.CreateInstance<HttpRequestExecutor>(serviceProvider),
                                                                                                           _exchanges));

            _serviceProvider = services.BuildServiceProvider();
            Client = _serviceProvider.GetRequiredService<ILogicBlockHttpClient>();
        }

        /// <summary>The fully wired client to inject into the block under test.</summary>
        public ILogicBlockHttpClient Client { get; }

        /// <summary>Every request the block issued, oldest first, answered or not.</summary>
        public IReadOnlyList<FakeHttpRequest> Requests
        {
            get => _exchanges.Requests;
        }

        /// <summary>How many requests are waiting for an answer.</summary>
        public int PendingCount
        {
            get => _exchanges.PendingCount;
        }

        /// <summary>
        ///     Answers the oldest outstanding request with status 200 and <paramref name="json" /> as its
        ///     <c>application/json</c> body.
        /// </summary>
        /// <param name="json">The response body.</param>
        public void Respond(string json)
        {
            Respond(HttpStatusCode.OK, json);
        }

        /// <summary>
        ///     Answers the oldest outstanding request with <paramref name="statusCode" /> and an optional body.
        /// </summary>
        /// <param name="statusCode">The response status.</param>
        /// <param name="body">The response body, or <c>null</c> for an empty one.</param>
        /// <param name="contentType">The content type of <paramref name="body" />.</param>
        public void Respond(HttpStatusCode statusCode, string? body = null, string contentType = "application/json")
        {
            var exchange = _exchanges.TakeOldest(nameof(Respond));
            var content = new ByteArrayContent(body == null ? Array.Empty<byte>() : Encoding.UTF8.GetBytes(body));
            if (body != null)
            {
                content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            exchange.Completion.TrySetResult(new HttpResponseMessage(statusCode) { Content = content });
            exchange.Settle();
        }

        /// <summary>
        ///     Fails the oldest outstanding request with <paramref name="exception" />, the way a transport failure arrives.
        /// </summary>
        /// <param name="exception">The exception the transport raised, delivered to the block unchanged.</param>
        public void Fail(Exception exception)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            var exchange = _exchanges.TakeOldest(nameof(Fail));
            exchange.Completion.TrySetException(exception);
            exchange.Settle();
        }

        /// <summary>
        ///     Disposes the composed services. A request still outstanding is abandoned: its callbacks never run.
        /// </summary>
        public void Dispose()
        {
            _serviceProvider.Dispose();
        }
    }
}
