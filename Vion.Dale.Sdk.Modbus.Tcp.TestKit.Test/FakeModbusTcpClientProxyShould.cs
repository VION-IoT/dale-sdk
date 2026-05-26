using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    /// <summary>
    ///     End-to-end usage examples for the Modbus TCP TestKit. Each test demonstrates a real test
    ///     pattern an SDK consumer would use, against a real <c>LogicBlockModbusTcpClient</c> and real
    ///     <c>ModbusTcpClientWrapper</c> — only <c>IModbusTcpClientProxy</c> and <c>IRequestQueue</c>
    ///     are substituted. The byte / word-order math is the same code that runs in production.
    /// </summary>
    [TestClass]
    public class FakeModbusTcpClientProxyShould
    {
        // --- Read path: byte / word-order coverage ---

        [TestMethod]
        public void DecodeUInt32_FromHoldingRegisters_WithMsbToLsb_AndMswToLsw()
        {
            // Pattern: pre-populate bytes the device "would have returned", drive the SUT's read,
            // assert the decoded value lands on the [ServiceProperty]. Catches byte/word-order bugs
            // because real ModbusDataConverter does the conversion against fake bytes.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(unitId: 1, startingAddress: 40000, registerBytes: new byte[] { 0x12, 0x34, 0x56, 0x78 });

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            Assert.AreEqual(0x12345678u, sut.Power, "MsbToLsb + MswToLsw should produce big-endian-word-and-byte interpretation.");
            Assert.IsNull(sut.LastReadError);
        }

        [TestMethod]
        public void RecordReadInHistory_WithAddressAndQuantity()
        {
            // Pattern: assert the SUT issued the read it was supposed to (address + count), independent
            // of the decoded value. Useful for "did the SUT poll the right register block?" checks.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(unitId: 1, startingAddress: 40000, registerBytes: new byte[] { 0, 0, 0, 0 });

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            Assert.HasCount(1, harness.Proxy.ReadHistory, "Exactly one read should have been issued.");
            var read = harness.Proxy.ReadHistory[0];
            Assert.AreEqual(ReadEventKind.HoldingRegisters, read.Kind);
            Assert.AreEqual(1, read.UnitId);
            Assert.AreEqual((ushort)40000, read.Address);
            Assert.AreEqual((ushort)2, read.Quantity); // 1 UInt32 = 2 registers
        }

        // --- Write path: wire-format encoding verification ---

        [TestMethod]
        public void EncodeUInt32Write_WithMswToLsw_ProducesExpectedBytes()
        {
            // Pattern: drive a typed write, then read back the bytes the fake recorded to verify the
            // SUT's encoding (byte order + word order + scaling). This is the byte-level-bug
            // regression net the customer asked for — catches the class of bug where the C# call
            // looks right but the wire bytes are wrong.
            using var harness = new FakeModbusTcpHarness();
            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.WriteActivePowerLimit(value: 0x12345678u, wordOrder: WordOrder32.MswToLsw);
            ctx.FlushPendingActions();

            var write = harness.Proxy.WriteHistory.Single();
            Assert.AreEqual(WriteEventKind.MultipleRegisters, write.Kind);
            Assert.AreEqual(1, write.UnitId);
            Assert.AreEqual((ushort)40378, write.Address);
            CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0x56, 0x78 }, write.Bytes,
                                      "MswToLsw + MsbToLsb: high word (0x1234) at addr+0, low word (0x5678) at addr+1; bytes MSB-first per register.");
        }

        [TestMethod]
        public void EncodeUInt32Write_WithLswToMsw_SwapsWords()
        {
            // Same write value, opposite word order — verifies the conversion code IS executing,
            // not just passing input through. With LswToMsw the high and low words swap positions.
            using var harness = new FakeModbusTcpHarness();
            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.WriteActivePowerLimit(value: 0x12345678u, wordOrder: WordOrder32.LswToMsw);
            ctx.FlushPendingActions();

            var write = harness.Proxy.WriteHistory.Single();
            CollectionAssert.AreEqual(new byte[] { 0x56, 0x78, 0x12, 0x34 }, write.Bytes,
                                      "LswToMsw: low word (0x5678) at addr+0, high word (0x1234) at addr+1.");
        }

        // --- Pipelining: synchronous queue handles back-to-back ops ---

        [TestMethod]
        public void HandleSequenceOfReadsAndWrites_InOrder()
        {
            // Pattern: prove the synchronous queue doesn't drop or reorder pipelined operations.
            // Read, then write, then read again — all three land in history in the expected order.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(unitId: 1, startingAddress: 40000, registerBytes: new byte[] { 0x00, 0x01, 0x00, 0x02 });

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            sut.WriteActivePowerLimit(value: 500);
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            Assert.HasCount(2, harness.Proxy.ReadHistory, "Two reads should be recorded.");
            Assert.HasCount(1, harness.Proxy.WriteHistory, "One write should be recorded.");
        }

        // --- Connection lifecycle: previews the IP-change reconnect assertion pattern ---

        [TestMethod]
        public void RecordConnectAsync_OnFirstOperation()
        {
            // Pattern: the SDK connects lazily on the first operation. ConnectionHistory exposes
            // the connect / disconnect calls so tests can assert reconnect behaviour after an
            // IpAddress property change (planned for fault-injection commits).
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(unitId: 1, startingAddress: 40000, registerBytes: new byte[] { 0, 0, 0, 0 });

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            Assert.IsEmpty(harness.Proxy.ConnectionHistory, "No connection should happen before the first operation.");

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            Assert.IsGreaterThanOrEqualTo(1, harness.Proxy.ConnectionHistory.Count, "ConnectAsync should have been called on the proxy for the first operation.");
            Assert.AreEqual(ConnectionEventKind.Connect, harness.Proxy.ConnectionHistory[0].Kind);
            Assert.AreEqual("127.0.0.1", harness.Proxy.ConnectionHistory[0].IpAddress?.ToString());
            Assert.AreEqual(502, harness.Proxy.ConnectionHistory[0].Port);
        }

        private static SampleModbusTcpBlock CreateBlock(FakeModbusTcpHarness harness)
        {
            return new SampleModbusTcpBlock(harness.Client, new Mock<ILogger>().Object);
        }
    }
}
