using System.Linq;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.Client.Implementation;
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
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0x12, 0x34, 0x56, 0x78 });

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
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0, 0, 0, 0 });

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

            sut.WriteActivePowerLimit(0x12345678u, WordOrder32.MswToLsw);
            ctx.FlushPendingActions();

            var write = harness.Proxy.WriteHistory.Single();
            Assert.AreEqual(WriteEventKind.MultipleRegisters, write.Kind);
            Assert.AreEqual(1, write.UnitId);
            Assert.AreEqual((ushort)40378, write.Address);
            CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0x56, 0x78 },
                                      write.Bytes,
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

            sut.WriteActivePowerLimit(0x12345678u, WordOrder32.LswToMsw);
            ctx.FlushPendingActions();

            var write = harness.Proxy.WriteHistory.Single();
            CollectionAssert.AreEqual(new byte[] { 0x56, 0x78, 0x12, 0x34 }, write.Bytes, "LswToMsw: low word (0x5678) at addr+0, high word (0x1234) at addr+1.");
        }

        // --- Pipelining: synchronous queue handles back-to-back ops ---

        [TestMethod]
        public void HandleSequenceOfReadsAndWrites_InOrder()
        {
            // Pattern: prove the synchronous queue doesn't drop or reorder pipelined operations.
            // Read, then write, then read again — all three land in history in the expected order.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0x00, 0x01, 0x00, 0x02 });

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            sut.WriteActivePowerLimit(500);
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            Assert.HasCount(2, harness.Proxy.ReadHistory, "Two reads should be recorded.");
            Assert.HasCount(1, harness.Proxy.WriteHistory, "One write should be recorded.");
        }

        // --- Fault injection: Modbus protocol exceptions per address ---

        [TestMethod]
        public void SurfaceInjectedModbusException_OnRead_AsErrorCallback()
        {
            // Pattern: simulate the device returning a Modbus exception code (e.g., the inverter
            // reporting IllegalDataAddress on a register the firmware doesn't expose). The SUT's
            // errorCallback should receive a ModbusException carrying the code.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.EnqueueReadModbusException(1, 40000, ModbusExceptionCode.IllegalDataAddress);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            Assert.IsInstanceOfType<ModbusException>(sut.LastReadError, "Error callback should have received a ModbusException.");
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, ((ModbusException)sut.LastReadError!).ExceptionCode);
            Assert.AreEqual(0u, sut.Power, "Power should not have been updated on a failed read.");
        }

        [TestMethod]
        public void RecoverFromInjectedFault_OnNextReadAtSameAddress()
        {
            // Pattern: fault is one-shot. First read fails, the queue is drained, the second read
            // succeeds against the in-memory store. Models a device-glitch → retry-succeeds scenario.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0x00, 0x00, 0x00, 0x2A });
            harness.Proxy.EnqueueReadModbusException(1, 40000, ModbusExceptionCode.ServerDeviceBusy);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.IsInstanceOfType<ModbusException>(sut.LastReadError, "First read should have failed.");

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(42u, sut.Power, "Second read should have succeeded with the in-memory value.");
        }

        [TestMethod]
        public void StackMultipleFaults_DrainFifo()
        {
            // Pattern: tests can queue multiple faults to model "fail, fail, recover" sequences
            // without explicit teardown between SUT calls.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0x00, 0x00, 0x00, 0x07 });
            harness.Proxy.EnqueueReadModbusException(1, 40000, ModbusExceptionCode.ServerDeviceBusy);
            harness.Proxy.EnqueueReadModbusException(1, 40000, ModbusExceptionCode.GatewayTargetDeviceFailedToRespond);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(ModbusExceptionCode.ServerDeviceBusy, ((ModbusException)sut.LastReadError!).ExceptionCode);

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(ModbusExceptionCode.GatewayTargetDeviceFailedToRespond, ((ModbusException)sut.LastReadError!).ExceptionCode);

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(7u, sut.Power, "Third read drains past the last fault and succeeds.");
        }

        [TestMethod]
        public void SurfaceInjectedModbusException_OnWrite_AndStillRecordTheAttempt()
        {
            // Pattern: write fails with a Modbus exception; the attempted write is still recorded in
            // WriteHistory (so tests can assert "the SUT did issue the write, the device rejected it"
            // — a different bug class from "the SUT didn't issue the write at all"). In-memory store
            // is left unchanged: the device rejected the write, so its registers are untouched.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.EnqueueWriteModbusException(1, 40378, ModbusExceptionCode.IllegalDataValue);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.WriteActivePowerLimit(0xDEADBEEFu);
            ctx.FlushPendingActions();

            Assert.IsInstanceOfType<ModbusException>(sut.LastReadError);
            Assert.AreEqual(ModbusExceptionCode.IllegalDataValue, ((ModbusException)sut.LastReadError!).ExceptionCode);
            Assert.HasCount(1, harness.Proxy.WriteHistory, "The attempted write should still be recorded in history even after the fake threw.");
        }

        // --- Fault injection: operation timeouts ---

        [TestMethod]
        public void SurfaceOperationTimeout_OnRead_AsErrorCallback()
        {
            // Pattern: simulate a device that's online but unresponsive (cable yanked mid-read,
            // firmware hang, etc.). Production raises OperationTimeoutException after the
            // operation timeout elapses; the fake surfaces the same exception immediately
            // because a synchronous test queue can't actually wait for a wall-clock timeout.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.EnqueueReadTimeout(1, 40000);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            Assert.IsInstanceOfType<OperationTimeoutException>(sut.LastReadError);
            Assert.AreEqual(0u, sut.Power, "Power should not have been updated on a timed-out read.");
        }

        [TestMethod]
        public void RecoverFromTimeout_OnNextRead()
        {
            // Pattern: device hung for one tick, then came back. Drive-flush-assert twice — first
            // produces the timeout, second succeeds with the in-memory value.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0x00, 0x00, 0x00, 0x63 });
            harness.Proxy.EnqueueReadTimeout(1, 40000);

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.IsInstanceOfType<OperationTimeoutException>(sut.LastReadError);

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();
            Assert.AreEqual(99u, sut.Power, "Second read should land the in-memory value.");
        }

        // --- Connection lifecycle: previews the IP-change reconnect assertion pattern ---

        [TestMethod]
        public void RecordConnectAsync_OnFirstOperation()
        {
            // Pattern: the SDK connects lazily on the first operation. ConnectionHistory exposes
            // the connect / disconnect calls so tests can assert reconnect behaviour after an
            // IpAddress property change (planned for fault-injection commits).
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0, 0, 0, 0 });

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

        // --- Connection failures + IP-change reconnect (customer-asked reconnect-on-endpoint-change pattern) ---

        [TestMethod]
        public void SurfaceConnectionFailure_AsErrorCallback_AndStillRecordTheAttempt()
        {
            // Pattern: transient unreachability (gateway reboot, network blip). The SUT's
            // errorCallback receives the failure; ConnectionHistory still records the attempt's
            // target IP/port so tests can verify the SUT tried to reach the right endpoint.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.EnqueueConnectFailure(new ConnectionTimeoutException(3.0));

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            Assert.IsInstanceOfType<ConnectionTimeoutException>(sut.LastReadError);
            Assert.HasCount(1, harness.Proxy.ConnectionHistory, "The failed connect attempt should still be in history.");
            Assert.AreEqual(ConnectionEventKind.Connect, harness.Proxy.ConnectionHistory[0].Kind);
            Assert.AreEqual("127.0.0.1", harness.Proxy.ConnectionHistory[0].IpAddress?.ToString());
        }

        [TestMethod]
        public void ReconnectToNewEndpoint_OnIpAddressChange()
        {
            // Pattern: the customer's runtime-reconfig scenario. The SUT's Connection.IpAddress
            // setter forwards to the wrapper which sets _reconnectRequired = true. On the next
            // operation, the wrapper calls Disconnect() (proxy.IsConnected was true) then
            // ConnectAsync(newIp). ConnectionHistory reveals the whole sequence: Connect(old),
            // Disconnect, Connect(new).
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0, 0, 0, 0 });

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            // First op: connects to the SUT's initial IP (127.0.0.1, set in the SUT's ctor).
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            // Reconfigure the SUT's endpoint at runtime, then issue another op.
            harness.Client.IpAddress = "192.168.1.99";
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            var events = harness.Proxy.ConnectionHistory.ToList();
            Assert.HasCount(3, events, "Should have: Connect(old) → Disconnect → Connect(new).");
            Assert.AreEqual(ConnectionEventKind.Connect, events[0].Kind);
            Assert.AreEqual("127.0.0.1", events[0].IpAddress?.ToString());
            Assert.AreEqual(ConnectionEventKind.Disconnect, events[1].Kind);
            Assert.AreEqual(ConnectionEventKind.Connect, events[2].Kind);
            Assert.AreEqual("192.168.1.99", events[2].IpAddress?.ToString());
        }

        // --- Verify helpers: sugar over the raw history accessors ---

        [TestMethod]
        public void VerifyReadAndWriteAndConnect_WithSugarHelpers()
        {
            // Pattern: same assertions as the raw-history tests above, expressed via Verify*
            // extension methods. Cleaner for tests that only care about "did X happen" rather than
            // "what does the full history look like". Sugar over harness.Proxy.{Read|Write|Connection}History.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0x12, 0x34, 0x56, 0x78 });

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            sut.WriteActivePowerLimit(0x12345678u);
            ctx.FlushPendingActions();

            // Read assertion: at addr 40000, 2 registers (one UInt32), via HoldingRegisters function.
            harness.Proxy.VerifyReadSent(1, 40000, 2, ReadEventKind.HoldingRegisters);

            // Write assertion: at addr 40378, with exact wire bytes — the byte-level regression net
            // expressed as a single line. MswToLsw default → high word 0x1234 at addr+0, low 0x5678 at addr+1.
            harness.Proxy.VerifyWriteSent(1, 40378, new byte[] { 0x12, 0x34, 0x56, 0x78 });

            // Connection assertion: the SUT's ctor set 127.0.0.1, the lazy connect targets that.
            harness.Proxy.VerifyConnectAttempted("127.0.0.1", 502);
        }

        [TestMethod]
        public void VerifyConnectionSequence_OnIpChange_WithSugarHelpers()
        {
            // Pattern: the customer's IP-change reconnect scenario, expressed via Verify*. Reads as
            // a story: connect to old, disconnect once, connect to new.
            using var harness = new FakeModbusTcpHarness();
            harness.Proxy.SetHoldingRegisters(1, 40000, new byte[] { 0, 0, 0, 0 });

            var sut = CreateBlock(harness);
            var ctx = sut.CreateTestContext().Build();

            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            harness.Client.IpAddress = "192.168.1.99";
            sut.ReadPowerOnce();
            ctx.FlushPendingActions();

            harness.Proxy.VerifyConnectAttempted("127.0.0.1");
            harness.Proxy.VerifyDisconnectCalled();
            harness.Proxy.VerifyConnectAttempted("192.168.1.99");
        }

        private static SampleModbusTcpBlock CreateBlock(FakeModbusTcpHarness harness)
        {
            return new SampleModbusTcpBlock(harness.Client, new Mock<ILogger>().Object);
        }
    }
}