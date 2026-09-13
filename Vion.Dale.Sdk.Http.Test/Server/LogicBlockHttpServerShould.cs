using System;
using System.Globalization;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Http.Server;
using Vion.Dale.Sdk.Http.Test.TestHelpers;

namespace Vion.Dale.Sdk.Http.Test.Server
{
    /// <summary>
    ///     The hosted server's own rules — its configuration, its lifecycle, the route table a block publishes inside
    ///     <c>Sync</c>, and the log of what it answered — over a transport with no socket, which records how the server drove
    ///     it and carries a request straight to the server's answer. What the socket decides on its own is
    ///     <c>TcpHttpServerTransportShould</c>'s.
    /// </summary>
    [TestClass]
    public class LogicBlockHttpServerShould
    {
        private static readonly DateTimeOffset Anchor = new(2026,
                                                            3,
                                                            1,
                                                            8,
                                                            30,
                                                            0,
                                                            TimeSpan.Zero);

        private FakeTimeProvider _clock = null!;

        private LogicBlockHttpServer _sut = null!;

        private StubHttpServerTransport _transport = null!;

        [TestInitialize]
        public void Initialize()
        {
            _transport = new StubHttpServerTransport();
            _clock = new FakeTimeProvider(Anchor);
            _sut = new LogicBlockHttpServer(_transport, _clock, NullLogger<LogicBlockHttpServer>.Instance);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.2")]
        public void ListenOnAllInterfacesAndPort8080UnlessTold()
        {
            // Arrange

            // Act
            _sut.IsEnabled = true;

            // Assert
            Assert.AreEqual(IPAddress.Any, _transport.LastListenAddress);
            Assert.AreEqual(8080, _transport.LastPort);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.3")]
        public void RefuseListenAddressAndPortWhileEnabled()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.ListenAddress = "127.0.0.1");
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.Port = 8081);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.4")]
        [DataRow(null, DisplayName = "null")]
        [DataRow("", DisplayName = "empty")]
        [DataRow("   ", DisplayName = "whitespace")]
        [DataRow("gateway.local", DisplayName = "a host name")]
        public void RefuseListenAddressOtherThanIpAddress(string? listenAddress)
        {
            // Arrange

            // Act / Assert
            var refusal = Assert.ThrowsExactly<FormatException>(() => _sut.ListenAddress = listenAddress);
            Assert.AreEqual($"'{listenAddress}' is not a valid IP address.", refusal.Message);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.4")]
        [DataRow(0)]
        [DataRow(65536)]
        [DataRow(-1)]
        public void RefusePortOutsideValidRange(int port)
        {
            // Arrange
            var ambientCulture = CultureInfo.CurrentCulture;
            CultureInfo.CurrentCulture = new CultureInfo("sv-SE");
            try
            {
                // Act / Assert — sv-SE writes a negative number with a true minus sign, so a culture-rendered -1 would not read -1
                var refusal = Assert.ThrowsExactly<FormatException>(() => _sut.Port = port);
                Assert.AreEqual($"Port {port.ToString(CultureInfo.InvariantCulture)} is outside the valid range (1-65535).", refusal.Message);
            }
            finally
            {
                CultureInfo.CurrentCulture = ambientCulture;
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.5")]
        public void PropagateBindFailureAndStayDisabled()
        {
            // Arrange
            _transport.ThrowOnStart = new InvalidOperationException("address in use");

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.IsEnabled = true);
            Assert.IsFalse(_sut.IsEnabled);
            Assert.IsFalse(_sut.IsListening);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.6")]
        public void StartOnEnableAndStopOnDisableOnceEach()
        {
            // Arrange
            _sut.ListenAddress = "127.0.0.1";
            _sut.Port = 18080;

            // Act
            _sut.IsEnabled = true;
            _sut.IsEnabled = true;
            var listeningWhileEnabled = _sut.IsListening;
            _sut.IsEnabled = false;
            _sut.IsEnabled = false;

            // Assert
            Assert.AreEqual(1, _transport.StartCalls);
            Assert.AreEqual(1, _transport.StopCalls);
            Assert.AreEqual(IPAddress.Loopback, _transport.LastListenAddress);
            Assert.AreEqual(18080, _transport.LastPort);
            Assert.IsTrue(listeningWhileEnabled);
            Assert.IsFalse(_sut.IsListening);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.6")]
        public void KeepPublishedResponsesAcrossDisableAndEnable()
        {
            // Arrange
            _sut.IsEnabled = true;
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/app/api/id/3.json", HttpServerResponse.Json("{}")));

            // Act
            _sut.IsEnabled = false;
            _sut.IsEnabled = true;

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, _transport.Send("GET", "/app/api/id/3.json").StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.7")]
        public void StopAndReportDisabledOnDisposeAndStaySilentOnSecond()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act
            _sut.Dispose();
            _sut.Dispose();

            // Assert
            Assert.IsFalse(_sut.IsEnabled);
            Assert.IsFalse(_sut.IsListening);
            Assert.AreEqual(2, _transport.DisposeCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.1")]
        public void PublishInsideSyncInActionAndValueForms()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{\"A\":1}")));
            var removed = _sut.Sync(snapshot => snapshot.RemoveResponse(HttpMethod.Get, "/a"));
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/b", HttpServerResponse.Json("{\"B\":1}")));
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/b", HttpServerResponse.Json("{\"B\":2}")));

