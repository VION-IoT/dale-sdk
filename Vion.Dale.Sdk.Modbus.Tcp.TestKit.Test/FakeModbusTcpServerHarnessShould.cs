using System;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    [TestClass]
    public class FakeModbusTcpServerHarnessShould
    {
        private FakeModbusTcpServerHarness _harness = null!;

        [TestInitialize]
        public void Initialize()
        {
            _harness = new FakeModbusTcpServerHarness();
            _harness.Server.HoldingRegisterCount = 10;
            _harness.Server.InputRegisterCount = 20;
            _harness.Server.CoilCount = 7;
            _harness.Server.DiscreteInputCount = 1;
        }

        [TestCleanup]
        public void Cleanup()
        {
            _harness.Dispose();
        }

        [TestMethod]
        public void DriveAFullServerCycleWithoutSockets()
        {
            _harness.Server.IsEnabled = true;

            // The test acts as the Modbus master.
            _harness.Client.WriteSingleHoldingRegister(1, 42);
            _harness.Client.WriteSingleCoil(2, true);

            // The block's tick: read commands, publish telemetry — one atomic Sync.
            _harness.Server.Sync(snapshot =>
                                 {
                                     Assert.AreEqual((ushort)42, snapshot.HoldingRegisters.ReadAsUShort(1));
                                     Assert.IsTrue(snapshot.Coils.Read(2));

                                     snapshot.InputRegisters.WriteAsInt(0, -123456, wordOrder: WordOrder32.LswToMsw);
                                     snapshot.DiscreteInputs.Write(0, true);
                                 });

            // Wire-exact assertions, independent of the SDK converter (declared-twice discipline).
            CollectionAssert.AreEqual(new byte[] { 0x1D, 0xC0, 0xFF, 0xFE }, _harness.Client.ReadInputRegistersRaw(0, 2));
            CollectionAssert.AreEqual(new[] { true }, _harness.Client.ReadDiscreteInputs(0, 1));
        }

        [TestMethod]
        public void RejectClientAccessOutsideTheDeclaredExtents()
        {
            _harness.Server.IsEnabled = true;

            var write = Assert.ThrowsExactly<ModbusException>(() => _harness.Client.WriteSingleHoldingRegister(10, 1));
            var read = Assert.ThrowsExactly<ModbusException>(() => _harness.Client.ReadInputRegistersRaw(19, 2));
            var coil = Assert.ThrowsExactly<ModbusException>(() => _harness.Client.WriteSingleCoil(7, true));

            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, write.ExceptionCode);
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, read.ExceptionCode);
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, coil.ExceptionCode);
        }

        [TestMethod]
        public void SetLastClientWriteAtOnClientWrites()
        {
            _harness.Server.IsEnabled = true;
            Assert.IsNull(_harness.Server.LastClientWriteAt);

            var before = DateTimeOffset.UtcNow;
            _harness.Client.WriteSingleHoldingRegister(0, 1);

            Assert.IsNotNull(_harness.Server.LastClientWriteAt);
            Assert.IsTrue(_harness.Server.LastClientWriteAt >= before.AddSeconds(-1));
        }

        [TestMethod]
        public void AllowSeedingDefaultsBeforeEnable()
        {
            _harness.Server.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(5, 0xCAFE));

            _harness.Server.IsEnabled = true;

            CollectionAssert.AreEqual(new byte[] { 0xCA, 0xFE }, _harness.Client.ReadHoldingRegistersRaw(5, 1));
        }

        [TestMethod]
        public void RoundTripRawHoldingRegisterWrites()
        {
            _harness.Server.IsEnabled = true;

            _harness.Client.WriteMultipleHoldingRegistersRaw(0, new byte[] { 0x12, 0x34, 0x56, 0x78 });

            Assert.AreEqual(0x12345678u, _harness.Server.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUInt(0)));
            CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0x56, 0x78 }, _harness.Client.ReadHoldingRegistersRaw(0, 2));
        }

        [TestMethod]
        public void ReadCoilsAsBools()
        {
            _harness.Server.IsEnabled = true;
            _harness.Client.WriteSingleCoil(0, true);
            _harness.Client.WriteSingleCoil(2, true);

            CollectionAssert.AreEqual(new[] { true, false, true }, _harness.Client.ReadCoils(0, 3));
        }

        [TestMethod]
        public void TrackListeningStateOnTheFakeProxy()
        {
            Assert.IsFalse(_harness.Proxy.IsListening);

            _harness.Server.IsEnabled = true;
            Assert.IsTrue(_harness.Proxy.IsListening);

            _harness.Server.IsEnabled = false;
            Assert.IsFalse(_harness.Proxy.IsListening);
        }

        [TestMethod]
        public void HandOutTheServerThroughTheFactory()
        {
            Assert.AreSame(_harness.Server, _harness.ServerFactory.Create());
        }

        [TestMethod]
        public void RejectOddLengthRawHoldingRegisterWrites()
        {
            // Impossible on the real wire (FC16 payloads are 2 bytes per register) — the fake must not
            // silently accept state a real master could never produce.
            _harness.Server.IsEnabled = true;

            Assert.ThrowsExactly<ArgumentException>(() => _harness.Client.WriteMultipleHoldingRegistersRaw(0, new byte[3]));
        }
    }
}