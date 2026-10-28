using System;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.TestKit;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit.Test
{
    [TestClass]
    public class FakeModbusTcpServerHarnessShould
    {
        private const ushort SharedAddress = 7;

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
        [TestProperty("spec", "AC-TKIT-011.1")]
        public void DriveFullServerCycleWithoutSockets()
        {
            // Arrange
            _harness.Server.IsEnabled = true;

            // The test acts as the Modbus master.
            _harness.Client.WriteSingleHoldingRegister(1, 42);
            _harness.Client.WriteSingleCoil(2, true);

            // The block's tick: read commands, publish telemetry — one atomic Sync.
            _harness.Server.Sync(snapshot =>
                                 {
                                     // Act / Assert
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
        [TestProperty("spec", "AC-TKIT-012.2")]
        public void RejectClientAccessOutsideDeclaredExtents()
        {
            // Arrange
            _harness.Server.IsEnabled = true;

            var write = Assert.ThrowsExactly<ModbusException>(() => _harness.Client.WriteSingleHoldingRegister(10, 1));
            var read = Assert.ThrowsExactly<ModbusException>(() => _harness.Client.ReadInputRegistersRaw(19, 2));
            var coil = Assert.ThrowsExactly<ModbusException>(() => _harness.Client.WriteSingleCoil(7, true));

            // Act / Assert
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, write.ExceptionCode);
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, read.ExceptionCode);
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, coil.ExceptionCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.3")]
        public void StampLastClientWriteOnEveryClientWrite()
        {
            // Arrange
            _harness.Server.IsEnabled = true;

            // Act / Assert
            Assert.IsNull(_harness.Server.LastClientWriteAt);

            var before = DateTimeOffset.UtcNow;
            _harness.Client.WriteSingleHoldingRegister(0, 1);

            Assert.IsNotNull(_harness.Server.LastClientWriteAt);
            Assert.IsTrue(_harness.Server.LastClientWriteAt >= before.AddSeconds(-1));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.2")]
        public void AllowSeedingRegisterBuffersBeforeEnable()
        {
            // Arrange
            _harness.Server.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(5, 0xCAFE));

            _harness.Server.IsEnabled = true;

            // Act / Assert
            CollectionAssert.AreEqual(new byte[] { 0xCA, 0xFE }, _harness.Client.ReadHoldingRegistersRaw(5, 1));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.1")]
        public void RoundTripRawHoldingRegisterWrites()
        {
            // Arrange
            _harness.Server.IsEnabled = true;

            _harness.Client.WriteMultipleHoldingRegistersRaw(0, new byte[] { 0x12, 0x34, 0x56, 0x78 });

            // Act / Assert
            Assert.AreEqual(0x12345678u, _harness.Server.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUInt(0)));
            CollectionAssert.AreEqual(new byte[] { 0x12, 0x34, 0x56, 0x78 }, _harness.Client.ReadHoldingRegistersRaw(0, 2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.1")]
        public void ReadCoilsAsBooleans()
        {
            // Arrange
            _harness.Server.IsEnabled = true;
            _harness.Client.WriteSingleCoil(0, true);
            _harness.Client.WriteSingleCoil(2, true);

            // Act / Assert
            CollectionAssert.AreEqual(new[] { true, false, true }, _harness.Client.ReadCoils(0, 3));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.2")]
        public void RefuseSimulatedRequestWhileNotListening()
        {
            // Arrange

            // Act / Assert
            Assert.IsFalse(_harness.Proxy.IsListening);

            _harness.Server.IsEnabled = true;
            Assert.IsTrue(_harness.Proxy.IsListening);

            _harness.Server.IsEnabled = false;
            Assert.IsFalse(_harness.Proxy.IsListening);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-011.2")]
        public void HandOutServerThroughFactory()
        {
            // Arrange

            // Act / Assert
            Assert.AreSame(_harness.Server, _harness.ServerFactory.Create());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.2")]
        public void RejectOddLengthRawHoldingRegisterWrites()
        {
            // Impossible on the real wire (FC16 payloads are 2 bytes per register) — the fake must not
            // silently accept state a real master could never produce.
            // Arrange
            _harness.Server.IsEnabled = true;

            // Act / Assert
            Assert.ThrowsExactly<ArgumentException>(() => _harness.Client.WriteMultipleHoldingRegistersRaw(0, new byte[3]));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.4")]
        public void DrivePerUnitServerCycleWithoutSockets()
        {
            // Arrange
            using var harness = new FakeModbusTcpServerHarness();
            var (socket1, socket2, _) = DeclareSocketsAndStation(harness.Server);
            harness.Server.IsEnabled = true;

            // The test acts as the Modbus master: one setpoint per socket, at the address both sockets share.
            harness.Client.ForUnit(1).WriteSingleHoldingRegister(SharedAddress, 16);
            harness.Client.ForUnit(2).WriteSingleHoldingRegister(SharedAddress, 32);

            // Act
            // The block's tick, per socket: read its own setpoint, publish its own telemetry beside it.
            foreach (var socket in new[] { socket1, socket2 })
            {
                socket.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(SharedAddress + 1, (ushort)(snapshot.HoldingRegisters.ReadAsUShort(SharedAddress) * 10)));
            }

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x00, 0xA0 }, harness.Client.ForUnit(1).ReadHoldingRegistersRaw(SharedAddress + 1, 1));
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x40 }, harness.Client.ForUnit(2).ReadHoldingRegistersRaw(SharedAddress + 1, 1));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.4")]
        public void AnswerUndeclaredUnitWithGatewayException()
        {
            // Arrange
            using var harness = new FakeModbusTcpServerHarness();
            var (socket1, _, _) = DeclareSocketsAndStation(harness.Server);
            harness.Server.IsEnabled = true;
            var undeclared = harness.Client.ForUnit(3);

            // Act
            var read = Assert.ThrowsExactly<ModbusException>(() => undeclared.ReadHoldingRegistersRaw(SharedAddress, 1));
            var write = Assert.ThrowsExactly<ModbusException>(() => undeclared.WriteSingleHoldingRegister(SharedAddress, 1));

            // Assert
            Assert.AreEqual(ModbusExceptionCode.GatewayTargetDeviceFailedToRespond, read.ExceptionCode);
            Assert.AreEqual(ModbusExceptionCode.GatewayTargetDeviceFailedToRespond, write.ExceptionCode);
            Assert.AreEqual((ushort)0, socket1.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(SharedAddress)));
            Assert.IsNull(harness.Server.LastClientWriteAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.4")]
        public void AnswerAnyUnitFromOneMapWhenNoUnitDeclared()
        {
            // Arrange
            _harness.Server.IsEnabled = true;

            // Act
            _harness.Client.ForUnit(5).WriteSingleHoldingRegister(1, 42);

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x2A }, _harness.Client.ForUnit(9).ReadHoldingRegistersRaw(1, 1));
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x2A }, _harness.Client.ReadHoldingRegistersRaw(1, 1));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.2")]
        public void RejectAccessOutsideAddressedUnitExtents()
        {
            // Arrange
            using var harness = new FakeModbusTcpServerHarness();
            DeclareSocketsAndStation(harness.Server);
            harness.Server.IsEnabled = true;
            harness.Client.ForUnit(1).ReadHoldingRegistersRaw(SharedAddress, 1); // inside socket 1's extent — answered

            // Act
            var read = Assert.ThrowsExactly<ModbusException>(() => harness.Client.ForUnit(200).ReadHoldingRegistersRaw(SharedAddress, 1));

            // Assert
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, read.ExceptionCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.5")]
        public void RefuseUnaddressedAccessWhenUnitsDeclared()
        {
            // Arrange
            using var harness = new FakeModbusTcpServerHarness();
            var (socket1, _, _) = DeclareSocketsAndStation(harness.Server);
            harness.Server.IsEnabled = true;

            // Act / Assert
            var viaClient = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Client.WriteSingleHoldingRegister(SharedAddress, 1));
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Client.ReadCoils(0, 1));
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Proxy.SimulateClientWriteHoldingRegisters(SharedAddress, new byte[] { 0x00, 0x01 }));
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Proxy.SimulateClientReadInputRegisters(0, 1));
            StringAssert.Contains(viaClient.Message, nameof(FakeModbusTcpServerClient.ForUnit));
            Assert.AreEqual((ushort)0, socket1.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(SharedAddress)));
            Assert.IsNull(harness.Server.LastClientWriteAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-012.6")]
        public void StampLastClientWritePerUnit()
        {
            // Arrange
            var clock = new FakeTimeProvider();
            using var harness = new FakeModbusTcpServerHarness(new FakeModbusTcpServerProxy { TimeProvider = clock });
            var (socket1, socket2, station) = DeclareSocketsAndStation(harness.Server);
            harness.Server.IsEnabled = true;
            var firstWriteAt = clock.GetUtcNow();
            harness.Client.ForUnit(1).WriteSingleHoldingRegister(SharedAddress, 1);
            clock.Advance(TimeSpan.FromSeconds(1));

            // Act
            harness.Client.ForUnit(2).WriteSingleCoil(0, true);

            // Assert
            Assert.AreEqual(firstWriteAt, socket1.LastClientWriteAt);
            Assert.AreEqual(firstWriteAt.AddSeconds(1), socket2.LastClientWriteAt);
            Assert.IsNull(station.LastClientWriteAt);
            Assert.AreEqual(firstWriteAt.AddSeconds(1), harness.Server.LastClientWriteAt);
        }

        /// <summary>
        ///     The layout of the device that motivated per-unit maps: sockets at units 1 and 2 sharing their addresses,
        ///     and a station at unit 200 whose extent stops short of the address the sockets share.
        /// </summary>
        private static (ILogicBlockModbusTcpServerUnit Socket1, ILogicBlockModbusTcpServerUnit Socket2, ILogicBlockModbusTcpServerUnit Station) DeclareSocketsAndStation(
            ILogicBlockModbusTcpServer server)
        {
            var socket1 = server.AddUnit(1);
            socket1.HoldingRegisterCount = 10;
            socket1.CoilCount = 8;
            var socket2 = server.AddUnit(2);
            socket2.HoldingRegisterCount = 10;
            socket2.CoilCount = 8;
            var station = server.AddUnit(200);
            station.HoldingRegisterCount = 5;

            return (socket1, socket2, station);
        }
    }
}