            // Assert
            Assert.IsTrue(removed);
            Assert.AreEqual(HttpStatusCode.NotFound, _transport.Send("GET", "/a").StatusCode);
            Assert.AreEqual("{\"B\":2}", Encoding.UTF8.GetString(_transport.Send("GET", "/b").Body.ToArray()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.1")]
        public void ClearEveryResponse()
        {
            // Arrange
            _sut.IsEnabled = true;
            _sut.Sync(snapshot =>
                      {
                          snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}"));
                          snapshot.SetResponse(HttpMethod.Put, "/b", HttpServerResponse.Json("{}"));
                      });

            // Act
            _sut.Sync(snapshot => snapshot.ClearResponses());

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, _transport.Send("GET", "/a").StatusCode);
            Assert.AreEqual(HttpStatusCode.NotFound, _transport.Send("PUT", "/b").StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.1")]
        public void AnswerRequestArrivingDuringSyncFromResponsesCallbackLeaves()
        {
            // Arrange
            _sut.IsEnabled = true;
            HttpServerResponse? answered = null;
            var requester = new Thread(() => answered = _transport.Send("GET", "/a"));

            // Act — the request is sent while the callback runs, and the callback publishes only once the requester is
            // blocked waiting for it; without the server's gate the requester would have answered 404 and ended
            var blockedOnCallback = false;
            _sut.Sync(snapshot =>
                      {
                          requester.Start();
                          blockedOnCallback = SpinWait.SpinUntil(() => (requester.ThreadState & ThreadState.WaitSleepJoin) != 0, TimeSpan.FromSeconds(10));
                          snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}"));
                      });
            requester.Join();

            // Assert
            Assert.IsTrue(blockedOnCallback, "The requester was never seen waiting for the callback.");
            Assert.AreEqual(HttpStatusCode.OK, answered!.StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.2")]
        public void AllowSyncWhileDisabled()
        {
            // Arrange

            // Act
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));
            _sut.IsEnabled = true;

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, _transport.Send("GET", "/a").StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.3")]
        [DataRow(1)]
        [DataRow(2)]
        public void RefuseEnableInsideSyncAtAnyDepth(int depth)
        {
            // Arrange
            void Nest(int remaining, Action innermost)
            {
                _sut.Sync(_ =>
                          {
                              if (remaining == 1)
                              {
                                  innermost();
                              }
                              else
                              {
                                  Nest(remaining - 1, innermost);
                              }
                          });
            }

            // Act / Assert
            Nest(depth, () => Assert.ThrowsExactly<InvalidOperationException>(() => _sut.IsEnabled = true));
            Assert.AreEqual(0, _transport.StartCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.3")]
        public void RefuseDisposeInsideSyncAfterNestedSyncReturned()
        {
            // Arrange — the nested call has returned, so only a depth and not a flag still knows the outer callback runs

            // Act / Assert
            _sut.Sync(_ =>
                      {
                          _sut.Sync(_ => { });
                          Assert.ThrowsExactly<InvalidOperationException>(() => _sut.Dispose());
                      });
            Assert.AreEqual(0, _transport.DisposeCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.4")]
        public void RefuseSnapshotUseAfterCallbackReturned()
        {
            // Arrange
            IHttpServerSnapshot? kept = null;
            _sut.Sync(snapshot => kept = snapshot);

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => kept!.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));
            Assert.ThrowsExactly<InvalidOperationException>(() => kept!.TakeReceivedRequests());
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.5")]
        public void AnswerWithPublishedStatusContentTypeAndBody()
        {
            // Arrange
            _sut.IsEnabled = true;
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Post,
                                                       "/app/api/command",
                                                       new HttpServerResponse(HttpStatusCode.Accepted, "text/plain", Encoding.UTF8.GetBytes("queued"))));

            // Act
            var response = _transport.Send("POST", "/app/api/command");

            // Assert
            Assert.AreEqual(HttpStatusCode.Accepted, response.StatusCode);
            Assert.AreEqual("text/plain", response.ContentType);
            Assert.AreEqual("queued", Encoding.UTF8.GetString(response.Body.ToArray()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.6")]
        [DataRow("/app/api/id/4.json", DisplayName = "a path never published")]
        [DataRow("/app/api/id/3.json", DisplayName = "a path whose only method was removed")]
        public void AnswerNotFoundForPathWithNoResponse(string path)
        {
            // Arrange
            _sut.IsEnabled = true;
            _sut.Sync(snapshot =>
                      {
                          snapshot.SetResponse(HttpMethod.Get, "/app/api/id/3.json", HttpServerResponse.Json("{}"));
                          snapshot.RemoveResponse(HttpMethod.Get, "/app/api/id/3.json");
                      });

            // Act
            var response = _transport.Send("PUT", path);

            // Assert
            Assert.AreEqual(HttpStatusCode.NotFound, response.StatusCode);
            Assert.IsEmpty(response.Headers);
            Assert.AreEqual(0, response.Body.Length);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.6")]
        public void AnswerMethodNotAllowedNamingPublishedMethods()
        {
            // Arrange
            _sut.IsEnabled = true;
            _sut.Sync(snapshot =>
                      {
                          snapshot.SetResponse(HttpMethod.Put, "/app/api/config", HttpServerResponse.Json("{}"));
                          snapshot.SetResponse(HttpMethod.Get, "/app/api/config", HttpServerResponse.Json("{}"));
                      });

            // Act
            var response = _transport.Send("DELETE", "/app/api/config");

            // Assert
            Assert.AreEqual(HttpStatusCode.MethodNotAllowed, response.StatusCode);
            Assert.AreEqual("Allow=GET, PUT", string.Join(";", response.Headers.Select(header => $"{header.Key}={header.Value}")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.7")]
        [DataRow("GET", "/app/api/id/3.json", "unit=3", HttpStatusCode.OK, DisplayName = "the path, whatever the query")]
        [DataRow("GET", "/APP/api/id/3.json", "", HttpStatusCode.NotFound, DisplayName = "the path in another case")]
        [DataRow("GET", "/app/api/id/3.json/", "", HttpStatusCode.NotFound, DisplayName = "the path with a trailing slash")]
        [DataRow("get", "/app/api/id/3.json", "", HttpStatusCode.MethodNotAllowed, DisplayName = "the method in another case")]
        public void MatchMethodAndPathOrdinallyIgnoringQuery(string method, string path, string query, HttpStatusCode expectedStatus)
        {
            // Arrange
            _sut.IsEnabled = true;
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/app/api/id/3.json", HttpServerResponse.Json("{}")));

            // Act
            var response = _transport.Send(method, path, query);

            // Assert
            Assert.AreEqual(expectedStatus, response.StatusCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.8")]
        public void HandAnsweredRequestsToBlockOnceInArrivalOrder()
        {
            // Arrange — the first request is one no response exists for, so recording is not conditional on being routed
            _sut.IsEnabled = true;
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Post, "/cmd", HttpServerResponse.Json("{}")));
            _transport.Send("GET", "/unknown", "x=1");
            _clock.Advance(TimeSpan.FromSeconds(2));
            _transport.Send("POST", "/cmd", "", Encoding.UTF8.GetBytes("{\"Reset\":true}"));

            // Act
            var taken = _sut.Sync(snapshot => snapshot.TakeReceivedRequests());
            var takenAgain = _sut.Sync(snapshot => snapshot.TakeReceivedRequests());

            // Assert
            Assert.AreEqual("GET /unknown ?x=1 @0|POST /cmd ? @2",
                            string.Join("|", taken.Select(request => $"{request.Method} {request.Path} ?{request.Query} @{(request.ReceivedAt - Anchor).TotalSeconds}")));
            Assert.AreEqual("{\"Reset\":true}", Encoding.UTF8.GetString(taken[1].Body.ToArray()));
            Assert.IsEmpty(takenAgain);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.9")]
        public void DropOldestBeyondCapacityAndCountDropsSinceLastTake()
        {
            // Arrange
            _sut.IsEnabled = true;
            for (var index = 0; index < LogicBlockHttpServer.ReceivedRequestCapacity + 3; index++)
            {
                _transport.Send("GET", "/r" + index.ToString(CultureInfo.InvariantCulture));
            }

            // Act
            var (dropped, taken, droppedAfterTake) = _sut.Sync(snapshot =>
                                                               {
                                                                   var droppedBefore = snapshot.DroppedRequestCount;
                                                                   var requests = snapshot.TakeReceivedRequests();

                                                                   return (droppedBefore, requests, snapshot.DroppedRequestCount);
                                                               });

            // Assert
            Assert.AreEqual(3, dropped);
            Assert.AreEqual(0, droppedAfterTake);
            Assert.HasCount(LogicBlockHttpServer.ReceivedRequestCapacity, taken);
            Assert.AreEqual("/r3", taken[0].Path);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.10")]
        public void ReportMostRecentArrivalAndNoneBeforeFirst()
        {
            // Arrange
            _sut.IsEnabled = true;
            var beforeAny = _sut.LastRequestAt;
            _transport.Send("GET", "/a");
            _clock.Advance(TimeSpan.FromSeconds(5));

            // Act
            _transport.Send("GET", "/b");

            // Assert
            Assert.IsNull(beforeAny);
            Assert.AreEqual(Anchor.AddSeconds(5), _sut.LastRequestAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.11")]
        [DataRow("", DisplayName = "an empty path")]
        [DataRow("app/api", DisplayName = "a path without its leading slash")]
        [DataRow("/app/api?unit=3", DisplayName = "a path carrying a query")]
        public void RefuseRouteWithUnusablePath(string path)
        {
            // Arrange

            // Act / Assert
            _sut.Sync(snapshot =>
                      {
                          Assert.AreEqual("path", Assert.Throws<ArgumentException>(() => snapshot.SetResponse(HttpMethod.Get, path, HttpServerResponse.Json("{}"))).ParamName);
                          Assert.AreEqual("path", Assert.Throws<ArgumentException>(() => snapshot.RemoveResponse(HttpMethod.Get, path)).ParamName);
                      });
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.11")]
        public void RefuseRouteWithoutMethodOrResponse()
        {
            // Arrange

            // Act / Assert
            _sut.Sync(snapshot =>
                      {
                          Assert.AreEqual("method", Assert.ThrowsExactly<ArgumentNullException>(() => snapshot.SetResponse(null!, "/a", HttpServerResponse.Json("{}"))).ParamName);
                          Assert.AreEqual("response", Assert.ThrowsExactly<ArgumentNullException>(() => snapshot.SetResponse(HttpMethod.Get, "/a", null!)).ParamName);
                      });
        }
    }
}