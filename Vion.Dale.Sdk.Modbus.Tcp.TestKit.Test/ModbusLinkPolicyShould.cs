using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Request;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    /// <summary>
    ///     What an unattended block sees when its device is unreachable, flapping or misconfigured: the socket closes
    ///     after a wire fault, repeated failed connects arm a backoff that requests fail fast against, and a corrected
    ///     address ends it. Driven end to end through a real <c>LogicBlockModbusTcpClient</c> on a virtual clock; only
    ///     <c>IModbusTcpClientProxy</c> and <c>IRequestQueue</c> are substituted.
    /// </summary>
    [TestClass]
    public class ModbusLinkPolicyShould
    {
        private static readonly DateTime Anchor = new(2026,
                                                      8,
                                                      18,
                                                      12,
                                                      0,
                                                      0,
                                                      DateTimeKind.Utc);

        [TestMethod]
        public void FailARequestArrivingDuringTheBackoffWithoutContactingTheDevice()
        {
            // Arrange — two failed connects arm the backoff; the first one on its own does not.
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.EnqueueConnectFailure(new ConnectionTimeoutException(3));
            harness.Proxy.EnqueueConnectFailure(new ConnectionTimeoutException(3));

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(ModbusTcpConnectionState.Disconnected,
                            harness.Client.Connection.State,
                            "One failed connect is a transient: the next request must still be allowed to try.");

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            var attemptsBeforeTheBackedOffRead = harness.Proxy.ConnectionHistory.Count;

            // Act
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert — the consumer is told what happened, and nothing reached the wire.
            Assert.AreEqual(ModbusOutcome.BackedOff, sut.LastReadReceipt!.Value.Outcome);
            Assert.IsInstanceOfType<LinkBackoffException>(sut.LastReadError);
            Assert.AreEqual(Anchor.AddSeconds(1), ((LinkBackoffException)sut.LastReadError!).NextAttemptAt);
            Assert.AreEqual(2, ((LinkBackoffException)sut.LastReadError!).ConsecutiveConnectFailures);
            Assert.AreEqual(0u, sut.Power, "A backed-off read must not deliver a value.");
            Assert.HasCount(attemptsBeforeTheBackedOffRead, harness.Proxy.ConnectionHistory, "A backed-off request must not touch the socket at all.");

            var connection = harness.Client.Connection;
            Assert.AreEqual(ModbusTcpConnectionState.BackingOff, connection.State);
            Assert.AreEqual(TimeSpan.FromSeconds(1), connection.CurrentBackoff);
            Assert.AreEqual(Anchor.AddSeconds(1), connection.NextAttemptAt);
            Assert.AreEqual(2, connection.ConnectAttemptCount, "One connect attempt per backoff period, not one per request.");

            var link = harness.Client.Link;
            Assert.AreEqual(1, link.BackedOffCount);
            Assert.AreEqual(ModbusOutcome.BackedOff, link.LastFailureOutcome);
            Assert.AreEqual(ModbusLinkState.Faulted, link.State, "A request that never reached the wire says nothing new about the link.");
        }

        [TestMethod]
        public void AttemptExactlyOneConnectOnceTheBackoffHasElapsed()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();
            ArmTheBackoff(harness, sut, ctx);

            // Act
            clock.Advance(TimeSpan.FromSeconds(1));
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert — the connect happened and succeeded, which clears the run and the backoff.
            Assert.AreEqual(ModbusOutcome.Success, sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(42u, sut.Power);

            var connection = harness.Client.Connection;
            Assert.AreEqual(ModbusTcpConnectionState.Connected, connection.State);
            Assert.AreEqual(3, connection.ConnectAttemptCount);
            Assert.AreEqual(0, connection.ConsecutiveConnectFailures);
            Assert.IsNull(connection.CurrentBackoff);
            Assert.IsNull(connection.NextAttemptAt);
        }

        [TestMethod]
        public void DoubleTheBackoffPerFurtherFailureUpToTheConfiguredMaximum()
        {
            // Arrange — a tight cap so two doublings reach it.
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Client.ConnectBackoffMax = TimeSpan.FromSeconds(2);
            for (var failure = 0; failure < 4; failure++)
            {
                harness.Proxy.EnqueueConnectFailure(new ConnectionTimeoutException(3));
            }

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            // Act / Assert — failures two, three and four, each after its predecessor's wait has elapsed.
            sut.ReadPowerOnce();
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(TimeSpan.FromSeconds(1), harness.Client.Connection.CurrentBackoff);

            clock.Advance(TimeSpan.FromSeconds(1));
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(TimeSpan.FromSeconds(2), harness.Client.Connection.CurrentBackoff);

            clock.Advance(TimeSpan.FromSeconds(2));
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(TimeSpan.FromSeconds(2), harness.Client.Connection.CurrentBackoff, "The maximum caps the doubling.");
            Assert.AreEqual(Anchor.AddSeconds(5), harness.Client.Connection.NextAttemptAt);
        }

        [TestMethod]
        public void ConnectOnTheVeryNextRequestAfterTheIpAddressIsCorrected()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();
            ArmTheBackoff(harness, sut, ctx);

            // Act
            harness.Client.IpAddress = "10.0.0.5";
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert — no clock advance was needed; the fix applies at once.
            Assert.AreEqual(ModbusOutcome.Success, sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(42u, sut.Power);
            Assert.AreEqual(ModbusTcpConnectionState.Connected, harness.Client.Connection.State);
            Assert.AreEqual("10.0.0.5", harness.Proxy.ConnectionHistory[^1].IpAddress!.ToString());
        }

        [TestMethod]
        public void KeepBackingOffWhenTheIpAddressIsSetToTheOneAlreadyInForce()
        {
            // Arrange — the first consumer re-applies its whole configuration whenever one field is edited.
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();
            ArmTheBackoff(harness, sut, ctx);
            var attemptsBeforeTheReapply = harness.Proxy.ConnectionHistory.Count;

            // Act
            harness.Client.IpAddress = "127.0.0.1";
            harness.Client.Port = 502;
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(ModbusOutcome.BackedOff, sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(ModbusTcpConnectionState.BackingOff, harness.Client.Connection.State);
            Assert.AreEqual(2, harness.Client.Connection.ConsecutiveConnectFailures, "Re-setting a value already in force must not reset the run.");
            Assert.HasCount(attemptsBeforeTheReapply, harness.Proxy.ConnectionHistory);
        }

        [TestMethod]
        public void ConnectOnTheVeryNextRequestAfterTheClientIsReEnabled()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();
            ArmTheBackoff(harness, sut, ctx);

            // Act
            harness.Client.IsEnabled = false;
            harness.Client.IsEnabled = true;
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(ModbusOutcome.Success, sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(42u, sut.Power);
            Assert.AreEqual(0, harness.Client.Connection.ConsecutiveConnectFailures);
        }

        [TestMethod]
        public void CloseTheSocketAfterATimeoutAndReconnectOnTheNextRequestWithoutBackingOff()
        {
            // Arrange — a peer that silently dropped: the connection is established, then a read times out.
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(ModbusTcpConnectionState.Connected, harness.Client.Connection.State);

            harness.Proxy.EnqueueReadFault(1, 40000, new OperationTimeoutException());

            // Act — the timeout, then the read after it.
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert — the half-open socket was released at once.
            Assert.AreEqual(ModbusOutcome.Timeout, sut.LastReadReceipt!.Value.Outcome);
            Assert.IsFalse(harness.Proxy.IsConnected, "A wire fault closes the socket eagerly, so Connection.State stays truthful.");
            Assert.AreEqual(ModbusTcpConnectionState.Disconnected, harness.Client.Connection.State);
            Assert.AreEqual(ConnectionEventKind.Disconnect, harness.Proxy.ConnectionHistory[^1].Kind);

            // Act
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert — reached again with no operator action, and a single timeout never arms a backoff.
            Assert.AreEqual(ModbusOutcome.Success, sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(42u, sut.Power);
            Assert.AreEqual(2, harness.Client.Connection.ConnectAttemptCount);
            Assert.AreEqual(0, harness.Client.Connection.ConsecutiveConnectFailures, "An operation timeout is not a failed connect.");
            Assert.AreEqual(0, harness.Client.Link.BackedOffCount);
        }

        [TestMethod]
        public void RunADisconnectWhileBackingOff()
        {
            // Arrange — a control operation never connects, so the backoff has nothing to say about it.
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();
            ArmTheBackoff(harness, sut, ctx);

            var disconnected = false;
            Exception? disconnectError = null;

            // Act
            harness.Client.Disconnect(sut, () => disconnected = true, exception => disconnectError = exception);
            ctx.FlushPendingActions();

            // Assert
            Assert.IsTrue(disconnected);
            Assert.IsNull(disconnectError);
            Assert.AreEqual(ModbusTcpConnectionState.BackingOff, harness.Client.Connection.State, "A disconnect must not end the backoff.");
        }

        [TestMethod]
        public void ReportBackingOffOnlyWhileTheWaitIsStillRunning()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();
            ArmTheBackoff(harness, sut, ctx);

            // Act
            clock.Advance(TimeSpan.FromSeconds(1));

            // Assert — the socket is closed and the next request will connect, so BackingOff would be a lie; the two
            // fields stay filled because that attempt has not happened yet.
            var connection = harness.Client.Connection;
            Assert.AreEqual(ModbusTcpConnectionState.Disconnected, connection.State);
            Assert.AreEqual(TimeSpan.FromSeconds(1), connection.CurrentBackoff);
            Assert.AreEqual(Anchor.AddSeconds(1), connection.NextAttemptAt);
        }

        [TestMethod]
        public void RejectABackoffThatIsNotPositiveOrExceedsItsMaximum()
        {
            // Arrange
            using var harness = CreateHarness(new FakeTimeProvider(Anchor));

            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => harness.Client.ConnectBackoff = TimeSpan.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => harness.Client.ConnectBackoff = TimeSpan.FromSeconds(-1));
            Assert.Throws<ArgumentOutOfRangeException>(() => harness.Client.ConnectBackoff = TimeSpan.FromSeconds(31));
            Assert.Throws<ArgumentOutOfRangeException>(() => harness.Client.ConnectBackoffMax = TimeSpan.FromMilliseconds(999));
            Assert.AreEqual(TimeSpan.FromSeconds(1), harness.Client.ConnectBackoff, "A rejected value must not replace the one in force.");
            Assert.AreEqual(TimeSpan.FromSeconds(30), harness.Client.ConnectBackoffMax);
        }

        [TestMethod]
        public void AcceptAConstantBackoffWhenBothKnobsAreTheSame()
        {
            // Arrange — the smallest constant wait a consumer can configure; there is no way to switch it off.
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Client.ConnectBackoff = TimeSpan.FromMilliseconds(200);
            harness.Client.ConnectBackoffMax = TimeSpan.FromMilliseconds(200);
            for (var failure = 0; failure < 3; failure++)
            {
                harness.Proxy.EnqueueConnectFailure(new ConnectionTimeoutException(3));
            }

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            // Act
            sut.ReadPowerOnce();
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            clock.Advance(TimeSpan.FromMilliseconds(200));
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(TimeSpan.FromMilliseconds(200), harness.Client.Connection.CurrentBackoff);
            Assert.AreEqual(3, harness.Client.Connection.ConnectAttemptCount);
        }

        /// <summary>Two failed connects, which is what arms the shortest backoff — 1 s from the anchor.</summary>
        private static void ArmTheBackoff(FakeModbusTcpHarness harness, SampleModbusTcpBlock sut, LogicBlockTestContext<SampleModbusTcpBlock> context)
        {
            harness.Proxy.EnqueueConnectFailure(new ConnectionTimeoutException(3));
            harness.Proxy.EnqueueConnectFailure(new ConnectionTimeoutException(3));
            sut.ReadPowerOnce();
            sut.ReadPowerOnce();
            context.FlushPendingActions();

            Assert.AreEqual(ModbusTcpConnectionState.BackingOff, harness.Client.Connection.State, "Arrange failed: the backoff was not armed.");
        }

        private static FakeModbusTcpHarness CreateHarness(FakeTimeProvider clock)
        {
            var proxy = new FakeModbusTcpClientProxy { Clock = clock };

            return new FakeModbusTcpHarness(proxy, clock);
        }

        private static SampleModbusTcpBlock CreateBlock(FakeModbusTcpHarness harness)
        {
            return new SampleModbusTcpBlock(harness.Client, new Mock<ILogger>().Object);
        }
    }
}