using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Http.Server;

namespace Vion.Dale.Sdk.Http.Test.Server
{
    /// <summary>
    ///     The exchange the transport reports to a development host's monitor for each connection it accepts: open from the
    ///     accept, closed once the request is recorded or refused.
    ///     <para>
    ///         No test here cites a criterion: they pin the premise <c>AC-HTTP-018.2</c> rests on — that the transport's
    ///         exchange outlives the recording of the request — which the stepped host cannot park, because nothing a block or
    ///         a test there can reach sits between the response being written and the request being recorded. The stepped end
    ///         of the criterion is <c>SocketExchangeSteppingShould</c> in <c>Vion.Dale.DevHost.Test</c>.
    ///     </para>
    /// </summary>
    [TestClass]
    public class TcpHttpServerTransportExchangeShould
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [TestMethod]
        public async Task KeepExchangeOpenUntilRequestRecorded()
        {
            // Arrange — the handler parks inside Delivered, after the response is written and before the request is recorded.
            var monitor = new CountingMonitor();
            var handler = new ParkingHandler();
            using var transport = new TcpHttpServerTransport(NullLogger<TcpHttpServerTransport>.Instance, TcpHttpServerTransport.DefaultReadBound, monitor);
            var port = FreePort();
            transport.Start(IPAddress.Loopback, port, handler);
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);
            await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes("GET /value HTTP/1.1\r\n\r\n")).AsTask().WaitAsync(Timeout);

            // Act
            Assert.IsTrue(handler.DeliveredEntered.Wait(Timeout));
            var openWhileRecording = monitor.Open;
            handler.Release();
            await ReadUntilClosedAsync(client.GetStream()).WaitAsync(Timeout);

            // Assert
            Assert.AreEqual(1, openWhileRecording);
            Assert.IsTrue(monitor.AllClosed.Wait(Timeout));
        }

        [TestMethod]
        public async Task CloseExchangeOfConnectionRefusedOverLimit()
        {
            // Arrange — a limit of zero refuses every connection with 503 before reading anything.
            var monitor = new CountingMonitor();
            using var transport = new TcpHttpServerTransport(NullLogger<TcpHttpServerTransport>.Instance,
                                                             TcpHttpServerTransport.DefaultReadBound,
                                                             0,
                                                             listener => listener.AcceptTcpClientAsync(),
                                                             monitor);
            var port = FreePort();
            transport.Start(IPAddress.Loopback, port, new ParkingHandler());
            using var client = new TcpClient();
            await client.ConnectAsync(IPAddress.Loopback, port).WaitAsync(Timeout);

            // Act
            var response = await ReadUntilClosedAsync(client.GetStream()).WaitAsync(Timeout);

            // Assert
            StringAssert.StartsWith(response, "HTTP/1.1 503");
            Assert.IsTrue(monitor.AllClosed.Wait(Timeout));
            Assert.AreEqual(1, monitor.Opened);
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
                // A reset after the server's close still ends the stream.
            }

            return Encoding.ASCII.GetString(received.ToArray());
        }

        private sealed class CountingMonitor : IExchangeActivityMonitor
        {
            private int _open;

            private int _opened;

            public ManualResetEventSlim AllClosed { get; } = new();

            public int Open
            {
                get => Volatile.Read(ref _open);
            }

            public int Opened
            {
                get => Volatile.Read(ref _opened);
            }

            public IDisposable OpenExchange(string description)
            {
                Interlocked.Increment(ref _opened);
                Interlocked.Increment(ref _open);
                AllClosed.Reset();

                return new Handle(this);
            }

            private sealed class Handle : IDisposable
            {
                private readonly CountingMonitor _monitor;

                private int _closed;

                public Handle(CountingMonitor monitor)
                {
                    _monitor = monitor;
                }

                public void Dispose()
                {
                    if (Interlocked.Exchange(ref _closed, 1) == 0 && Interlocked.Decrement(ref _monitor._open) == 0)
                    {
                        _monitor.AllClosed.Set();
                    }
                }
            }
        }

        private sealed class ParkingHandler : IHttpServerExchangeHandler
        {
            private readonly ManualResetEventSlim _released = new();

            public ManualResetEventSlim DeliveredEntered { get; } = new();

            public HttpServerResponse Answer(HttpServerExchange exchange)
            {
                return HttpServerResponse.Json("42");
            }

            public void Delivered(HttpServerExchange exchange)
            {
                DeliveredEntered.Set();
                _released.Wait(Timeout);
            }

            public void Release()
            {
                _released.Set();
            }
        }
    }
}