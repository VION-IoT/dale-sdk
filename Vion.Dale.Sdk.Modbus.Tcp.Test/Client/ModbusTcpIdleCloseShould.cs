using System;
using System.Collections.Generic;
using System.Net;
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
using Vion.Dale.Sdk.Modbus.Tcp.Test.TestHelpers;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Client
{
    /// <summary>
    ///     Real-socket coverage of a peer that closes an idle connection: the whole client — the real proxy, wrapper,
    ///     request queue and diagnostics — against a Modbus TCP server on loopback whose idle reaper releases a client
    ///     connection the way a device sold with an idle timeout does. The kit's fake proxy stands in below the socket
    ///     and reports a connection the peer never closes, so no in-memory tier reaches this branch.
    ///     <para>
    ///         Cross-tier: this tier owns the detection over a real socket and what the link and the connection read
    ///         afterwards; <c>ModbusTcpClientWrapperShould</c> owns the reuse decision and the order the reconnect and
    ///         the send happen in.
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

        private ModbusTcpServer _peer = null!;

        private long _requestsServedByPeer;

        private ServiceProvider _serviceProvider = null!;

        private ILogicBlockModbusTcpClient _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _dispatcherMock.Setup(dispatcher => dispatcher.InvokeSynchronized(It.IsAny<Action>())).Callback<Action>(action => action());

            var port = LoopbackPorts.Free();
            _peer = new ModbusTcpServer { ConnectionTimeout = PeerIdleTimeout };

            // The server counts nothing of its own, and the validator is the one hook every request passes through
            // before it is answered — which is what makes a request that never arrived observable from the far side.
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
            // The client owns a socket and a queue consumer and the peer a listener; this assembly does not
            // parallelize, so anything left running here runs beside every later test in it.
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

            // Act — a gap the peer's reaper outlives the connection by, then the next cycle's first read.
            await Task.Delay(IdleGap);
            var receipt = await ReadAsync(StatusAddress);

            // Assert
            Assert.AreEqual(ModbusOutcome.Success, receipt.Outcome);
            Assert.AreEqual(ModbusLinkState.Online, _sut.Link.State);
            Assert.AreEqual(0L, _sut.Link.TransportErrorCount);
            Assert.AreEqual(0L, _sut.Link.TimeoutCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-008.4")]
        public async Task ReadDeviceOnceAfterPeerClosedIdleConnection()
        {
            // Arrange
            await ReadPollCycleAsync();
            var requestsBefore = Interlocked.Read(ref _requestsServedByPeer);

            // Act
            await Task.Delay(IdleGap);
            await ReadAsync(StatusAddress);

            // Assert — the request reaches the device exactly once. One sent into the closed connection never arrives,
            // so a read that cost its operation reads here as a count that did not move.
            Assert.AreEqual(requestsBefore + 1, Interlocked.Read(ref _requestsServedByPeer));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-008.5")]
        public async Task ReconnectOnceForWholePollCycleAfterPeerClosedIdleConnection()
        {
            // Arrange
            await ReadPollCycleAsync();
            var connectAttemptsBefore = _sut.Connection.ConnectAttemptCount;

            // Act — the whole next cycle: only its first read finds the connection gone.
            await Task.Delay(IdleGap);
            var outcomes = await ReadPollCycleAsync();

            /*
             * Assert. Both halves are needed and neither alone discriminates: reconnecting only after the first read
             * has faulted moves the count by one too, and the outcome of that first read is what tells the two apart.
             */
            CollectionAssert.AreEqual(new[] { ModbusOutcome.Success, ModbusOutcome.Success, ModbusOutcome.Success }, outcomes);
            Assert.AreEqual(connectAttemptsBefore + 1, _sut.Connection.ConnectAttemptCount);
            Assert.AreEqual(ModbusTcpConnectionState.Connected, _sut.Connection.State);
        }

        private async Task<List<ModbusOutcome>> ReadPollCycleAsync()
        {
            return
            [
                (await ReadAsync(StatusAddress)).Outcome,
                (await ReadAsync(WindowAddress)).Outcome,
                (await ReadAsync(WindowAddress + 1)).Outcome,
            ];
        }

        /*
         * Completes with the receipt whichever callback the client invokes, so a failed read is something the test's
         * own assertion reports rather than an exception thrown past it.
         */
        private async Task<ModbusReceipt> ReadAsync(ushort startingAddress)
        {
            var completion = new TaskCompletionSource<ModbusReceipt>(TaskCreationOptions.RunContinuationsAsynchronously);
            _sut.ReadInputRegistersAsUShort(UnitIdentifier,
                                            startingAddress,
                                            1,
                                            _dispatcherMock.Object,
                                            (_, receipt) => completion.TrySetResult(receipt),
                                            (_, receipt) => completion.TrySetResult(receipt));

            return await completion.Task.WaitAsync(_callbackTimeout);
        }
    }
}