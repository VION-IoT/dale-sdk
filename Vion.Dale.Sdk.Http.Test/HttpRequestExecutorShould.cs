using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Http.Test.TestHelpers;

namespace Vion.Dale.Sdk.Http.Test
{
    /// <summary>
    ///     What a block sees once a request is on its way: which failure arrives as which exception, when a
    ///     callback runs and on what, what is disposed, and which timeout bound produced an expiry.
    ///     <para>
    ///         Every test that sends drives the real executor over a stub innermost handler and
    ///         <b>awaits</b> the returned task, so nothing here waits on the clock and no assertion races the
    ///         exchange — the refusals are answered at the caller before a request exists and are
    ///         synchronous. Callbacks are
    ///         drained from a <c>RecordingDispatcher</c> afterwards, because that is what a real block's actor
    ///         does: the self-send runs the callback after the executor has already returned.
    ///     </para>
    ///     <para>
    ///         Two limits of this seam are stated rather than worked around. A 3xx is not followed, because the
    ///         stub replaces the very handler that follows redirects — so a redirect assertion here would prove
    ///         the opposite of production, and `AC-HTTP-002.1` states the policy the package owns instead. And a
    ///         <c>Content-Length</c> that disagrees with the body is accepted here where a real
    ///         <c>SocketsHttpHandler</c> fails the read.
    ///     </para>
    /// </summary>
    [TestClass]
    public class HttpRequestExecutorShould
    {
        private const string Url = "http://vion.test/resource";

        /// <summary>
        ///     The bound on a settlement this suite expects to be immediate. It is not a race: the exchanges
        ///     it guards complete without their body ever arriving, so an implementation that waited for the
        ///     body could not finish at all, however long the bound.
        /// </summary>
        private static readonly TimeSpan SettlementTimeout = TimeSpan.FromSeconds(10);

        /// <summary>The failures the error model names, one per row of `AC-HTTP-006.1`.</summary>
        public enum Failure
        {
            NonSuccessStatus,

            RelativeUrl,

            EmptyBody,

            MalformedBody,

            NullBody,

            DisposedClient,

            TransportSocketFailure,

            TransportStreamFailure,

            TransportCancellation,

            ClientErrorStatus,

            UnfollowedRedirect,

            NonStandardStatus,

            BrokenBody,

            TransportRequestFailure,

            HandlerStatusException,

            HandlerTimeout,

            HandlerInvalidOperation,

            HandlerObjectDisposed,

            ClientConfigurationFailure,

            ClientBoundElapsed,

            // The one row that does not fail, for the families that range over every outcome.
            Answered,
        }

        /// <summary>The three executor overloads, as the rows of the families above.</summary>
        public enum Overload
        {
            ResponseContent,

            NoResponse,

            ResponseMessage,
        }

        private readonly Mock<ILogger<HttpRequestExecutor>> _loggerMock = new();

        private RecordingDispatcher _dispatcher = null!;

        /// <summary>Values beyond the edges of the band the runtime's cancellation source accepts.</summary>
        public static IEnumerable<object[]> TimeoutsOutsideBand
        {
            get =>
            [
                [HttpRequestExecutor.MaxRequestTimeout + TimeSpan.FromMilliseconds(1)],
                [TimeSpan.MaxValue],
                [TimeSpan.FromMilliseconds(-2)],
                [TimeSpan.MinValue],
            ];
        }

        /// <summary>Values at or inside those edges, the infinite sentinel included.</summary>
        public static IEnumerable<object[]> TimeoutsInsideBand
        {
            get =>
            [
                [HttpRequestExecutor.MaxRequestTimeout],
                [HttpRequestExecutor.MaxRequestTimeout - TimeSpan.FromMilliseconds(1)],
                [Timeout.InfiniteTimeSpan],
            ];
        }

        [TestInitialize]
        public void TestInitialize()
        {
            _dispatcher = new RecordingDispatcher();
        }

        // ---- the actor hop ------------------------------------------------

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-005.1")]
        [DataRow(Overload.ResponseContent)]
        [DataRow(Overload.NoResponse)]
        [DataRow(Overload.ResponseMessage)]
        public async Task ScheduleCallbackOntoDispatcherRatherThanRunItInline(Overload overload)
        {
            // Arrange
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson));
            var ranInline = false;

            // Act
            await Execute(sut, overload, () => ranInline = true);

            // Assert — the callback is queued for the block's actor and has not run yet
            Assert.IsFalse(ranInline);
            Assert.AreEqual(1, _dispatcher.QueuedCount);
            _dispatcher.Drain();
            Assert.IsTrue(ranInline);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-005.2")]
        [DataRow(HttpStatusCode.OK, DisplayName = "success path")]
        [DataRow(HttpStatusCode.BadGateway, DisplayName = "failure path")]
        public async Task RunNeitherCallbackBeforeBlockReceivedFirstMessage(HttpStatusCode statusCode)
        {
            // Arrange — the dispatcher refuses the self-send exactly as LogicBlockBase does before a block
            // has an actor; the executor catches that refusal, so the request's outcome reaches nobody
            var sut = Executor(StubHttpMessageHandler.Answering(statusCode, TestObject.PascalCaseJson));
            var unstarted = new UnstartedBlockDispatcher();
            var successRan = false;
            var errorRan = false;

            // Act
            await sut.ExecuteRequestAsync(unstarted, Url, HttpMethod.Get, _ => successRan = true, (_, _) => errorRan = true);

            // Assert
            Assert.IsFalse(successRan);
            Assert.IsFalse(errorRan);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-005.3")]
        public async Task DeliverCallbacksInOrderExchangesComplete()
        {
            // Arrange — the slow exchange is issued first and released last; nothing in the package
            // serialises the two, so the order the block sees is the order they finished
            var slowReleased = new TaskCompletionSource<bool>();
            var handler = StubHttpMessageHandler.Responding(async (request, _) =>
                                                            {
                                                                if (request.RequestUri!.AbsolutePath.EndsWith("slow", StringComparison.Ordinal))
                                                                {
                                                                    await slowReleased.Task.ConfigureAwait(false);
                                                                }

                                                                return StubHttpMessageHandler.Respond(HttpStatusCode.OK, TestObject.PascalCaseJson);
                                                            });
            var sut = Executor(handler);
            var completed = new List<string>();

            // Act
            var slow = sut.ExecuteRequestAsync(_dispatcher, "http://vion.test/slow", HttpMethod.Get, _ => completed.Add("slow"));
            await sut.ExecuteRequestAsync(_dispatcher, "http://vion.test/fast", HttpMethod.Get, _ => completed.Add("fast"));
            slowReleased.SetResult(true);
            await slow;
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual("fast,slow", string.Join(",", completed));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-005.3")]
        public async Task AcceptRequestIssuedFromCallback()
        {
            // Arrange — the chained call a block makes when one answer decides the next request
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(handler);
            Task? chained = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, _ => chained = sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Post));
            _dispatcher.Drain();
            await chained!;

            // Assert
            Assert.HasCount(2, handler.Requests);
            Assert.IsNull(_dispatcher.DrainFailure);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-005.4")]
        public async Task NotFailRequestWhenCallbackThrows()
        {
            // Arrange — an inline dispatcher, because this criterion is about the hand-over and nothing
            // else: only when the callback runs inside `InvokeSynchronized` does the package's catch see the
            // exception at all. The deferred shape `AC-HTTP-005.2` uses would leave the request finished
            // before the throw and would read green however that catch were written.
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson));
            Exception? received = null;

