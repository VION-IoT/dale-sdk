using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Modbus.Core.Diagnostics;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Diagnostics;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    /// <summary>
    ///     The receipt, link and connection surface driven end to end through a real
    ///     <c>LogicBlockModbusTcpClient</c> on a virtual clock — the shape a consumer will write against. Only
    ///     <c>IModbusTcpClientProxy</c> and <c>IRequestQueue</c> are substituted; everything between the block and the
    ///     bytes is production code.
    /// </summary>
    [TestClass]
    public class ModbusLinkDiagnosticsShould
    {
        private static readonly DateTime Anchor = new(2026,
                                                      8,
                                                      18,
                                                      12,
                                                      0,
                                                      0,
                                                      DateTimeKind.Utc);

        [TestMethod]
        public void DeliverAReceiptToTheReadCallbackStampedWhenTheResponseWasObserved()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.ResponseDelay = TimeSpan.FromMilliseconds(120);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x12, 0x34, 0x56, 0x78]);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            // Act
            sut.ReadPowerOnce();

            // The block is slow to drain its mailbox; the receipt must not move with it.
            clock.Advance(TimeSpan.FromSeconds(9));
            ctx.FlushPendingActions();

            // Assert
            Assert.IsNotNull(sut.LastReadReceipt);
            var receipt = sut.LastReadReceipt!.Value;
            Assert.AreEqual(ModbusOutcome.Success, receipt.Outcome);
            Assert.AreEqual(Anchor.AddMilliseconds(120), receipt.ReceivedAt);
            Assert.AreEqual(TimeSpan.FromMilliseconds(120), receipt.RoundTrip);
            Assert.AreEqual(TimeSpan.Zero, receipt.QueuedWait);
            Assert.AreEqual(TimeSpan.FromSeconds(9), clock.GetElapsedTime(receipt.ReceivedTimestamp), "The monotonic stamp is what a freshness gate ages the value on.");
        }

        [TestMethod]
        public void ReportTheLinkAsFaultedAfterAConnectFailureAndOnlineAfterTheNextAnswer()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.ConnectDelay = TimeSpan.FromMilliseconds(40);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0, 0, 0, 0]);
            harness.Proxy.EnqueueConnectFailure(new ConnectionTimeoutException(3));

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            // Act — first read cannot connect.
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(ModbusLinkState.Faulted, harness.Client.Link.State);
            Assert.AreEqual(1, harness.Client.Link.TimeoutCount);
            Assert.IsNull(harness.Client.Link.LastContactAt);
            Assert.AreEqual(ModbusOutcome.Timeout, sut.LastReadReceipt!.Value.Outcome);

            var connection = harness.Client.Connection;
            Assert.AreEqual(ModbusTcpConnectionState.Disconnected, connection.State);
            Assert.AreEqual(1, connection.ConnectAttemptCount);
            Assert.AreEqual(1, connection.ConnectFailureCount);
            Assert.AreEqual(1, connection.ConsecutiveConnectFailures);

            // Act — the second read gets through.
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(ModbusLinkState.Online, harness.Client.Link.State);
            Assert.AreEqual(1, harness.Client.Link.SuccessCount);
            Assert.IsNotNull(harness.Client.Link.LastContactAt);

            connection = harness.Client.Connection;
            Assert.AreEqual(ModbusTcpConnectionState.Connected, connection.State);
            Assert.AreEqual(2, connection.ConnectAttemptCount);
            Assert.AreEqual(0, connection.ConsecutiveConnectFailures, "A successful connect clears the consecutive-failure run.");
            Assert.AreEqual(TimeSpan.FromMilliseconds(40), connection.LastConnectDuration);
        }

        [TestMethod]
        public void ExpireHeldRequestsOlderThanMaxQueuedAgeWithoutContactingTheDevice()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            // A backed-up queue: the reads are accepted now and run much later.
            harness.Queue.Hold = true;
            sut.ReadPowerOnce();
            sut.ReadPowerOnce();

            // Act — 40 s of outage, then the queue drains.
            clock.Advance(TimeSpan.FromSeconds(40));
            harness.Queue.Drain();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(ModbusOutcome.Expired, sut.LastReadReceipt!.Value.Outcome);
            Assert.IsInstanceOfType<RequestExpiredException>(sut.LastReadError);
            Assert.AreEqual(0u, sut.Power, "An expired read must not deliver a value.");
            Assert.IsEmpty(harness.Proxy.ReadHistory, "An expired request never reaches the device.");
            Assert.IsEmpty(harness.Proxy.ConnectionHistory, "An expired request must not even establish a connection.");

            var link = harness.Client.Link;
            Assert.AreEqual(2, link.ExpiredCount);
            Assert.AreEqual(ModbusOutcome.Expired, link.LastFailureOutcome);
            Assert.AreEqual(ModbusLinkState.Unknown, link.State, "Local congestion says nothing about the device, so the link verdict must not move.");
        }

        [TestMethod]
        public void ExecuteRequestsThatAreStillFreshWhenTheQueueDrains()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            harness.Queue.Hold = true;
            sut.ReadPowerOnce();

            // Act — well inside the 30 s default.
            clock.Advance(TimeSpan.FromSeconds(5));
            harness.Queue.Drain();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(ModbusOutcome.Success, sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(42u, sut.Power);
            Assert.AreEqual(TimeSpan.FromSeconds(5), sut.LastReadReceipt!.Value.QueuedWait);
            Assert.AreEqual(TimeSpan.FromSeconds(5), harness.Client.Link.MaxQueuedWait);
        }

        [TestMethod]
        public void ReportTheQueueDepthOnTheLinkSummary()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0, 0, 0, 0]);

            var sut = CreateBlock(harness);
            sut.CreateTestContext().WithTimeProvider(clock).Build();

            // Act
            harness.Queue.Hold = true;
            sut.ReadPowerOnce();
            sut.ReadPowerOnce();
            sut.ReadPowerOnce();

            // Assert
            Assert.AreEqual(3, harness.Client.Link.QueueDepth);
        }

        [TestMethod]
        public void DeliverAReceiptToTheWriteCallback()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.ResponseDelay = TimeSpan.FromMilliseconds(15);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            // Act
            sut.WriteActivePowerLimit(5000);
            ctx.FlushPendingActions();

            // Assert
            Assert.IsNotNull(sut.LastWriteReceipt);
            Assert.AreEqual(ModbusOutcome.Success, sut.LastWriteReceipt!.Value.Outcome);
            Assert.AreEqual(TimeSpan.FromMilliseconds(15), sut.LastWriteReceipt!.Value.RoundTrip);
        }

        [TestMethod]
        public void ClassifyADeviceExceptionCodeAsDeviceErrorAndKeepTheLinkOnline()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.EnqueueReadFault(1, 40000, new ModbusException(ModbusExceptionCode.IllegalDataAddress, "no such register"));

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            // Act
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert — the device answered, so the link is up even though the request was refused.
            Assert.AreEqual(ModbusOutcome.DeviceError, sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(ModbusLinkState.Online, harness.Client.Link.State);
            Assert.AreEqual(1, harness.Client.Link.DeviceErrorCount);
            Assert.IsNotNull(harness.Client.Link.LastContactAt);
        }

        [TestMethod]
        public void ClassifyAFrameFaultWithoutADeviceCodeAsProtocolError()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.EnqueueReadFault(1, 40000, new ModbusException("no specific exception code"));

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            // Act
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(ModbusOutcome.ProtocolError, sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(ModbusLinkState.Faulted, harness.Client.Link.State);
            Assert.AreEqual(0, harness.Client.Link.DeviceErrorCount);
            Assert.AreEqual(1, harness.Client.Link.ProtocolErrorCount);
        }

        [TestMethod]
        public void RejectANonPositiveMaxQueuedAge()
        {
            // Arrange
            using var harness = CreateHarness(new FakeTimeProvider(Anchor));

            // Act / Assert
            Assert.Throws<ArgumentOutOfRangeException>(() => harness.Client.MaxQueuedAge = TimeSpan.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => harness.Client.MaxQueuedAge = TimeSpan.FromSeconds(-1));
            Assert.AreEqual(TimeSpan.FromSeconds(30), harness.Client.MaxQueuedAge, "A rejected value must not replace the one in force.");
        }

        [TestMethod]
        public void ExecuteEveryQueuedRequestWhenMaxQueuedAgeIsDisabled()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);
            harness.Client.MaxQueuedAge = null;

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            harness.Queue.Hold = true;
            sut.ReadPowerOnce();

            // Act
            clock.Advance(TimeSpan.FromMinutes(10));
            harness.Queue.Drain();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(ModbusOutcome.Success, sut.LastReadReceipt!.Value.Outcome);
            Assert.AreEqual(42u, sut.Power);
        }

        [TestMethod]
        public void ApplyAMaxQueuedAgeSetAfterTheRequestsWereAlreadyQueued()
        {
            // Arrange
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();

            harness.Queue.Hold = true;
            sut.ReadPowerOnce();
            clock.Advance(TimeSpan.FromSeconds(5));

            // Act — tightened while the request is already waiting; the check runs at dequeue, so it applies.
            harness.Client.MaxQueuedAge = TimeSpan.FromSeconds(2);
            harness.Queue.Drain();
            ctx.FlushPendingActions();

            // Assert
            Assert.AreEqual(ModbusOutcome.Expired, sut.LastReadReceipt!.Value.Outcome);
            Assert.IsEmpty(harness.Proxy.ReadHistory);
        }

        [TestMethod]
        public void RunADisconnectHoweverLongItWaited()
        {
            // Arrange — a disconnect that has waited a long time is exactly the one still worth doing, so it is
            // exempt from the queued-age check and carries no receipt.
            var clock = new FakeTimeProvider(Anchor);
            using var harness = CreateHarness(clock);
            harness.Proxy.SetHoldingRegisters(1, 40000, [0, 0, 0, 0]);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().WithTimeProvider(clock).Build();
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            var disconnected = false;
            harness.Queue.Hold = true;
            harness.Client.Disconnect(sut, () => disconnected = true);

            // Act
            clock.Advance(TimeSpan.FromSeconds(40));
            harness.Queue.Drain();
            ctx.FlushPendingActions();

            // Assert
            Assert.IsTrue(disconnected);
            Assert.AreEqual(ModbusTcpConnectionState.Disconnected, harness.Client.Connection.State);
            Assert.AreEqual(0, harness.Client.Link.ExpiredCount, "A control operation is not a device transaction and must not appear in the link summary.");
            Assert.AreEqual(1, harness.Client.Link.SuccessCount, "Only the read counts.");
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