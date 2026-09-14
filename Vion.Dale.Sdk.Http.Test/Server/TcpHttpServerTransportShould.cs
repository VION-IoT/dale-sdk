using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Http.Server;

namespace Vion.Dale.Sdk.Http.Test.Server
{
    /// <summary>
    ///     What the socket decides before a request reaches the server's route table — framing, the size caps, malformed
    ///     requests, one request per connection, the read bound — driven over real loopback sockets against the real server,
    ///     so "records nothing" is read from the server's own request log. Each wait is bounded by a class-level timeout
    ///     that only a hung server can reach; the read-bound row makes the bound's expiry itself the observable.
    /// </summary>
    [TestClass]
    public class TcpHttpServerTransportShould
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        private int _port;

        private LogicBlockHttpServer _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _port = FreePort();
            _sut = Compose(TcpHttpServerTransport.DefaultReadBound);
            _sut.ListenAddress = "127.0.0.1";
            _sut.Port = _port;
            _sut.IsEnabled = true;
        }

        [TestCleanup]
        public void Cleanup()
        {
            // Every test enables a listener on a real port; leaving it bound would hold the port for the rest of the run.
            _sut.Dispose();
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.5")]
        public async Task ServePublishedResponseToPlatformHttpClient()
        {
            // Arrange
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/app/api/id/3.json", HttpServerResponse.Json("{\"Serial\":\"SIM-0000-0003\"}")));
            using var client = new HttpClient();

            // Act
            using var response = await client.GetAsync($"http://127.0.0.1:{_port}/app/api/id/3.json").WaitAsync(Timeout);

            // Assert
            Assert.AreEqual(HttpStatusCode.OK, response.StatusCode);
            Assert.AreEqual("application/json", response.Content.Headers.ContentType?.MediaType);
            Assert.AreEqual("{\"Serial\":\"SIM-0000-0003\"}", await response.Content.ReadAsStringAsync());
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.7")]
        [TestProperty("spec", "AC-HTTP-016.8")]
        public async Task RouteOnPathAndRecordQueryAndHeadersAsSent()
        {
            // Arrange
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/app/api/id/3.json", HttpServerResponse.Json("{}")));

            // Act
            var response = await ExchangeAsync("GET /app/api/id/3.json?unit=3 HTTP/1.1\r\nHost: gateway\r\nX-Probe: a\r\nx-probe: b\r\n\r\n");

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 200 OK\r\n");
            var request = _sut.Sync(snapshot => snapshot.TakeReceivedRequests().Single());
            Assert.AreEqual("unit=3", request.Query);
            Assert.AreEqual("a, b", request.Headers["X-PROBE"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.1")]
        public async Task ReadBodyOfExactlyContentLength()
        {
            // Arrange — the declared length is shorter than what follows, so only the length decides where the body ends

            // Act
            await ExchangeAsync("POST /cmd HTTP/1.1\r\nContent-Length: 3\r\n\r\nhello");

            // Assert
            var request = _sut.Sync(snapshot => snapshot.TakeReceivedRequests().Single());
            Assert.AreEqual("hel", Encoding.UTF8.GetString(request.Body.ToArray()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.1")]
        public async Task TreatRequestWithNeitherLengthNorEncodingAsBodiless()
        {
            // Arrange — the bytes after the blank line are not a body: nothing declared one
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Post, "/cmd", HttpServerResponse.Json("{}")));

            // Act
            var response = await ExchangeAsync("POST /cmd HTTP/1.1\r\n\r\nhello");

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 200 OK\r\n");
            Assert.AreEqual(0, _sut.Sync(snapshot => snapshot.TakeReceivedRequests().Single()).Body.Length);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-016.8")]
        public async Task KeepRepeatedIdenticalContentLengthOnce()
        {
            // Arrange

            // Act
            await ExchangeAsync("POST /cmd HTTP/1.1\r\nContent-Length: 3\r\nContent-Length: 3\r\n\r\nhello");

            // Assert
            var request = _sut.Sync(snapshot => snapshot.TakeReceivedRequests().Single());
            Assert.AreEqual("3", request.Headers["Content-Length"]);
            Assert.AreEqual("hel", Encoding.UTF8.GetString(request.Body.ToArray()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.1")]
        public async Task SendHeadResponseWithLengthAndWithoutBody()
        {
            // Arrange
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Head, "/done", new HttpServerResponse(HttpStatusCode.OK, "text/plain", Encoding.UTF8.GetBytes("done"))));

            // Act
            var response = await ExchangeAsync("HEAD /done HTTP/1.1\r\n\r\n");

            // Assert
            Assert.AreEqual("HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 4\r\nConnection: close\r\n\r\n", response);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.8")]
        public async Task SendStatusWithoutNamedReasonPhraseWithEmptyOne()
        {
            // Arrange
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/rejected", HttpServerResponse.Json("{}", (HttpStatusCode)422)));
            using var client = new HttpClient();

            // Act
            var raw = await ExchangeAsync("GET /rejected HTTP/1.1\r\n\r\n");
            using var response = await client.GetAsync($"http://127.0.0.1:{_port}/rejected").WaitAsync(Timeout);

            // Assert
            StringAssert.StartsWith(raw, "HTTP/1.1 422 \r\n");
            Assert.AreEqual(422, (int)response.StatusCode);
            Assert.AreEqual(string.Empty, response.ReasonPhrase);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.1")]
        [DataRow(HttpStatusCode.OK, "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: 4\r\nConnection: close\r\n\r\ndone", DisplayName = "a status with a body")]
        [DataRow(HttpStatusCode.NoContent, "HTTP/1.1 204 No Content\r\nConnection: close\r\n\r\n", DisplayName = "a status that forbids one")]
        public async Task SendContentLengthAndNoBodyWhereStatusForbidsOne(HttpStatusCode statusCode, string expectedResponse)
        {
            // Arrange
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/done", new HttpServerResponse(statusCode, "text/plain", Encoding.UTF8.GetBytes("done"))));

            // Act
            var response = await ExchangeAsync("GET /done HTTP/1.1\r\n\r\n");

            // Assert
            Assert.AreEqual(expectedResponse, response);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.2")]
        public async Task AnswerLengthRequiredForTransferEncoding()
        {
            // Arrange

            // Act
            var response = await ExchangeAsync("POST /cmd HTTP/1.1\r\nTransfer-Encoding: chunked\r\n\r\n5\r\nhello\r\n0\r\n\r\n");

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 411 ");
            Assert.IsEmpty(_sut.Sync(snapshot => snapshot.TakeReceivedRequests()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.3")]
        [DataRow(0, DisplayName = "headers that end just past the cap")]
        [DataRow(8192, DisplayName = "headers that do not end before the read buffer is full")]
        public async Task AnswerHeaderFieldsTooLargeBeyondHeaderCap(int extraPadding)
        {
            // Arrange — the two rows reach the two places the cap is decided: once the headers' end has been read, and while
            // it is still being looked for
            var oversized = "GET /a HTTP/1.1\r\nX-Padding: " + new string('p', TcpHttpServerTransport.HeaderCap + extraPadding) + "\r\n\r\n";

            // Act
            var response = await ExchangeAsync(oversized);

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 431 ");
            Assert.IsEmpty(_sut.Sync(snapshot => snapshot.TakeReceivedRequests()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.3")]
        [DataRow(-1, DisplayName = "a head one byte under the cap")]
        [DataRow(0, DisplayName = "a head of exactly the cap")]
        public async Task ServeHeadAtOrUnderHeaderCap(int offsetFromCap)
        {
            // Arrange — the head is everything before the blank line that ends it, and its length alone decides
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));

            // Act
            var response = await ExchangeAsync(HeadOfLength(TcpHttpServerTransport.HeaderCap + offsetFromCap));

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 200 OK\r\n");
            Assert.HasCount(1, _sut.Sync(snapshot => snapshot.TakeReceivedRequests()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.3")]
        public async Task RefuseHeadOneByteOverHeaderCap()
        {
            // Arrange
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));

            // Act
            var response = await ExchangeAsync(HeadOfLength(TcpHttpServerTransport.HeaderCap + 1));

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 431 ");
            Assert.IsEmpty(_sut.Sync(snapshot => snapshot.TakeReceivedRequests()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.3")]
        [DataRow(1, DisplayName = "a length one byte over the cap")]
        [DataRow(0, DisplayName = "a length past the largest integer")]
        public async Task AnswerContentTooLargeBeyondBodyCap(int bytesOverCap)
        {
            // Arrange — the length is declared and no body follows: the refusal is decided on the declaration
            var declared = bytesOverCap > 0 ? (TcpHttpServerTransport.BodyCap + bytesOverCap).ToString(System.Globalization.CultureInfo.InvariantCulture) : "99999999999999999999";

            // Act
            var response = await ExchangeAsync($"POST /cmd HTTP/1.1\r\nContent-Length: {declared}\r\n\r\n");

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 413 ");
            Assert.IsEmpty(_sut.Sync(snapshot => snapshot.TakeReceivedRequests()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.4")]
        [DataRow("GET /a HTTP/2.0\r\n\r\n", DisplayName = "an unsupported version")]
        [DataRow("GET /a\r\n\r\n", DisplayName = "a request line without a version")]
        [DataRow("GET a HTTP/1.1\r\n\r\n", DisplayName = "a target that is neither a path nor a URL")]
        [DataRow("GET /a HTTP/1.1\r\nBad Header: x\r\n\r\n", DisplayName = "a header name with a space")]
        [DataRow("GET /a HTTP/1.1\r\nNoColon\r\n\r\n", DisplayName = "a header line without a colon")]
        [DataRow("POST /a HTTP/1.1\r\nContent-Length: ten\r\n\r\n", DisplayName = "a length that is not a number")]
        [DataRow("POST /a HTTP/1.1\r\nContent-Length: -1\r\n\r\n", DisplayName = "a negative length")]
        [DataRow("POST /a HTTP/1.1\r\nContent-Length: 3\r\nContent-Length: 4\r\n\r\nabcd", DisplayName = "two different lengths")]
        public async Task AnswerBadRequestForMalformedRequest(string request)
        {
            // Arrange

            // Act
            var response = await ExchangeAsync(request);

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 400 ");
            Assert.IsEmpty(_sut.Sync(snapshot => snapshot.TakeReceivedRequests()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.5")]
        public async Task CloseConnectionAfterOneRequest()
        {
            // Arrange — a second request is already on the wire behind the first
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));

            // Act
            var response = await ExchangeAsync("GET /a HTTP/1.1\r\n\r\nGET /a HTTP/1.1\r\n\r\n");

            // Assert — the stream ended after one response, and the server recorded one request
            Assert.AreEqual(1, response.Split("HTTP/1.1 ").Length - 1);
            StringAssert.Contains(response, "\r\nConnection: close\r\n");
            Assert.HasCount(1, _sut.Sync(snapshot => snapshot.TakeReceivedRequests()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.6")]
        public async Task CloseSilentClientOnceReadBoundElapses()
        {
            // Arrange — a server of its own with a short bound; the bound's expiry is the only thing that can end the read
            var port = FreePort();
            using var server = Compose(TimeSpan.FromMilliseconds(200));
            server.ListenAddress = "127.0.0.1";
            server.Port = port;
            server.IsEnabled = true;
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /a HTTP/1.1\r\n"));

            // Act
            var read = await ReadUntilClosedAsync(client.GetStream()).WaitAsync(Timeout);

            // Assert
            Assert.AreEqual(string.Empty, read);
            Assert.IsEmpty(server.Sync(snapshot => snapshot.TakeReceivedRequests()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.7")]
        [DataRow("POST /a HTTP/1.1\r\nHost: a", DisplayName = "halfway through its headers")]
        [DataRow("POST /a HTTP/1.1\r\nContent-Length: 10\r\n\r\nabc", DisplayName = "halfway through its body")]
        public async Task RecordNothingFromClientHangingUpMidRequestAndServeOthers(string partialRequest)
        {
            // Arrange — the server's closing its side is the synchronisation point: the hang-up has been handled before the
            // log is read and the next client arrives
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Post, "/a", HttpServerResponse.Json("{}")));
            using (var leaving = new TcpClient())
            {
                await leaving.ConnectAsync(IPAddress.Loopback, _port).WaitAsync(Timeout);
                var leavingStream = leaving.GetStream();
                await leavingStream.WriteAsync(Encoding.ASCII.GetBytes(partialRequest));
                leaving.Client.Shutdown(SocketShutdown.Send);
                await ReadUntilClosedAsync(leavingStream).WaitAsync(Timeout);
            }

            var recordedFromLeaving = _sut.Sync(snapshot => snapshot.TakeReceivedRequests());

            // Act
            var response = await ExchangeAsync("POST /a HTTP/1.1\r\nContent-Length: 0\r\n\r\n");

            // Assert
            Assert.IsEmpty(recordedFromLeaving);
            StringAssert.StartsWith(response, "HTTP/1.1 200 OK\r\n");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.9")]
        [TestProperty("spec", "AC-HTTP-016.8")]
        public async Task AbandonRequestAwaitingItsAnswerOnStopAndRecordNothing()
        {
            // Arrange — a server of its own with a short bound, composed so the test holds its transport: disabling the server
            // from another thread while a callback runs is refused, and stopping the transport is exactly what disabling does
            var port = FreePort();
            var transport = new TcpHttpServerTransport(NullLogger<TcpHttpServerTransport>.Instance, TimeSpan.FromMilliseconds(300));
            using var server = new LogicBlockHttpServer(transport, TimeProvider.System, NullLogger<LogicBlockHttpServer>.Instance);
            server.ListenAddress = "127.0.0.1";
            server.Port = port;
            server.IsEnabled = true;
            using var waiting = new TcpClient();
            Task? stopping = null;
            var awaitingAnswer = false;
            var closedByStop = false;

            // Act — a silent client that connected after the waiting one being closed by the bound shows the bound has
            // elapsed, so the waiting connection still being open shows its request was read and is waiting for the callback.
            // The stop then closes it while the callback still holds its answer back
            server.Sync(snapshot =>
                        {
                            snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}"));
                            waiting.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout).GetAwaiter().GetResult();
                            waiting.GetStream().Write(Encoding.ASCII.GetBytes("GET /a HTTP/1.1\r\n\r\n"));
                            using (var silent = new TcpClient())
                            {
                                silent.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout).GetAwaiter().GetResult();
                                ReadUntilClosedAsync(silent.GetStream()).WaitAsync(Timeout).GetAwaiter().GetResult();
                            }

                            awaitingAnswer = !IsClosedByPeer(waiting);
                            stopping = Task.Run(transport.Stop);
                            closedByStop = SpinWait.SpinUntil(() => IsClosedByPeer(waiting), Timeout);
                        });
            await stopping!.WaitAsync(Timeout);
            var received = await ReadUntilClosedAsync(waiting.GetStream()).WaitAsync(Timeout);

            // Assert
            Assert.IsTrue(awaitingAnswer, "The waiting connection was closed by its bound, so its request was never read.");
            Assert.IsTrue(closedByStop, "The stop never closed the waiting connection.");
            Assert.AreEqual(string.Empty, received);
            Assert.IsEmpty(server.Sync(snapshot => snapshot.TakeReceivedRequests()));
            Assert.IsNull(server.LastRequestAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.6")]
        public async Task LeaveReadBoundUncountedWhileRequestWaitsForSyncCallback()
        {
            // Arrange — a server of its own with a short bound
            var port = FreePort();
            using var server = Compose(TimeSpan.FromMilliseconds(300));
            server.ListenAddress = "127.0.0.1";
            server.Port = port;
            server.IsEnabled = true;
            using var waiting = new TcpClient();
            Task<string>? waitingResponse = null;

            // Act — the waiting request is complete and blocked on the callback. A silent client that connected after it
            // being closed by the bound is the proof the bound has elapsed for the waiting request as well
            server.Sync(snapshot =>
                        {
                            waiting.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout).GetAwaiter().GetResult();
                            waiting.GetStream().Write(Encoding.ASCII.GetBytes("GET /a HTTP/1.1\r\n\r\n"));
                            waitingResponse = ReadUntilClosedAsync(waiting.GetStream());
                            using var silent = new TcpClient();
                            silent.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout).GetAwaiter().GetResult();
                            ReadUntilClosedAsync(silent.GetStream()).WaitAsync(Timeout).GetAwaiter().GetResult();
                            snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}"));
                        });
            var response = await waitingResponse!.WaitAsync(Timeout);

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 200 OK\r\n");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.6")]
        public async Task CloseClientKeepingConnectionOpenOnceReadBoundElapsesAfterAnswer()
        {
            // Arrange — the client takes its response and never closes; only the bound, armed again for the response, ends it
            var port = FreePort();
            using var server = Compose(TimeSpan.FromMilliseconds(300));
            server.ListenAddress = "127.0.0.1";
            server.Port = port;
            server.IsEnabled = true;
            server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);

            // Act — the response ends where the server half-closes, which is not the close under test: only once the server has
            // closed its socket does a byte the client keeps sending come back as a reset
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /a HTTP/1.1\r\n\r\n"));
            var response = await ReadUntilClosedAsync(client.GetStream()).WaitAsync(Timeout);
            var closedByServer = SpinWait.SpinUntil(() =>
                                                    {
                                                        try
                                                        {
                                                            client.GetStream().Write(new byte[] { 0 });

                                                            return false;
                                                        }
                                                        catch (IOException)
                                                        {
                                                            return true;
                                                        }
                                                    },
                                                    Timeout);

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 200 OK\r\n");
            Assert.IsTrue(closedByServer, "The server was still reading from the client after the bound should have closed it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.9")]
        public async Task AnswerRequestOnOneConnectionWhileAnotherStillArrives()
        {
            // Arrange — a bound far past the test's own timeout, so only concurrent serving can answer the second client
            var port = FreePort();
            using var server = Compose(TimeSpan.FromSeconds(60));
            server.ListenAddress = "127.0.0.1";
            server.Port = port;
            server.IsEnabled = true;
            server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Post, "/cmd", HttpServerResponse.Json("{\"Version\":1}")));
            using var slow = new TcpClient();
            await slow.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
            await slow.GetStream().WriteAsync(Encoding.ASCII.GetBytes("POST /cmd HTTP/1.1\r\nContent-Length: 5\r\n\r\nhe"));
            var slowResponse = ReadUntilClosedAsync(slow.GetStream());

            // Act
            var fastResponse = await ExchangeAsync("POST /cmd HTTP/1.1\r\nContent-Length: 0\r\n\r\n", port);
            server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Post, "/cmd", HttpServerResponse.Json("{\"Version\":2}")));
            await slow.GetStream().WriteAsync(Encoding.ASCII.GetBytes("llo"));

            // Assert — each answered from the responses published when its own request was complete
            StringAssert.EndsWith(fastResponse, "{\"Version\":1}");
            StringAssert.EndsWith(await slowResponse.WaitAsync(Timeout), "{\"Version\":2}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-017.10")]
        public async Task AnswerServiceUnavailableToConnectionPastLimit()
        {
            // Arrange — two silent clients fill a limit of two; connections are accepted in the order they arrive, so both
            // are being served when the third is accepted
            var port = FreePort();
            using var server = Compose(TimeSpan.FromSeconds(60), 2);
            server.ListenAddress = "127.0.0.1";
            server.Port = port;
            server.IsEnabled = true;
            using var first = new TcpClient();
            using var second = new TcpClient();
            using var third = new TcpClient();
            await first.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
            await second.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);

            // Act
            await third.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
            var response = await ReadUntilClosedAsync(third.GetStream()).WaitAsync(Timeout);

            // Assert
            Assert.AreEqual("HTTP/1.1 503 Service Unavailable\r\nContent-Length: 0\r\nConnection: close\r\n\r\n", response);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.10")]
        public async Task GoOnAcceptingAfterSocketErrorAcceptingOneConnection()
        {
            // Arrange — the first accept fails with a socket error; every later one is the listener's own
            var port = FreePort();
            var accepts = 0;
            var transport = new TcpHttpServerTransport(NullLogger<TcpHttpServerTransport>.Instance,
                                                       TcpHttpServerTransport.DefaultReadBound,
                                                       TcpHttpServerTransport.DefaultConnectionLimit,
                                                       listener => Interlocked.Increment(ref accepts) == 1 ?
                                                                       Task.FromException<TcpClient>(new SocketException((int)SocketError.ConnectionReset)) :
                                                                       listener.AcceptTcpClientAsync());
            using var server = new LogicBlockHttpServer(transport, TimeProvider.System, NullLogger<LogicBlockHttpServer>.Instance);
            server.ListenAddress = "127.0.0.1";
            server.Port = port;
            server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));

            // Act
            server.IsEnabled = true;
            var response = await ExchangeAsync("GET /a HTTP/1.1\r\n\r\n", port);

            // Assert — answered without the server being cycled, by the accept after the failed one
            StringAssert.StartsWith(response, "HTTP/1.1 200 OK\r\n");
            Assert.IsTrue(server.IsListening);
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.10")]
        public async Task StopListeningWhileEnabledWhenAcceptFailsUnexpectedly()
        {
            // Arrange — the first accept fails in a way the loop does not expect; every later one is the listener's own
            var port = FreePort();
            var accepts = 0;
            var transport = new TcpHttpServerTransport(NullLogger<TcpHttpServerTransport>.Instance,
                                                       TcpHttpServerTransport.DefaultReadBound,
                                                       TcpHttpServerTransport.DefaultConnectionLimit,
                                                       listener => Interlocked.Increment(ref accepts) == 1 ?
                                                                       Task.FromException<TcpClient>(new InvalidOperationException("accept failed")) :
                                                                       listener.AcceptTcpClientAsync());
            using var server = new LogicBlockHttpServer(transport, TimeProvider.System, NullLogger<LogicBlockHttpServer>.Instance);
            server.ListenAddress = "127.0.0.1";
            server.Port = port;
            server.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));

            // Act
            server.IsEnabled = true;
            var stoppedListening = SpinWait.SpinUntil(() => !server.IsListening, Timeout);
            var enabledAfterFailure = server.IsEnabled;
            server.IsEnabled = false;
            server.IsEnabled = true;

            // Assert — the block sees the failure, and cycling the server listens again
            Assert.IsTrue(stoppedListening, "The server still reported listening after its accept loop failed.");
            Assert.IsTrue(enabledAfterFailure);
            StringAssert.StartsWith(await ExchangeAsync("GET /a HTTP/1.1\r\n\r\n", port), "HTTP/1.1 200 OK\r\n");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.5")]
        public async Task FailSecondEnableOnHeldPortAndLeaveFirstServing()
        {
            // Arrange
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));
            using var second = Compose(TcpHttpServerTransport.DefaultReadBound);
            second.ListenAddress = "127.0.0.1";
            second.Port = _port;

            // Act / Assert
            Assert.ThrowsExactly<SocketException>(() => second.IsEnabled = true);
            Assert.IsFalse(second.IsEnabled);
            Assert.IsFalse(second.IsListening);
            Assert.IsTrue(_sut.IsListening);
            StringAssert.StartsWith(await ExchangeAsync("GET /a HTTP/1.1\r\n\r\n"), "HTTP/1.1 200 OK\r\n");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.8")]
        public async Task RebindPortWhileClosedServerConnectionsStillLinger()
        {
            // Arrange — the server closes each connection first, so every exchange leaves a socket lingering on the server's
            // own port once the server is gone, and Linux refuses a bind over one unless both sockets allow address reuse
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));
            for (var exchange = 0; exchange < 3; exchange++)
            {
                await ExchangeAsync("GET /a HTTP/1.1\r\n\r\n");
            }

            _sut.Dispose();
            Assert.IsTrue(WaitForLingeringConnection(_port), "No closed connection lingers on the port, so a rebind here cannot fail.");
            using var next = Compose(TcpHttpServerTransport.DefaultReadBound);
            next.ListenAddress = "127.0.0.1";
            next.Port = _port;

            // Act
            next.IsEnabled = true;

            // Assert
            Assert.IsTrue(next.IsListening);
        }

        private static LogicBlockHttpServer Compose(TimeSpan readBound, int connectionLimit = TcpHttpServerTransport.DefaultConnectionLimit)
        {
            return new LogicBlockHttpServer(new TcpHttpServerTransport(NullLogger<TcpHttpServerTransport>.Instance,
                                                                       readBound,
                                                                       connectionLimit,
                                                                       listener => listener.AcceptTcpClientAsync()),
                                            TimeProvider.System,
                                            NullLogger<LogicBlockHttpServer>.Instance);
        }

        /// <summary>
        ///     A request whose head — its request line and headers, before the blank line — is exactly
        ///     <paramref name="length" /> bytes.
        /// </summary>
        private static string HeadOfLength(int length)
        {
            const string prefix = "GET /a HTTP/1.1\r\nX-Padding: ";

            return prefix + new string('p', length - prefix.Length) + "\r\n\r\n";
        }

        /// <summary>Whether the server has closed or reset <paramref name="client" />'s connection, with nothing left unread.</summary>
        private static bool IsClosedByPeer(TcpClient client)
        {
            return client.Client.Poll(0, SelectMode.SelectRead) && client.Client.Available == 0;
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }

        /// <summary>Whether a connection on <paramref name="port" /> reaches <c>TIME_WAIT</c> within the class timeout.</summary>
        private static bool WaitForLingeringConnection(int port)
        {
            var deadline = DateTime.UtcNow + Timeout;
            while (DateTime.UtcNow < deadline)
            {
                if (IPGlobalProperties.GetIPGlobalProperties().GetActiveTcpConnections().Any(connection => connection.LocalEndPoint.Port == port && connection.State == TcpState.TimeWait))
                {
                    return true;
                }

                Thread.Sleep(10);
            }

            return false;
        }

        private static async Task<string> ReadUntilClosedAsync(Stream stream)
        {
            using var received = new MemoryStream();
            try
            {
                await stream.CopyToAsync(received);
            }
            catch (IOException)
            {
                // A reset after the server's close still ends the stream; what was received before it is the answer.
            }

            return Encoding.ASCII.GetString(received.ToArray());
        }

        private async Task<string> ExchangeAsync(string request, int? port = null)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port ?? _port).WaitAsync(Timeout);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request)).AsTask().WaitAsync(Timeout);

            return await ReadUntilClosedAsync(stream).WaitAsync(Timeout);
        }
    }
}