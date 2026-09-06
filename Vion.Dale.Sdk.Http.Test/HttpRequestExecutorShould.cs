using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Http.Test.TestHelpers;

namespace Vion.Dale.Sdk.Http.Test
{
    /// <summary>
    ///     What a block sees once a request is on its way: which failure arrives as which exception, when a
    ///     callback runs and on what, what is disposed, and which timeout bound produced an expiry.
    ///     <para>
    ///         Every test drives the real executor over a stub innermost handler and <b>awaits</b> the returned
    ///         task, so nothing here waits on the clock and no assertion races the exchange. Callbacks are
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
            await sut.ExecuteRequestAsync(unstarted, Url, HttpMethod.Get, () => successRan = true, _ => errorRan = true);

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
            var slow = sut.ExecuteRequestAsync(_dispatcher, "http://vion.test/slow", HttpMethod.Get, () => completed.Add("slow"));
            await sut.ExecuteRequestAsync(_dispatcher, "http://vion.test/fast", HttpMethod.Get, () => completed.Add("fast"));
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
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, () => chained = sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Post));
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
            // Arrange — the throw happens on the actor, after the executor has returned, so the package
            // never sees it; what matters is that the request itself completed cleanly
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson));

            // Act
            var request = sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, () => throw new InvalidOperationException("callback failed"));
            await request;
            _dispatcher.Drain();

            // Assert
            Assert.AreEqual(TaskStatus.RanToCompletion, request.Status);
            Assert.IsInstanceOfType<InvalidOperationException>(_dispatcher.DrainFailure);
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
                                          _ => { },
                                          exception => received = exception);
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
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, () => { }, _ => { });
            Assert.AreEqual(1, _dispatcher.QueuedCount);
            _dispatcher.Drain();

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, () => { });

            // Assert
            Assert.AreEqual(0, _dispatcher.QueuedCount);
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
                                                                                            () => { },
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
                                          () => { },
                                          null,
                                          null,
                                          null,
                                          timeout);

            // Assert
            Assert.HasCount(1, handler.Requests);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-007.2")]
        [DataRow("en-US")]
        [DataRow("de-DE")]
        public void NameBoundInInvariantCultureWhenRefusingTimeout(string culture)
        {
            // Arrange — the sibling of the timeout message: this refusal renders three durations, and a
            // German-locale gateway must read the same string a Swiss support engineer greps for
            var previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var sut = Executor(StubHttpMessageHandler.Answering(HttpStatusCode.OK, TestObject.PascalCaseJson));

            // Act / Assert
            var refusal = Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => sut.ExecuteRequestAsync(_dispatcher,
                                                                                                          Url,
                                                                                                          HttpMethod.Get,
                                                                                                          () => { },
                                                                                                          null,
                                                                                                          null,
                                                                                                          null,
                                                                                                          TimeSpan.MaxValue));
            CultureInfo.CurrentCulture = previousCulture;
            Assert.Contains("A request timeout is -00:00:00.0010000 for no bound, or from 00:00:00 to 49.17:02:47.2940000.", refusal.Message);
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
            // number in the current culture reads 0,05 and no support query for 0.05 finds it
            var previousCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo(culture);
            var sut = Executor(StubHttpMessageHandler.NeverCompleting());
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          () => { },
                                          exception => received = exception,
                                          timeout: TimeSpan.FromMilliseconds(50));
            _dispatcher.Drain();
            CultureInfo.CurrentCulture = previousCulture;

            // Assert
            Assert.IsNotNull(received);
            Assert.AreEqual("Timed out after 0.05 seconds", received.Message);
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
                                          () => { },
                                          exception => received = exception,
                                          timeout: TimeSpan.Zero);
            _dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<TimeoutException>(received);
            Assert.AreEqual("Timed out after 0 seconds", received.Message);
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
                                                  () => succeeded = true,
                                                  exception => received = exception,
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
        public async Task DeliverTaskCanceledExceptionWhenClientTimeoutElapses()
        {
            // Arrange — no per-request bound, so the client's own is the only one; the exception class is
            // the platform's cancellation rather than the package's TimeoutException
            var sut = Executor(StubHttpMessageHandler.NeverCompleting(), TimeSpan.FromMilliseconds(50));
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, () => { }, exception => received = exception);
            _dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<TaskCanceledException>(received);
            Assert.IsInstanceOfType<TimeoutException>(received.InnerException);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-008.2")]
        public async Task BoundRequestByClientTimeoutUnderLongerPerRequestTimeout()
        {
            // Arrange — the per-request bound is three orders of magnitude larger than the client's, so a
            // request that ends at all ended at the client's
            var sut = Executor(StubHttpMessageHandler.NeverCompleting(), TimeSpan.FromMilliseconds(50));
            Exception? received = null;

            // Act
            await sut.ExecuteRequestAsync(_dispatcher,
                                          Url,
                                          HttpMethod.Get,
                                          () => { },
                                          exception => received = exception,
                                          timeout: TimeSpan.FromMinutes(1));
            _dispatcher.Drain();

            // Assert
            Assert.IsInstanceOfType<TaskCanceledException>(received);
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
                                          _ => { },
                                          exception => received = exception)
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
                                                      _ => { },
                                                      exception => received = exception);
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
            await sut.ExecuteRequestAsync(_dispatcher, Url, HttpMethod.Get, ReadBody, value => deserialized = value);
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
            await sut.ExecuteRequestAsync(_dispatcher, request, received => body = ReadReleasedBody(received, gated));
            _dispatcher.Drain();

            // Assert — the callback read a body that had not arrived when it was scheduled, and the caller's
            // own request is still readable, which a disposed one would not be
            Assert.AreEqual(TestObject.PascalCaseJson, body);
            Assert.AreEqual(0, response.Disposals);
            Assert.AreEqual("{\"sent\":true}", await request.Content.ReadAsStringAsync());
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
                                          () => { },
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
                                          () => { },
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
                                          () => succeeded = true,
                                          null,
                                          headers);
            _dispatcher.Drain();

            // Assert — the request goes, without the header and without telling the caller
            Assert.IsTrue(succeeded);
            Assert.IsNotNull(handler.LastRequest);
            Assert.AreEqual("X-Kept", string.Join(",", handler.LastRequest.Headers.Select(header => header.Key)));
        }

        // ---- fixtures -----------------------------------------------------

        private HttpRequestExecutor Executor(StubHttpMessageHandler handler, TimeSpan? clientTimeout = null)
        {
            var httpClient = new HttpClient(handler);
            if (clientTimeout.HasValue)
            {
                httpClient.Timeout = clientTimeout.Value;
            }

            return new HttpRequestExecutor(new SingleHttpClientFactory(httpClient), _loggerMock.Object);
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

                    return (new HttpRequestExecutor(new SingleHttpClientFactory(disposedClient), _loggerMock.Object), Url);
                case Failure.TransportSocketFailure:
                    return (Executor(StubHttpMessageHandler.Throwing(new System.Net.Sockets.SocketException(10061))), Url);
                case Failure.TransportStreamFailure:
                    return (Executor(StubHttpMessageHandler.Throwing(new IOException("the connection was reset"))), Url);
                default: throw new ArgumentOutOfRangeException(nameof(failure), failure, null);
            }
        }

        private Task Execute(HttpRequestExecutor sut, Overload overload, Action? onSuccess = null, Action<Exception>? onError = null, TimeSpan? timeout = null)
        {
            switch (overload)
            {
                case Overload.ResponseContent:
                    return sut.ExecuteRequestAsync(_dispatcher,
                                                   Url,
                                                   HttpMethod.Get,
                                                   ReadBody,
                                                   onSuccess == null ? null! : _ => onSuccess(),
                                                   onError,
                                                   timeout: timeout);
                case Overload.NoResponse:
                    return sut.ExecuteRequestAsync(_dispatcher,
                                                   Url,
                                                   HttpMethod.Get,
                                                   onSuccess,
                                                   onError,
                                                   timeout: timeout);
                case Overload.ResponseMessage:
                    return sut.ExecuteRequestAsync(_dispatcher, new HttpRequestMessage(HttpMethod.Get, Url), onSuccess == null ? null : _ => onSuccess(), onError, timeout);
                default: throw new ArgumentOutOfRangeException(nameof(overload), overload, null);
            }
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