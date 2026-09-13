using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
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
        public async Task AnswerContentTooLargeBeyondBodyCap()
        {
            // Arrange — the length is declared and no body follows: the refusal is decided on the declaration
            var declared = (TcpHttpServerTransport.BodyCap + 1).ToString(System.Globalization.CultureInfo.InvariantCulture);

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
        public async Task ServeOtherClientsAfterClientHangsUp()
        {
            // Arrange — the leaving client stops sending halfway through its request, and the server's closing its side is
            // the synchronisation point: the hang-up has been handled before the next client arrives
            _sut.Sync(snapshot => snapshot.SetResponse(HttpMethod.Get, "/a", HttpServerResponse.Json("{}")));
            using (var leaving = new TcpClient())
            {
                await leaving.ConnectAsync(IPAddress.Loopback, _port).WaitAsync(Timeout);
                await leaving.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /a HTTP/1.1\r\nHost: a"));
                leaving.Client.Shutdown(SocketShutdown.Send);
                await ReadUntilClosedAsync(leaving.GetStream()).WaitAsync(Timeout);
            }

            // Act
            var response = await ExchangeAsync("GET /a HTTP/1.1\r\n\r\n");

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 200 OK\r\n");
        }

        [TestMethod]
        [TestProperty("spec", "AC-HTTP-015.5")]
        public void FailSecondEnableOnHeldPortAndLeaveFirstServing()
        {
            // Arrange
            using var second = Compose(TcpHttpServerTransport.DefaultReadBound);
            second.ListenAddress = "127.0.0.1";
            second.Port = _port;

            // Act / Assert
            Assert.ThrowsExactly<SocketException>(() => second.IsEnabled = true);
            Assert.IsFalse(second.IsEnabled);
            Assert.IsFalse(second.IsListening);
            Assert.IsTrue(_sut.IsListening);
        }

        private static LogicBlockHttpServer Compose(TimeSpan readBound)
        {
            return new LogicBlockHttpServer(new TcpHttpServerTransport(NullLogger<TcpHttpServerTransport>.Instance, readBound), TimeProvider.System, NullLogger<LogicBlockHttpServer>.Instance);
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
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

        private async Task<string> ExchangeAsync(string request)
        {
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, _port).WaitAsync(Timeout);
            var stream = client.GetStream();
            await stream.WriteAsync(Encoding.ASCII.GetBytes(request)).AsTask().WaitAsync(Timeout);

            return await ReadUntilClosedAsync(stream).WaitAsync(Timeout);
        }
    }
}