            // Act
            var request = sut.ExecuteRequestAsync(new InlineDispatcher(),
                                                  Url,
                                                  HttpMethod.Get,
                                                  _ => throw new InvalidOperationException("callback failed"),
                                                  (exception, _) => received = exception);
            await request;

            // Assert — awaiting without a throw is the "not to the caller" half; the request neither faulted
            // nor was reported as failed
            Assert.AreEqual(TaskStatus.RanToCompletion, request.Status);
            Assert.IsNull(received);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-005.4")]
        public async Task NotFailRequestWhenErrorCallbackThrows()
        {
            // Arrange — the sibling of the test above, on the other of `TryInvokeCallback`'s two callback
            // kinds: here the request has already failed, and a block author's own bug in the handler for
            // that failure must not turn into a second, different failure the caller has to deal with
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.BadGateway));

            // Act
            var request = sut.ExecuteRequestAsync(new InlineDispatcher(), Url, HttpMethod.Get, _ => { }, (_, _) => throw new InvalidOperationException("error callback failed"));
            await request;

            // Assert
            Assert.AreEqual(TaskStatus.RanToCompletion, request.Status);
        }

        // ---- the error model ---------------------------------------------

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-006.1")]
        [DataRow(Failure.NonSuccessStatus, typeof(HttpRequestException))]
        [DataRow(Failure.RelativeUrl, typeof(InvalidOperationException))]
        [DataRow(Failure.EmptyBody, typeof(JsonException))]
        [DataRow(Failure.MalformedBody, typeof(JsonException))]
        [DataRow(Failure.NullBody, typeof(ContentNullAfterDeserializationException))]
        [DataRow(Failure.DisposedClient, typeof(ObjectDisposedException))]
        [DataRow(Failure.TransportSocketFailure, typeof(System.Net.Sockets.SocketException))]
        [DataRow(Failure.TransportStreamFailure, typeof(IOException))]
        [DataRow(Failure.TransportCancellation, typeof(OperationCanceledException))]
        public async Task DeliverOneExceptionClassPerFailure(Failure failure, Type expectedExceptionType)
        {
            // Arrange
            var (sut, url) = ArrangeFailure(failure);
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          url,
                                          HttpMethod.Get,
                                          ReadBody,
                                          (_, _) => { },
                                          (exception, _) => received = exception);
            _dispatcher.Drain();

            // Assert — a transport failure arrives as the handler threw it: the package wraps nothing
            Assert.IsNotNull(received);
            Assert.AreEqual(expectedExceptionType, received.GetType());
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-006.2")]
        public async Task ScheduleNothingWhenRequestFailsWithoutErrorCallback()
        {
            // Arrange — the same request with a callback schedules exactly one action, which is what makes
            // this negative meaningful rather than vacuous
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.BadGateway));
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, _ => { }, (_, _) => { });
            Assert.AreEqual(1, _dispatcher.QueuedCount);
            _dispatcher.Drain();

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, _ => { });

            // Assert
            Assert.AreEqual(0, _dispatcher.QueuedCount);
        }

        // ---- the receipt -------------------------------------------------

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-019.1")]
        [DataRow(Overload.ResponseContent, HttpStatusCode.OK)]
        [DataRow(Overload.NoResponse, HttpStatusCode.OK)]
        [DataRow(Overload.ResponseMessage, HttpStatusCode.OK)]
        [DataRow(Overload.ResponseContent, HttpStatusCode.BadGateway)]
        [DataRow(Overload.NoResponse, HttpStatusCode.BadGateway)]
        [DataRow(Overload.ResponseMessage, HttpStatusCode.BadGateway)]
        public async Task HandReceiptToCallbackThatRuns(Overload overload, HttpStatusCode statusCode)
        {
            // Arrange — a 2xx reaches the success callback and a 502 the error callback, so the rows cover both kinds on
            // every overload; the status is what shows the receipt is this request's and not a default one
            var sut = Executor(StubHttpMessageHandler.Answering(statusCode, TestObject.PascalCaseJson));
            HttpReceipt? received = null;

            // Act
            await Execute(sut, overload, () => { }, _ => { }, onReceipt: receipt => received = receipt);
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(statusCode, received?.StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-019.2")]
        [DataRow(Failure.ClientErrorStatus, HttpOutcome.ClientError)]
        [DataRow(Failure.UnfollowedRedirect, HttpOutcome.ClientError)]
        [DataRow(Failure.NonSuccessStatus, HttpOutcome.ServerError)]
        [DataRow(Failure.NonStandardStatus, HttpOutcome.ServerError)]
        [DataRow(Failure.EmptyBody, HttpOutcome.ContentError)]
        [DataRow(Failure.MalformedBody, HttpOutcome.ContentError)]
        [DataRow(Failure.NullBody, HttpOutcome.ContentError)]
        [DataRow(Failure.BrokenBody, HttpOutcome.TransportError)]
        [DataRow(Failure.TransportRequestFailure, HttpOutcome.TransportError)]
        [DataRow(Failure.HandlerStatusException, HttpOutcome.TransportError)]
        [DataRow(Failure.TransportSocketFailure, HttpOutcome.TransportError)]
        [DataRow(Failure.TransportStreamFailure, HttpOutcome.TransportError)]
        [DataRow(Failure.TransportCancellation, HttpOutcome.TransportError)]
        [DataRow(Failure.HandlerTimeout, HttpOutcome.TransportError)]
        [DataRow(Failure.HandlerInvalidOperation, HttpOutcome.TransportError)]
        [DataRow(Failure.HandlerObjectDisposed, HttpOutcome.TransportError)]
        [DataRow(Failure.RelativeUrl, HttpOutcome.Invalid)]
        [DataRow(Failure.DisposedClient, HttpOutcome.Invalid)]
        [DataRow(Failure.ClientConfigurationFailure, HttpOutcome.Invalid)]
        public async Task ReportOutcomeOfFailedRequest(Failure failure, HttpOutcome expectedOutcome)
        {
            // Arrange — the handler rows throw the very classes the client refuses with, so only where the exception was
            // raised, and not its class, can tell a request that never left from one a handler failed
            var (sut, url) = ArrangeFailure(failure);
            HttpReceipt? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          url,
                                          HttpMethod.Get,
                                          ReadBody,
                                          (_, _) => { },
                                          (_, receipt) => received = receipt);
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(expectedOutcome, received?.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-019.2")]
        [DataRow(Overload.ResponseContent)]
        [DataRow(Overload.NoResponse)]
        [DataRow(Overload.ResponseMessage)]
        public async Task ReportSuccessOfAnsweredRequest(Overload overload)
        {
            // Arrange
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.Created, TestObject.PascalCaseJson));
            HttpReceipt? received = null;

            // Act
            await Execute(sut, overload, () => { }, _ => { }, onReceipt: receipt => received = receipt);
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(HttpOutcome.Success, received?.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-019.2")]
        [DataRow(0d, null, DisplayName = "per-request bound")]
        [DataRow(null, 50d, DisplayName = "client bound")]
        public async Task ReportTimeoutWhenBoundCancelsExchange(double? requestTimeoutMilliseconds, double? clientTimeoutMilliseconds)
        {
            // Arrange — the handler never answers and honours the token, so the bound is the only thing that ends the
            // exchange; a handler that throws a TimeoutException of its own is a transport error instead
            var sut = Executor(StubHttpMessageHandler.NeverCompleting(), clientTimeoutMilliseconds == null ? null : TimeSpan.FromMilliseconds(clientTimeoutMilliseconds.Value));
            HttpReceipt? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          _ => { },
                                          (_, receipt) => received = receipt,
                                          timeout: requestTimeoutMilliseconds == null ? null : TimeSpan.FromMilliseconds(requestTimeoutMilliseconds.Value));
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(HttpOutcome.Timeout, received?.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-019.2")]
        public async Task ReportInvalidWhenRequestMessageSentBefore()
        {
            // Arrange — the client refuses a message it has sent once, before any handler sees it
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK));
            var request = new HttpRequestMessage(HttpMethod.Get, Url);
            await sut.ExecuteRequestAsync(_dispatcher, request, (response, _) => response.Dispose());
            _dispatcher.Drain();
            HttpReceipt? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, request, (_, _) => { }, (_, receipt) => received = receipt);
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(HttpOutcome.Invalid, received?.Outcome);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-019.3")]
        [DataRow(Failure.ClientErrorStatus, 404)]
        [DataRow(Failure.NonSuccessStatus, 502)]
        [DataRow(Failure.MalformedBody, 200)]
        [DataRow(Failure.BrokenBody, 200)]
        [DataRow(Failure.HandlerStatusException, null)]
        [DataRow(Failure.TransportRequestFailure, null)]
        [DataRow(Failure.RelativeUrl, null)]
        public async Task CarryStatusOfJudgedResponseOnly(Failure failure, int? expectedStatus)
        {
            // Arrange — the handler-status row throws an exception carrying a 404 for a response that never arrived, which
            // on this runtime is readable off the exception; only a status read off the judged response leaves it empty
            var (sut, url) = ArrangeFailure(failure);
            HttpReceipt? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          url,
                                          HttpMethod.Get,
                                          ReadBody,
                                          (_, _) => { },
                                          (_, receipt) => received = receipt);
            _dispatcher.Drain();

            // Assert
            Assert.IsNotNull(received);
            Assert.AreEqual(expectedStatus, (int?)received.Value.StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-019.4")]
        [DataRow(Overload.ResponseContent, HttpStatusCode.OK)]
        [DataRow(Overload.NoResponse, HttpStatusCode.OK)]
        [DataRow(Overload.ResponseMessage, HttpStatusCode.OK)]
        [DataRow(Overload.ResponseContent, HttpStatusCode.BadGateway)]
        public async Task StampReceiptWhenOutcomeObserved(Overload overload, HttpStatusCode statusCode)
        {
            // Arrange — the server takes three seconds on the registered clock, and the block runs the callback a minute
            // after the outcome was observed: the receipt carries the first instant, not the second
            var clock = new FakeTimeProvider();
            var handler = StubHttpMessageHandler.Responding((_, _) =>
                                                            {
                                                                clock.Advance(TimeSpan.FromSeconds(3));

                                                                return Task.FromResult(StubHttpMessageHandler.Respond(statusCode, TestObject.PascalCaseJson));
                                                            });
            var sut = Executor(handler, clock: clock);
            HttpReceipt? received = null;

            // Act
            await Execute(sut, overload, () => { }, _ => { }, onReceipt: receipt => received = receipt);
            var observedAt = clock.GetUtcNow().UtcDateTime;
            var observedTimestamp = clock.GetTimestamp();
            clock.Advance(TimeSpan.FromMinutes(1));
            _dispatcher.Drain();

            // Assert
            Assert.IsNotNull(received);
            Assert.AreEqual(observedAt, received.Value.ReceivedAt);
            Assert.AreEqual(DateTimeKind.Utc, received.Value.ReceivedAt.Kind);
            Assert.AreEqual(observedTimestamp, received.Value.ReceivedTimestamp);
            Assert.AreEqual(TimeSpan.FromSeconds(3), received.Value.RoundTrip);
        }

        // ---- the summary -------------------------------------------------

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-005.2")]
        [DataRow(HttpStatusCode.OK, DisplayName = "success path")]
        [DataRow(HttpStatusCode.BadGateway, DisplayName = "failure path")]
        public async Task KeepOutcomeInSummaryWhenCallbackCannotBeHandedOver(HttpStatusCode statusCode)
        {
            // Arrange — the dispatcher refuses the self-send exactly as LogicBlockBase does before a block has an actor, so
            // neither callback runs and the summary is the one place outside the log the outcome still reaches
            var sut = Executor(StubHttpMessageHandler.Answering(statusCode, TestObject.PascalCaseJson));

            // Act
            await sut.ExecuteRequestAsync(new UnstartedBlockDispatcher(), Url, HttpMethod.Get, _ => { }, (_, _) => { });

            // Assert
            Assert.AreEqual(1, sut.Summary.SuccessCount + sut.Summary.ServerErrorCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.1")]
        [DataRow(true, DisplayName = "with callbacks")]
        [DataRow(false, DisplayName = "without callbacks")]
        public async Task RecordRequestBeforeCallbackRuns(bool withCallbacks)
        {
            // Arrange — the callbacks are queued and not run, so a summary updated inside them would still read empty
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.NotFound));

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, withCallbacks ? _ => { } : null, withCallbacks ? (_, _) => { } : null);

            // Assert
            Assert.AreEqual(1, sut.Summary.ClientErrorCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.2")]
        [DataRow(Failure.Answered, HttpOutcome.Success)]
        [DataRow(Failure.ClientErrorStatus, HttpOutcome.ClientError)]
        [DataRow(Failure.NonSuccessStatus, HttpOutcome.ServerError)]
        [DataRow(Failure.MalformedBody, HttpOutcome.ContentError)]
        [DataRow(Failure.ClientBoundElapsed, HttpOutcome.Timeout)]
        [DataRow(Failure.TransportRequestFailure, HttpOutcome.TransportError)]
        [DataRow(Failure.RelativeUrl, HttpOutcome.Invalid)]
        public async Task CountRequestUnderItsOutcome(Failure failure, HttpOutcome expectedOutcome)
        {
            // Arrange — two requests, so a counter that moved for the wrong outcome, or twice for one, shows in the total
            var (sut, url) = ArrangeFailure(failure);

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, url, HttpMethod.Get, ReadBody, (_, _) => { });
            await sut.ExecuteRequestAsync(_dispatcher, url, HttpMethod.Get, ReadBody, (_, _) => { });

            // Assert
            var summary = sut.Summary;
            Assert.AreEqual(2, CountOf(summary, expectedOutcome));
            Assert.AreEqual(2,
                            summary.SuccessCount + summary.ClientErrorCount + summary.ServerErrorCount + summary.ContentErrorCount + summary.TimeoutCount +
                            summary.TransportErrorCount + summary.InvalidCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.3")]
        [DataRow(Failure.ClientErrorStatus, HttpOutcome.ClientError, 404)]
        [DataRow(Failure.TransportRequestFailure, HttpOutcome.TransportError, null)]
        public async Task RecordLastFailureWithItsStatus(Failure failure, HttpOutcome expectedOutcome, int? expectedStatus)
        {
            // Arrange
            var (sut, url) = ArrangeFailure(failure);
            HttpReceipt? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          url,
                                          HttpMethod.Get,
                                          ReadBody,
                                          (_, _) => { },
                                          (_, receipt) => received = receipt);
            _dispatcher.Drain();

            // Assert
            var summary = sut.Summary;
            Assert.AreEqual(expectedOutcome, summary.LastFailureOutcome);
            Assert.AreEqual(expectedStatus, summary.LastFailureStatusCode);
            Assert.AreEqual(received?.ReceivedAt, summary.LastFailureAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.3")]
        public async Task LeaveLastFailureUnchangedBySuccess()
        {
            // Arrange — a failure, then a success on the same client
            var failing = true;
            var handler = StubHttpMessageHandler.Responding((_, _) => Task.FromResult(StubHttpMessageHandler.Respond(failing ? HttpStatusCode.BadGateway : HttpStatusCode.OK,
                                                                                                                     TestObject.PascalCaseJson)));
            var sut = Executor(handler);
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, ReadBody, (_, _) => { });
            var afterFailure = sut.Summary;
            failing = false;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, ReadBody, (_, _) => { });

            // Assert
            Assert.AreEqual(HttpOutcome.ServerError, sut.Summary.LastFailureOutcome);
            Assert.AreEqual(afterFailure.LastFailureAt, sut.Summary.LastFailureAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.3")]
        [DataRow(Failure.Answered, true)]
        [DataRow(Failure.ClientErrorStatus, true)]
        [DataRow(Failure.NonSuccessStatus, true)]
        [DataRow(Failure.BrokenBody, true)]
        [DataRow(Failure.TransportRequestFailure, false)]
        [DataRow(Failure.RelativeUrl, false)]
        public async Task RecordLastResponseOfAnyStatus(Failure failure, bool responseArrived)
        {
            // Arrange
            var (sut, url) = ArrangeFailure(failure);
            HttpReceipt? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          url,
                                          HttpMethod.Get,
                                          ReadBody,
                                          (_, receipt) => received = receipt,
                                          (_, receipt) => received = receipt);
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(responseArrived ? received?.ReceivedAt : null, sut.Summary.LastResponseAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.4")]
        [DataRow(Failure.Answered, 1)]
        [DataRow(Failure.ClientErrorStatus, 1)]
        [DataRow(Failure.NonSuccessStatus, 1)]
        [DataRow(Failure.MalformedBody, 1)]
        [DataRow(Failure.ClientBoundElapsed, 0)]
        [DataRow(Failure.TransportRequestFailure, 0)]
        [DataRow(Failure.BrokenBody, 0)]
        [DataRow(Failure.RelativeUrl, 0)]
        public async Task FeedRoundTripOnlyFromRequestsServerAnswered(Failure failure, int expectedRoundTrips)
        {
            // Arrange
            var (sut, url) = ArrangeFailure(failure);

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, url, HttpMethod.Get, ReadBody, (_, _) => { });

            // Assert
            var summary = sut.Summary;
            Assert.AreEqual(expectedRoundTrips, summary.RecentRoundTripCount);
            Assert.AreEqual(expectedRoundTrips == 1, summary.MaxRoundTrip.HasValue);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-020.7")]
        public async Task CountRequestInFlightUntilOutcomeRecorded()
        {
            // Arrange — the handler answers only when released, so the request is outstanding in between
            var released = new TaskCompletionSource<HttpResponseMessage>();
            var sut = Executor(StubHttpMessageHandler.Responding((_, _) => released.Task));
            var request = sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, _ => { });
            var whileOutstanding = sut.Summary.InFlightCount;

            // Act
            released.SetResult(StubHttpMessageHandler.Respond(HttpStatusCode.OK, null));
            await request;

            // Assert — ended before its callback has run: the callback is still queued
            Assert.AreEqual(1, whileOutstanding);
            Assert.AreEqual(0, sut.Summary.InFlightCount);
            Assert.AreEqual(1, _dispatcher.QueuedCount);
        }

        // ---- refusals at the caller ---------------------------------------

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-007.1")]
        [DataRow(Overload.ResponseContent)]
        [DataRow(Overload.NoResponse)]
        [DataRow(Overload.ResponseMessage)]
        public void RefuseRequestWithoutDispatcher(Overload overload)
        {
            // Arrange — without a dispatcher there is nowhere for either callback to run, so a request that
            // went anyway would change the server's state and report nothing back
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(handler);
            _dispatcher = null!;

            // Act / Assert
            var refusal = Assert.ThrowsExactly<ArgumentNullException>(() => Execute(sut, overload));
            Assert.AreEqual("dispatcher", refusal.ParamName);
            Assert.Contains(nameof(IHttpRequestExecutor.ExecuteRequestAsync), refusal.Message);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-007.2")]
        [DataRow(Overload.ResponseContent)]
        [DataRow(Overload.NoResponse)]
        [DataRow(Overload.ResponseMessage)]
        public void RefuseTimeoutCancellationSourceWouldReject(Overload overload)
        {
            // Arrange — the source throws from its constructor, which sits outside the executor's try, so
            // an unrefused value loses the request with no callback and nothing thrown to the caller
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(handler);

            // Act / Assert
            var refusal = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => Execute(sut, overload, timeout: TimeSpan.MaxValue));
            Assert.AreEqual("timeout", refusal.ParamName);
            Assert.Contains(nameof(IHttpRequestExecutor.ExecuteRequestAsync), refusal.Message);
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-007.2")]
        [DynamicData(nameof(TimeoutsOutsideBand))]
        public void RefuseEveryTimeoutBeyondEdgesOfBand(TimeSpan timeout)
        {
            // Arrange
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(handler);

            // Act / Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sut.ExecuteRequestAsync(_dispatcher,
                                                                                            Url,
                                                                                            HttpMethod.Get,
                                                                                            _ => { },
                                                                                            null,
                                                                                            null,
                                                                                            null,
                                                                                            timeout));
            Assert.IsEmpty(handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-007.2")]
        [DynamicData(nameof(TimeoutsInsideBand))]
        public async Task AcceptEveryTimeoutInsideBand(TimeSpan timeout)
        {
            // Arrange — the two edges the source does take, and the sentinel that means no bound at all;
            // refusing any of these would refuse a value the request could have run with
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(handler);

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          _ => { },
                                          null,
                                          null,
                                          null,
                                          timeout);

            // Assert
            Assert.HasCount(1, handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-007.2")]
        public void NameParameterMemberAndBoundsWhenRefusingTimeout()
        {
            // Arrange — a block author meeting this refusal has to learn three things from it: which
            // argument was wrong, which of their own calls to edit, and what would have been taken instead.
            // The bounds are asserted against the package's own constants, because what this criterion
            // claims is that the message names them — the value of the band itself is pinned separately,
            // against the cancellation source, by `RefuseExactlyWhatCancellationSourceRefuses`.
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson));

            // Act / Assert
            var refusal = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sut.ExecuteRequestAsync(_dispatcher,
                                                                                                          Url,
                                                                                                          HttpMethod.Get,
                                                                                                          _ => { },
                                                                                                          null,
                                                                                                          null,
                                                                                                          null,
                                                                                                          TimeSpan.MaxValue));
            Assert.AreEqual("timeout", refusal.ParamName);
            Assert.Contains(nameof(IHttpRequestExecutor.ExecuteRequestAsync), refusal.Message);
            Assert.Contains($"A request timeout is {Timeout.InfiniteTimeSpan} for no bound, or from {TimeSpan.Zero} to {HttpRequestExecutor.MaxRequestTimeout}.", refusal.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-007.2")]
        public void RefuseExactlyWhatCancellationSourceRefuses()
        {
            // Arrange — the band's edge read off the runtime rather than restated, because the families
            // above express their rows relative to the package's constant and would follow it anywhere it
            // moved. This is the one assertion that would notice.

            // Act / Assert
            using var atEdge = new CancellationTokenSource(HttpRequestExecutor.MaxRequestTimeout);
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => new CancellationTokenSource(HttpRequestExecutor.MaxRequestTimeout + TimeSpan.FromMilliseconds(1)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-003.3")]
        [DataRow(Overload.ResponseContent)]
        [DataRow(Overload.NoResponse)]
        [DataRow(Overload.ResponseMessage)]
        public async Task ScheduleNothingWithoutSuccessCallback(Overload overload)
        {
            // Arrange — the same exchange with a callback schedules exactly one action, which is what makes
            // this negative meaningful; without the guard the queued action throws inside the block's actor
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson));
            await Execute(sut, overload, () => { });
            Assert.AreEqual(1, _dispatcher.QueuedCount);
            _dispatcher.Drain();

            // Act
            await Execute(sut, overload);

            // Assert
            Assert.AreEqual(0, _dispatcher.QueuedCount);
        }

        // ---- the two timeout bounds --------------------------------------

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-008.1")]
        [DataRow(Overload.ResponseContent)]
        [DataRow(Overload.NoResponse)]
        [DataRow(Overload.ResponseMessage)]
        public async Task DeliverTimeoutExceptionWhenPerRequestBoundElapses(Overload overload)
        {
            // Arrange — the handler never answers and honours the token, so the per-request bound is the
            // only thing that can end the exchange; nothing here waits on the wall clock
            var sut = Executor(StubHttpMessageHandler.NeverCompleting());
            Exception? received = null;

            // Act
            await Execute(sut, overload, onError: exception => received = exception, timeout: TimeSpan.FromMilliseconds(50));
            _dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<TimeoutException>(received);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-008.1")]
        [DataRow("en-US")]
        [DataRow("de-DE")]
        [DataRow("fr-FR")]
        public async Task NameTimeoutInInvariantCultureWhateverMachineRunsIt(string culture)
        {
            // Arrange — the gateways this runs on are German-locale machines, where a message rendering the
            // number in the current culture reads 0,05 and no support query for 0.05 finds it. The restore
            // is in a finally because a failing assert would otherwise leave the locale set for every test
            // the assembly runs after this one.
            var previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var sut = Executor(StubHttpMessageHandler.NeverCompleting());
            Exception? received = null;

            try
            {
                // Act
                await sut.ExecuteRequestAsync(_dispatcher,
                                              Url,
                                              HttpMethod.Get,
                                              _ => { },
                                              (exception, _) => received = exception,
                                              timeout: TimeSpan.FromMilliseconds(50));
                _dispatcher.Drain();

                // Assert
                Assert.IsNotNull(received);
                Assert.AreEqual("Timed out after 0.05 seconds", received.Message);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-008.1")]
        public async Task FailRequestAtOnceOnZeroPerRequestBound()
        {
            // Arrange — a zero bound is already expired when the source is built
            var sut = Executor(StubHttpMessageHandler.NeverCompleting());
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          _ => { },
                                          (exception, _) => received = exception,
                                          timeout: TimeSpan.Zero);
            _dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<TimeoutException>(received);
            Assert.AreEqual("Timed out after 0 seconds", received.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-006.1")]
        [DataRow(HttpStatusCode.BadGateway, null, typeof(HttpRequestException), DisplayName = "a non-success status judged after the bound")]
        [DataRow(HttpStatusCode.OK, "{\"IntValue\":", typeof(JsonException), DisplayName = "a body that will not parse, read after the bound")]
        public async Task KeepExceptionClassOfFailureAfterBoundElapsed(HttpStatusCode statusCode, string? jsonBody, Type expectedExceptionType)
        {
            // Arrange — the bound has already fired by the time the failure happens: a zero timeout builds
            // an already-cancelled source, and this handler answers regardless of the token. Nothing here
            // waits, and what failed is then not the cancellation — so the class must still name the failure
            var sut = Executor(StubHttpMessageHandler.Answering(statusCode, jsonBody));
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          ReadBody,
                                          (_, _) => { },
                                          (exception, _) => received = exception,
                                          timeout: TimeSpan.Zero);
            _dispatcher.Drain();

            // Assert
            Assert.IsNotNull(received);
            Assert.AreEqual(expectedExceptionType, received.GetType());
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-008.1")]
        public async Task ApplyNoPerRequestBoundOnInfiniteTimeout()
        {
            // Arrange — the handler honours the token and answers only when the test releases it, so a bound
            // that was armed at all would have ended the exchange before the release. A handler that ignored
            // the token could not tell the two apart: it would answer either way.
            var released = new TaskCompletionSource<bool>();
            var handler = StubHttpMessageHandler.Responding(async (_, cancellationToken) =>
                                                            {
                                                                await released.Task.WaitAsync(cancellationToken).ConfigureAwait(false);

                                                                return StubHttpMessageHandler.Respond(HttpStatusCode.OK, TestObject.PascalCaseJson);
                                                            });
            var sut = Executor(handler);
            var succeeded = false;
            Exception? received = null;

            // Act
            var request = sut.ExecuteRequestAsync(_dispatcher,
                                                  Url,
                                                  HttpMethod.Get,
                                                  _ => succeeded = true,
                                                  (exception, _) => received = exception,
                                                  timeout: Timeout.InfiniteTimeSpan);
            released.SetResult(true);
            await request;
            _dispatcher.Drain();

            // Assert
            Assert.IsTrue(succeeded);
            Assert.IsNull(received);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-008.2")]
        public async Task DeliverTimeoutExceptionWhenClientBoundElapses()
        {
            // Arrange — no per-request bound, so the client's own is the only one that can end the
            // exchange, and it is the bound the message has to name
            var sut = Executor(StubHttpMessageHandler.NeverCompleting(), TimeSpan.FromMilliseconds(50));
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, _ => { }, (exception, _) => received = exception);
            _dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<TimeoutException>(received);
            Assert.AreEqual("Timed out after 0.05 seconds", received.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-008.2")]
        public async Task NameClientBoundUnderLongerPerRequestBound()
        {
            // Arrange — the per-request bound is three orders of magnitude larger than the client's, so a
            // request that ends at all ended at the client's. The number is what discriminates: a message
            // built from the per-request value would read 60 seconds for an exchange that ran 50 ms.
            var sut = Executor(StubHttpMessageHandler.NeverCompleting(), TimeSpan.FromMilliseconds(50));
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          _ => { },
                                          (exception, _) => received = exception,
                                          timeout: TimeSpan.FromMinutes(1));
            _dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<TimeoutException>(received);
            Assert.AreEqual("Timed out after 0.05 seconds", received.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-008.2")]
        [DataRow("en-US")]
        [DataRow("de-DE")]
        [DataRow("fr-FR")]
        public async Task NameClientBoundInInvariantCultureWhateverMachineRunsIt(string culture)
        {
            // Arrange — the ceiling's message is minted on its own path, so the locale discipline the
            // per-request row above pins has to be shown here too rather than inferred from the two sharing
            // a renderer today. The restore is in a finally because a failing assert would otherwise leave
            // the locale set for every test the assembly runs after this one.
            var previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var sut = Executor(StubHttpMessageHandler.NeverCompleting(), TimeSpan.FromMilliseconds(50));
            Exception? received = null;

            try
            {
                // Act
                await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, _ => { }, (exception, _) => received = exception);
                _dispatcher.Drain();

                // Assert
                Assert.IsNotNull(received);
                Assert.AreEqual("Timed out after 0.05 seconds", received.Message);
            }
            finally
            {
                CultureInfo.CurrentCulture = previousCulture;
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-008.3")]
        [DataRow(Overload.ResponseContent)]
        [DataRow(Overload.NoResponse)]
        [DataRow(Overload.ResponseMessage)]
        public void MeasurePerRequestTimeoutOnRegisteredClock(Overload overload)
        {
            // Arrange — a clock that moves only when told, and a handler that never answers: the bound can elapse only on
            // an advance of that clock, and no real time is allowed to pass for it
            var clock = new FakeTimeProvider();
            var sut = new HttpRequestExecutor(new SingleHttpClientFactory(new HttpClient(StubHttpMessageHandler.NeverCompleting())), _loggerMock.Object, clock);
            Exception? received = null;
            var exchange = Execute(sut, overload, onError: exception => received = exception, timeout: TimeSpan.FromSeconds(5));
            clock.Advance(TimeSpan.FromSeconds(4));
            var completedBeforeBound = exchange.IsCompleted;

            // Act
            clock.Advance(TimeSpan.FromSeconds(1));
            var completedOnBound = exchange.IsCompleted;
            _dispatcher.Drain();

            // Assert
            Assert.IsFalse(completedBeforeBound);
            Assert.IsTrue(completedOnBound);
            Assert.AreEqual("Timed out after 5 seconds", received?.Message);
        }

        // ---- lifetime and disposal ---------------------------------------

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-010.1")]
        public async Task JudgeStatusOnHeadersBeforeBodyArrives()
        {
            // Arrange — the body is never released, so a failure reported at all was reported on the
            // headers alone
            var gated = new GatedHttpContent(TestObject.PascalCaseJson);
            var sut = Executor(StubHttpMessageHandler.Returning(new HttpResponseMessage(HttpStatusCode.BadGateway) { Content = gated }));
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          ReadBody,
                                          (_, _) => { },
                                          (exception, _) => received = exception)
                     .WaitAsync(SettlementTimeout);
            _dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<HttpRequestException>(received);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-010.2")]
        [DataRow(Overload.ResponseContent)]
        [DataRow(Overload.NoResponse)]
        [DataRow(Overload.ResponseMessage)]
        public async Task DisposeResponseWhenRequestFails(Overload overload)
        {
            // Arrange — no callback ever receives a failed response, so nothing downstream can dispose it;
            // a block polling a failing endpoint on a timer leaks one per tick
            var response = new CountingHttpResponse(HttpStatusCode.BadGateway, "{}");
            var sut = Executor(StubHttpMessageHandler.Returning(response));

            // Act
            await Execute(sut, overload, onError: _ => { });
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(1, response.Disposals);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-010.2")]
        [DataRow(Overload.ResponseContent)]
        [DataRow(Overload.NoResponse)]
        [DataRow(Overload.ResponseMessage)]
        public async Task DisposeFailedResponseBeforeBodyEverArrives(Overload overload)
        {
            // Arrange — the failure is judged on the headers, so the leaked response would still be holding
            // a body stream nobody will ever drain; this is the shape the leak actually takes in production
            var response = new CountingHttpResponse(HttpStatusCode.BadGateway, "{}") { Content = new GatedHttpContent(TestObject.PascalCaseJson) };
            var sut = Executor(StubHttpMessageHandler.Returning(response));

            // Act
            await Execute(sut, overload, onError: _ => { }).WaitAsync(SettlementTimeout);
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(1, response.Disposals);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-010.2")]
        public async Task DisposeResponseWhenReadingBodyFails()
        {
            // Arrange — the sibling of the failed-status path: here the status was fine and the body was
            // not, so the response reached the caller's local and the failure came afterwards
            var response = new CountingHttpResponse(HttpStatusCode.OK, "{}");
            var sut = Executor(StubHttpMessageHandler.Returning(response));
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync<TestObject>(_dispatcher,
                                                      Url,
                                                      HttpMethod.Get,
                                                      _ => throw new InvalidOperationException("body unreadable"),
                                                      (_, _) => { },
                                                      (exception, _) => received = exception);
            _dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<InvalidOperationException>(received);
            Assert.AreEqual(1, response.Disposals);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-010.3")]
        public async Task DisposeResponseItCreatedForMemberCarryingResponseType()
        {
            // Arrange
            var response = new CountingHttpResponse(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(StubHttpMessageHandler.Returning(response));
            TestObject? deserialized = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, ReadBody, (value, _) => deserialized = value);
            _dispatcher.Drain();

            // Assert — the value was read out of the response before it went, so the callback loses nothing
            Assert.AreEqual(1, response.Disposals);
            Assert.AreEqual(42, deserialized?.IntValue);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-004.2")]
        public async Task LeaveSendRequestResponseAndCallerRequestUndisposed()
        {
            // Arrange — the callback receives a response whose body has not arrived; both it and the
            // caller's own request must outlive the call
            var gated = new GatedHttpContent(TestObject.PascalCaseJson);
            var response = new CountingHttpResponse(HttpStatusCode.OK, "{}") { Content = gated };
            var sut = Executor(StubHttpMessageHandler.Returning(response));
            var request = new HttpRequestMessage(HttpMethod.Post, Url) { Content = new StringContent("{\"sent\":true}") };
            string? body = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, request, (received, _) => body = ReadReleasedBody(received, gated));
            _dispatcher.Drain();

            // Assert — the callback read a body that had not arrived when it was scheduled, and the caller's
            // own request is still readable, which a disposed one would not be
            Assert.AreEqual(TestObject.PascalCaseJson, body);
            Assert.AreEqual(0, response.Disposals);
            Assert.AreEqual("{\"sent\":true}", await request.Content.ReadAsStringAsync());
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-004.1")]
        public async Task SendContentOfCallerOnAnyMethod()
        {
            // Arrange — a GET carrying a body, with the charset parameter `AC-HTTP-011.2` says the other
            // seven members cannot set. The escape hatch challenges neither: it is how a block reaches a
            // server that wants a body on a method that conventionally has none, or a content type of its own
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(handler);
            var request = new HttpRequestMessage(HttpMethod.Get, Url) { Content = new StringContent(TestObject.PascalCaseJson, Encoding.UTF8, "application/json") };

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, request);
            _dispatcher.Drain();

            // Assert
            Assert.IsNotNull(handler.LastRequest?.Content);
            Assert.AreEqual(HttpMethod.Get, handler.LastRequest.Method);
            Assert.AreEqual(TestObject.PascalCaseJson, await handler.LastRequest.Content.ReadAsStringAsync());
            Assert.AreEqual("application/json; charset=utf-8", handler.LastRequest.Content.Headers.ContentType?.ToString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-004.2")]
        public async Task DisposeSendRequestResponseWhenCallerGaveNoCallback()
        {
            // Arrange — the sibling of the ownership rule above. `SendRequest`'s success callback owns the
            // response, and the signature lets a caller pass none; the overload has no `finally` precisely
            // so that a callback's response survives, which leaves this branch with no owner at all
            var response = new CountingHttpResponse(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(StubHttpMessageHandler.Returning(response));

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, new HttpRequestMessage(HttpMethod.Get, Url));
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(1, response.Disposals);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-011.2")]
        public async Task DisposeSerializedBodyWithRequest()
        {
            // Arrange — the body belongs to the request the executor built, so it goes with it; a caller
            // holding a reference to what the serializer produced must not expect to read it afterwards
            var body = new StringContent("{\"sent\":true}");
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson));

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Post,
                                          _ => { },
                                          null,
                                          null,
                                          body);
            _dispatcher.Drain();

            // Act / Assert
            await Assert.ThrowsAsync<ObjectDisposedException>(() => body.ReadAsStringAsync());
        }

        // ---- headers ------------------------------------------------------

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-012.1")]
        public async Task AddHeadersOfCallerWithoutValidatingThem()
        {
            // Arrange
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(handler);
            var headers = new Dictionary<string, string> { { "Authorization", "Bearer token" }, { "X-Trace", "42" } };

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          _ => { },
                                          null,
                                          headers);

            // Assert
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual("Bearer token", string.Join(",", handler.LastRequest.Headers.GetValues("Authorization")));
            Assert.AreEqual("42", string.Join(",", handler.LastRequest.Headers.GetValues("X-Trace")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-012.2")]
        [DataRow("Content-Type", "application/xml", DisplayName = "a content header, which is not a request header")]
        [DataRow("Content-Length", "7", DisplayName = "a content header the transport owns")]
        [DataRow("Bad Name", "value", DisplayName = "a name the header collection rejects")]
        public async Task DropHeaderRequestRefusesAndSendAnyway(string headerName, string headerValue)
        {
            // Arrange
            var handler = StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson);
            var sut = Executor(handler);
            var headers = new Dictionary<string, string> { { headerName, headerValue }, { "X-Kept", "yes" } };
            var succeeded = false;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          _ => succeeded = true,
                                          null,
                                          headers);
            _dispatcher.Drain();

            // Assert — the request goes, without the header and without telling the caller
            Assert.IsTrue(succeeded);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual("X-Kept", string.Join(",", handler.LastRequest.Headers.Select(header => header.Key)));
        }

        // ---- fixtures -----------------------------------------------------

        private HttpRequestExecutor Executor(StubHttpMessageHandler handler, TimeSpan? clientTimeout = null, TimeProvider? clock = null)
        {
            var httpClient = new HttpClient(handler);
            if (clientTimeout.HasValue)
            {
                httpClient.Timeout = clientTimeout.Value;
            }

            return new HttpRequestExecutor(new SingleHttpClientFactory(httpClient), _loggerMock.Object, clock ?? TimeProvider.System);
        }

        private (HttpRequestExecutor Sut, string Url) ArrangeFailure(Failure failure)
        {
            switch (failure)
            {
                case Failure.NonSuccessStatus:
                    return (Executor(StubHttpMessageHandler.Answering(HttpStatusCode.BadGateway)), Url);
                case Failure.RelativeUrl:
                    return (Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson)), "/relative/path");
                case Failure.EmptyBody:
                    return (Executor(StubHttpMessageHandler.Answering(HttpStatusCode.NoContent)), Url);
                case Failure.MalformedBody:
                    return (Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, "{\"IntValue\":")), Url);
                case Failure.NullBody:
                    return (Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, "null")), Url);
                case Failure.DisposedClient:
                    var disposedClient = new HttpClient(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson));
                    disposedClient.Dispose();

                    return (new HttpRequestExecutor(new SingleHttpClientFactory(disposedClient), _loggerMock.Object, TimeProvider.System), Url);
                case Failure.TransportSocketFailure:
                    return (Executor(StubHttpMessageHandler.Throwing(new System.Net.Sockets.SocketException(10061))), Url);
                case Failure.TransportStreamFailure:
                    return (Executor(StubHttpMessageHandler.Throwing(new IOException("the connection was reset"))), Url);
                case Failure.TransportCancellation:
                    // A cancellation the handler raised, under a client bound this fixture leaves at
                    // HttpClient's own default and never comes near. It is what separates "a cancellation"
                    // from "the client's bound elapsed" — a relabel that reads only the per-request source's
                    // flag hands this to the block as a timeout, naming a bound the exchange never reached.
                    return (Executor(StubHttpMessageHandler.Throwing(new OperationCanceledException())), Url);
                case Failure.ClientErrorStatus:
                    return (Executor(StubHttpMessageHandler.Answering(HttpStatusCode.NotFound)), Url);
                case Failure.UnfollowedRedirect:
                    return (Executor(StubHttpMessageHandler.Answering(HttpStatusCode.Found)), Url);
                case Failure.NonStandardStatus:
                    return (Executor(StubHttpMessageHandler.Answering((HttpStatusCode)600)), Url);
                case Failure.BrokenBody:
                    return (Executor(StubHttpMessageHandler.Returning(new HttpResponseMessage(HttpStatusCode.OK) { Content = new BrokenHttpContent() })), Url);
                case Failure.TransportRequestFailure:
                    // The class a refused connection arrives as, so the status row beside it is told apart by the
                    // receipt and not by the exception's class.
                    return (Executor(StubHttpMessageHandler.Throwing(new HttpRequestException("No connection could be made"))), Url);
                case Failure.HandlerStatusException:
                    return (Executor(StubHttpMessageHandler.Throwing(new HttpRequestException("refused by a handler", null, HttpStatusCode.NotFound))), Url);
                case Failure.HandlerTimeout:
                    return (Executor(StubHttpMessageHandler.Throwing(new TimeoutException())), Url);
                case Failure.HandlerInvalidOperation:
                    return (Executor(StubHttpMessageHandler.Throwing(new InvalidOperationException())), Url);
                case Failure.HandlerObjectDisposed:
                    return (Executor(StubHttpMessageHandler.Throwing(new ObjectDisposedException(null))), Url);
                case Failure.ClientConfigurationFailure:
                    var provider = HttpSdk.Compose(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson),
                                                   _ => throw new InvalidOperationException("configureClient failed"));

                    return ((HttpRequestExecutor)provider.GetRequiredService<IHttpRequestExecutor>(), Url);
                case Failure.ClientBoundElapsed:
                    return (Executor(StubHttpMessageHandler.NeverCompleting(), TimeSpan.FromMilliseconds(50)), Url);
                case Failure.Answered:
                    return (Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson)), Url);
                default: throw new ArgumentOutOfRangeException(nameof(failure), failure, null);
            }
        }

        private Task Execute(HttpRequestExecutor sut,
                             Overload overload,
                             Action? onSuccess = null,
                             Action<Exception>? onError = null,
                             TimeSpan? timeout = null,
                             Action<HttpReceipt>? onReceipt = null)
        {
            // Each overload's callbacks are adapted to the same two shapes, so the families above vary in the overload and
            // in nothing else; a receipt reaches onReceipt from whichever callback ran.
            Action<HttpReceipt>? succeeded = onSuccess == null ? null : receipt =>
                                                                        {
                                                                            onSuccess();
                                                                            onReceipt?.Invoke(receipt);
                                                                        };
            Action<Exception, HttpReceipt>? failed = onError == null ? null : (exception, receipt) =>
                                                                              {
                                                                                  onError(exception);
                                                                                  onReceipt?.Invoke(receipt);
                                                                              };
            switch (overload)
            {
                case Overload.ResponseContent:
                    return sut.ExecuteRequestAsync(_dispatcher,
                                                   Url,
                                                   HttpMethod.Get,
                                                   ReadBody,
                                                   succeeded == null ? null! : (_, receipt) => succeeded(receipt),
                                                   failed,
                                                   timeout: timeout);
                case Overload.NoResponse:
                    return sut.ExecuteRequestAsync(_dispatcher,
                                                   Url,
                                                   HttpMethod.Get,
                                                   succeeded,
                                                   failed,
                                                   timeout: timeout);
                case Overload.ResponseMessage:
                    return sut.ExecuteRequestAsync(_dispatcher,
                                                   new HttpRequestMessage(HttpMethod.Get, Url),
                                                   succeeded == null ? null : (_, receipt) => succeeded(receipt),
                                                   failed,
                                                   timeout);
                default: throw new ArgumentOutOfRangeException(nameof(overload), overload, null);
            }
        }

        private static long CountOf(HttpClientSummary summary, HttpOutcome outcome)
        {
            return outcome switch
            {
                HttpOutcome.Success => summary.SuccessCount,
                HttpOutcome.ClientError => summary.ClientErrorCount,
                HttpOutcome.ServerError => summary.ServerErrorCount,
                HttpOutcome.ContentError => summary.ContentErrorCount,
                HttpOutcome.Timeout => summary.TimeoutCount,
                HttpOutcome.TransportError => summary.TransportErrorCount,
                HttpOutcome.Invalid => summary.InvalidCount,
                _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, null),
            };
        }

        /// <summary>
        ///     The delegate the client hands the executor in production, built from the real serializer, so
        ///     the exception classes a body produces are the ones a block author actually receives.
        /// </summary>
        private static Task<TestObject> ReadBody(HttpResponseMessage response)
        {
            return new HttpContentSerializer(Options.Create(new JsonSerializerOptions())).DeserializeJsonAsync<TestObject>(response.Content);
        }

        private static string ReadReleasedBody(HttpResponseMessage response, GatedHttpContent gated)
        {
            gated.Release();

            return response.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        }
    }
}