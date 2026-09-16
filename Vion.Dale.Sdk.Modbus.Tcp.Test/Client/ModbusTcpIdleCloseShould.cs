using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Client
{
    /// <summary>
    ///     Real-socket coverage of a peer that closes an idle connection: the whole client — the real proxy, wrapper,
    ///     request queue and diagnostics — against a Modbus TCP server on loopback whose idle reaper releases a client
    ///     connection the way a device sold with an idle timeout does. Every fake in this repo keeps its socket open,
    ///     so only a socket proves this.
    ///     <para>
    ///         Cross-tier: this tier owns the detection over a real socket and what the link reads afterwards;
    ///         <c>ModbusTcpClientWrapperShould</c> owns the reuse decision and the order the reconnect and the send
    ///         happen in.
    ///     </para>
    ///     <para>
    ///         The waits here are real, and what is asserted is a window's <b>expiry</b>: the gap is three times the
    ///         reaper's idle timeout, so load can only make the close more certain to have landed before the next
    ///         operation. Nothing here waits for something to appear.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ModbusTcpIdleCloseShould
    {
        private const int UnitIdentifier = 1;

        private const ushort StatusAddress = 0;

        private const ushort WindowAddress = 6;

        // The reaper's granularity is its own; three times the timeout is what makes the gap's expiry the assertion
        // rather than a race with it.
        private static readonly TimeSpan PeerIdleTimeout = TimeSpan.FromMilliseconds(500);

        private static readonly TimeSpan IdleGap = TimeSpan.FromMilliseconds(1500);

        private readonly TimeSpan _callbackTimeout = TimeSpan.FromSeconds(30);

        private readonly Mock<IActorDispatcher> _dispatcherMock = new();

        private ILogicBlockModbusTcpClient _sut = null!;

        private ModbusTcpServer _peer = null!;

        private ServiceProvider _serviceProvider = null!;

        private long _requestsServedByPeer;

        [TestInitialize]
        public void Initialize()
        {
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => action());

            var port = FreeLoopbackPort();
            _peer = new ModbusTcpServer { ConnectionTimeout = PeerIdleTimeout };

            // The server counts nothing of its own, and the validator is the one hook every request passes through
            // before it is answered — which is what makes "sent twice" observable from the far side of the socket.
            _peer.RequestValidator = (_, _, _, _) =>
                                     {
                                         Interlocked.Increment(ref _requestsServedByPeer);

                                         return ModbusExceptionCode.OK;
                                     };
            _peer.AddUnit(UnitIdentifier);
            _peer.Start(new IPEndPoint(IPAddress.Loopback, port));

            var services = new ServiceCollection();
            services.AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance);
            services.AddSingleton(typeof(ILogger<>), typeof(NullLogger<>));
            services.AddDaleModbusTcpSdk();
            _serviceProvider = services.BuildServiceProvider();

            _sut = _serviceProvider.GetRequiredService<ILogicBlockModbusTcpClient>();
            _sut.IpAddress = IPAddress.Loopback.ToString();
            _sut.Port = port;
            _sut.IsEnabled = true;
        }

        [TestCleanup]
        public void Cleanup()
        {
            // The client owns a socket and a queue consumer, and the server a listener: a leaked listener fails the
            // next test in this class on its own port.
            _sut.Dispose();
            _serviceProvider.Dispose();
            _peer.Stop();
            _peer.Dispose();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-008.5")]
        public async Task ServeNextPollCycleAfterPeerClosedIdleConnection()
        {
            // Arrange — the consumer's shape: a status read plus two window reads, all through the one request queue.
            await ReadPollCycleAsync();
            var afterFirstCycle = _sut.Link;

            // Act — a gap the peer's reaper outlives the connection by, then the next cycle's first read.
            await Task.Delay(IdleGap);
            var receipt = await ReadAsync(StatusAddress);

            // Assert
            Assert.AreEqual(ModbusOutcome.Success, receipt.Outcome);
            Assert.AreEqual(ModbusLinkState.Online, _sut.Link.State);
            Assert.AreEqual(afterFirstCycle.TransportErrorCount, _sut.Link.TransportErrorCount);
            Assert.AreEqual(afterFirstCycle.TimeoutCount, _sut.Link.TimeoutCount);
            Assert.AreEqual(0L, _sut.Link.TransportErrorCount);
            Assert.AreEqual(0L, _sut.Link.TimeoutCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-008.4")]
        public async Task ReadDeviceOnceAfterPeerClosedIdleConnection()
        {
            // Arrange
            await ReadPollCycleAsync();
            var requestsBefore = RequestsSeenByPeer();

            // Act
            await Task.Delay(IdleGap);
            await ReadAsync(StatusAddress);

            // Assert — the reconnect costs a connection, never a second request on the wire.
            Assert.AreEqual(requestsBefore + 1, RequestsSeenByPeer());
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-008.5")]
        public async Task ReconnectOnceWhenPeerClosedIdleConnection()
        {
            // Arrange
            await ReadPollCycleAsync();
            var connectAttemptsBefore = _sut.Connection.ConnectAttemptCount;

            // Act — the whole next cycle: only its first read finds the connection gone.
            await Task.Delay(IdleGap);
            await ReadPollCycleAsync();

            // Assert
            Assert.AreEqual(connectAttemptsBefore + 1, _sut.Connection.ConnectAttemptCount);
            Assert.AreEqual(ModbusTcpConnectionState.Connected, _sut.Connection.State);
        }

        private async Task ReadPollCycleAsync()
        {
            await ReadAsync(StatusAddress);
            await ReadAsync(WindowAddress);
            await ReadAsync((ushort)(WindowAddress + 1));
        }

        private async Task<ModbusReceipt> ReadAsync(ushort startingAddress)
        {
            var completion = new TaskCompletionSource<ModbusReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            _sut.ReadInputRegistersAsUShort(UnitIdentifier,
                                            startingAddress,
                                            1,
                                            _dispatcherMock.Object,
                                            (_, receipt) => completion.TrySetResult(receipt),
                                            (exception, receipt) => completion.TrySetException(new InvalidOperationException($"The read ended as {receipt.Outcome}.", exception)));

            return await completion.Task.WaitAsync(_callbackTimeout);
        }

        private long RequestsSeenByPeer()
        {
            return Interlocked.Read(ref _requestsServedByPeer);
        }

        private static int FreeLoopbackPort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }
    }
}
