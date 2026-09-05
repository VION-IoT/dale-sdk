using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    /// <summary>
    ///     The queue the fake client runs on, and the harness that composes it. Every test drives the real
    ///     <c>LogicBlockModbusTcpClient</c> — only the byte-level proxy and this queue are substituted — so
    ///     what runs between the block and the fake is SDK code. No socket, no runtime, no development host.
    /// </summary>
    [TestClass]
    public class SynchronousRequestQueueShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.4")]
        public void RunEnqueuedRequestOnCallingThread()
        {
            // Arrange
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);
            var block = CreateBlock(harness);
            var context = block.CreateTestContext().Build();

            // Act — the read runs during the call, so the proxy has already seen it before any drain
            block.ReadPowerOnce();

            // Assert
            Assert.HasCount(1, harness.Proxy.ReadHistory);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.4")]
        public void RouteCallbackThroughDispatcherProductionUses()
        {
            // Arrange
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);
            var block = CreateBlock(harness);
            var context = block.CreateTestContext().Build();

            // Act
            block.ReadPowerOnce();

            // Assert — the value lands only once the context drains, as a production callback would
            Assert.AreEqual(0u, block.Power);
            context.FlushPendingActions();
            Assert.AreEqual(42u, block.Power);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.5")]
        public void BufferEnqueuedRequestsWhileHeld()
        {
            // Arrange
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);
            var block = CreateBlock(harness);
            block.CreateTestContext().Build();
            harness.Queue.Hold = true;

            // Act
            block.ReadPowerOnce();
            block.ReadPowerOnce();

            // Assert
            Assert.AreEqual(2, harness.Queue.QueuedRequestCount);
            Assert.IsEmpty(harness.Proxy.ReadHistory);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.5")]
        public void RunBufferedRequestsInEnqueueOrderOnDrain()
        {
            // Arrange
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);
            var block = CreateBlock(harness);
            block.CreateTestContext().Build();
            harness.Queue.Hold = true;
            block.ReadPowerOnce();
            block.WriteActivePowerLimit(7);

            // Act
            harness.Queue.Drain();

            // Assert
            Assert.HasCount(1, harness.Proxy.ReadHistory);
            Assert.HasCount(1, harness.Proxy.WriteHistory);
            Assert.AreEqual(0, harness.Queue.QueuedRequestCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.5")]
        public void BufferAgainRequestEnqueuedDuringDrainWhileStillHeld()
        {
            // Arrange
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);
            var block = CreateBlock(harness);
            var context = block.CreateTestContext().Build();
            harness.Queue.Hold = true;
            block.ReadPowerOnce();

            // Act — the drain runs what it holds; the callback's own follow-up read is enqueued during it
            harness.Queue.Drain();
            context.FlushPendingActions();
            block.ReadPowerOnce();

            // Assert
            Assert.AreEqual(1, harness.Queue.QueuedRequestCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.7")]
        public void DiscardBufferedRequestsOnDispose()
        {
            // Arrange
            var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);
            var block = CreateBlock(harness);
            block.CreateTestContext().Build();
            harness.Queue.Hold = true;
            block.ReadPowerOnce();

            // Act
            harness.Queue.Dispose();

            // Assert — a disposed queue runs nothing it was holding
            Assert.AreEqual(0, harness.Queue.QueuedRequestCount);
            Assert.IsEmpty(harness.Proxy.ReadHistory);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.7")]
        public void RefuseRequestEnqueuedBeforeInitialization()
        {
            // Arrange — the queue is initialized when the client is enabled, so a request issued
            // against a queue nothing initialized has no accumulator to report into
            var queue = new SynchronousRequestQueue(new Mock<Vion.Dale.Sdk.Modbus.Tcp.Client.Request.IRequestFactory>().Object);

            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => queue.Enqueue("probe", null!, _ => System.Threading.Tasks.Task.CompletedTask, null, null));
            StringAssert.Contains(thrown.Message, nameof(SynchronousRequestQueue));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.3")]
        public void MeasureOnRealSystemClockUnlessCallerSuppliesOne()
        {
            // Arrange / Act
            using var systemClocked = new FakeModbusTcpHarness();
            var supplied = new FakeTimeProvider(new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero));
            using var virtuallyClocked = new FakeModbusTcpHarness(supplied);

            systemClocked.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);
            virtuallyClocked.Proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);

            var onSystemClock = CreateBlock(systemClocked);
            var onVirtualClock = CreateBlock(virtuallyClocked);
            var systemContext = onSystemClock.CreateTestContext().Build();
            var virtualContext = onVirtualClock.CreateTestContext().WithTimeProvider(supplied).Build();

            onSystemClock.ReadPowerOnce();
            onVirtualClock.ReadPowerOnce();
            systemContext.FlushPendingActions();
            virtualContext.FlushPendingActions();

            // Assert — the receipt of the harness given a clock is stamped from it; the default one is not
            Assert.AreEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), onVirtualClock.LastReadReceipt!.Value.ReceivedAt);
            Assert.AreNotEqual(new DateTime(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc), onSystemClock.LastReadReceipt!.Value.ReceivedAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.3")]
        public void RefuseNullProxy()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => new FakeModbusTcpHarness((FakeModbusTcpClientProxy)null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => new FakeModbusTcpServerHarness(null!));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.3")]
        public void RefuseNullClock()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => new FakeModbusTcpHarness((TimeProvider)null!));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-010.3")]
        public void RecordSingleCoilWriteInWireBytesAndMultipleCoilWritePerCoil()
        {
            // Arrange — the two coil-write arms record differently on purpose, and the WriteEvent doc
            // says so: one byte per coil is what makes an expected-bytes argument legible
            var proxy = new FakeModbusTcpClientProxy();
            IModbusTcpClientProxy asProxy = proxy;

            // Act
            asProxy.WriteSingleCoilAsync(1, 20, true, default).GetAwaiter().GetResult();
            asProxy.WriteMultipleCoilsAsync(1, 10, [true, false, true], default).GetAwaiter().GetResult();

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0xFF }, proxy.WriteHistory[0].Bytes);
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x00, 0x01 }, proxy.WriteHistory[1].Bytes);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-010.3")]
        public void AnswerCoilReadInWireBitPacking()
        {
            // Arrange — the read direction does pack bits, which is the asymmetry the write doc names
            var proxy = new FakeModbusTcpClientProxy();
            proxy.SetCoil(1, 10, true);
            proxy.SetCoil(1, 12, true);
            IModbusTcpClientProxy asProxy = proxy;

            // Act
            var packed = asProxy.ReadCoilsAsync(1, 10, 3, default).GetAwaiter().GetResult();

            // Assert — coils 0 and 2 of the window, least-significant bit first
            CollectionAssert.AreEqual(new byte[] { 0b0000_0101 }, packed.ToArray());
        }

        private static SampleModbusTcpBlock CreateBlock(FakeModbusTcpHarness harness)
        {
            return new SampleModbusTcpBlock(harness.Client, new Mock<ILogger>().Object);
        }
    }
}
