using System;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Moq;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    /// <summary>
    ///     The fake client's two delay knobs and the queue's maximum queued age — the three settable
    ///     durations the kit exposes. Each row is one setter, because they are the same rule at three call
    ///     sites and a guard added to one alone would pass a third of it.
    /// </summary>
    [TestClass]
    public class FakeModbusTcpClientProxyGuardsShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-TKIT-010.5")]
        public void RefuseNegativeResponseDelay()
        {
            // Arrange
            var proxy = new FakeModbusTcpClientProxy();

            // Act / Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => proxy.ResponseDelay = TimeSpan.FromMilliseconds(-5));
            Assert.AreEqual(TimeSpan.Zero, proxy.ResponseDelay);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-010.5")]
        public void RefuseNegativeConnectDelay()
        {
            // Arrange
            var proxy = new FakeModbusTcpClientProxy();

            // Act / Assert
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => proxy.ConnectDelay = TimeSpan.FromMilliseconds(-5));
            Assert.AreEqual(TimeSpan.Zero, proxy.ConnectDelay);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-010.5")]
        public void AcceptZeroDelayWithoutClock()
        {
            // Arrange
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.ResponseDelay = TimeSpan.Zero;
            harness.Proxy.ConnectDelay = TimeSpan.Zero;
            harness.Proxy.SetHoldingRegister(1, 0, 0x12, 0x34);

            // Act / Assert — zero is the default and must not need a clock, which is what the guard below
            // would otherwise make it need
            Assert.IsNull(harness.Proxy.Clock);
            Assert.AreEqual(TimeSpan.Zero, harness.Proxy.ResponseDelay);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-010.5")]
        public void ConsumeVirtualTimeOnEveryOperationWhenDelayConfigured()
        {
            // Arrange
            var clock = new FakeTimeProvider(new DateTimeOffset(2026,
                                                                1,
                                                                1,
                                                                0,
                                                                0,
                                                                0,
                                                                TimeSpan.Zero));
            var proxy = new FakeModbusTcpClientProxy { Clock = clock, ResponseDelay = TimeSpan.FromMilliseconds(40), ConnectDelay = TimeSpan.FromMilliseconds(10) };
            using var harness = new FakeModbusTcpHarness(proxy, clock);
            proxy.SetHoldingRegisters(1, 40000, [0x00, 0x00, 0x00, 0x2A]);
            var block = new SampleModbusTcpBlock(harness.Client, new Mock<ILogger>().Object);
            var context = block.CreateTestContext().WithTimeProvider(clock).Build();

            // Act
            block.ReadPowerOnce();
            context.FlushPendingActions();

            // Assert — one connect and one read, so 10 ms + 40 ms of virtual time
            Assert.AreEqual(new DateTime(2026,
                                         1,
                                         1,
                                         0,
                                         0,
                                         0,
                                         50,
                                         DateTimeKind.Utc),
                            clock.GetUtcNow().UtcDateTime);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-010.5")]
        public void RefuseOperationWhenDelayConfiguredWithoutClock()
        {
            // Arrange
            var proxy = new FakeModbusTcpClientProxy { ResponseDelay = TimeSpan.FromMilliseconds(40) };
            using var harness = new FakeModbusTcpHarness(proxy);
            var block = new SampleModbusTcpBlock(harness.Client, new Mock<ILogger>().Object);
            var context = block.CreateTestContext().Build();

            // Act
            block.ReadPowerOnce();
            context.FlushPendingActions();

            // Assert — the refusal reaches the block as the operation's failure, naming both properties
            Assert.IsNotNull(block.LastReadError);
            StringAssert.Contains(block.LastReadError!.Message, "ResponseDelay needs a virtual clock");
            StringAssert.Contains(block.LastReadError!.Message, nameof(FakeModbusTcpHarness));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.6")]
        [DataRow(0, DisplayName = "zero")]
        [DataRow(-1, DisplayName = "negative")]
        public void RefuseMaximumQueuedAgeNotGreaterThanZero(int seconds)
        {
            // Arrange
            using var harness = new FakeModbusTcpHarness();
            var before = harness.Queue.MaxQueuedAge;

            // Act / Assert — the same refusal the real queue makes, which the fake's inherited
            // documentation already promised
            Assert.ThrowsExactly<ArgumentOutOfRangeException>(() => harness.Queue.MaxQueuedAge = TimeSpan.FromSeconds(seconds));
            Assert.AreEqual(before, harness.Queue.MaxQueuedAge);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.6")]
        public void AcceptMaximumQueuedAgeGreaterThanZeroAndNullToDisable()
        {
            // Arrange
            using var harness = new FakeModbusTcpHarness();

            // Act
            harness.Queue.MaxQueuedAge = TimeSpan.FromSeconds(5);

            // Assert
            Assert.AreEqual(TimeSpan.FromSeconds(5), harness.Queue.MaxQueuedAge);
            harness.Queue.MaxQueuedAge = null;
            Assert.IsNull(harness.Queue.MaxQueuedAge);
        }
    }
}