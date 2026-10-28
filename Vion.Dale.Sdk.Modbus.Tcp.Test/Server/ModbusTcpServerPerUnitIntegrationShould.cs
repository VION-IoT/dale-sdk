using System;
using System.Net;
using FluentModbus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Modbus.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.Test.TestHelpers;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Server
{
    /// <summary>
    ///     Real-socket coverage of a server that declares units: a FluentModbus client against the real server stack
    ///     over loopback, one connection, in the layout of the device that motivated it — units 1 and 2 at the same
    ///     addresses, and unit 200 with a map shaped differently. Address 7 is the shared one: it is inside units 1
    ///     and 2 and outside unit 200, so one map shared by every unit, or one extent shared by every unit, gives a
    ///     different observable here.
    /// </summary>
    [TestClass]
    public class ModbusTcpServerPerUnitIntegrationShould
    {
        private const ushort SharedAddress = 7;

        private ModbusTcpClient _client = null!;

        private FakeTimeProvider _clock = null!;

        private int _port;

        private LogicBlockModbusTcpServer _sut = null!;

        private ILogicBlockModbusTcpServerUnit _unit1 = null!;

        private ILogicBlockModbusTcpServerUnit _unit2 = null!;

        private ILogicBlockModbusTcpServerUnit _unit200 = null!;

        [TestInitialize]
        public void Initialize()
        {
            _clock = new FakeTimeProvider();
            _sut = Compose(_clock);
            _port = LoopbackPorts.Free();
            _sut.ListenAddress = "127.0.0.1";
            _sut.Port = _port;
            _unit1 = _sut.AddUnit(1);
            _unit1.HoldingRegisterCount = 10;
            _unit2 = _sut.AddUnit(2);
            _unit2.HoldingRegisterCount = 10;
            _unit200 = _sut.AddUnit(200);
            _unit200.HoldingRegisterCount = 5;

            // A request the server never answers would otherwise block the test run instead of failing it.
            _client = new ModbusTcpClient { ReadTimeout = 2000 };
        }

        [TestCleanup]
        public void Cleanup()
        {
            if (_client.IsConnected)
            {
                _client.Disconnect();
            }

            _sut.Dispose();
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-021.1")]
        public void ServeOwnMapPerDeclaredUnit()
        {
            // Arrange
            _unit1.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(SharedAddress, 0x0101));
            _unit2.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(SharedAddress, 0x0202));
            _sut.IsEnabled = true;
            Connect();
            var seededAtUnit1 = _client.ReadHoldingRegisters(1, SharedAddress, 1).ToArray();
            var seededAtUnit2 = _client.ReadHoldingRegisters(2, SharedAddress, 1).ToArray();

            // Act
            _client.WriteSingleRegister(2, SharedAddress, new byte[] { 0x00, 0x2A });

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x01 }, seededAtUnit1);
            CollectionAssert.AreEqual(new byte[] { 0x02, 0x02 }, seededAtUnit2);
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x2A }, _client.ReadHoldingRegisters(2, SharedAddress, 1).ToArray());
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x01 }, _client.ReadHoldingRegisters(1, SharedAddress, 1).ToArray());
            Assert.AreEqual((ushort)42, _unit2.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(SharedAddress)));
            Assert.AreEqual((ushort)0x0101, _unit1.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(SharedAddress)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-021.1")]
        public void ServeUnitZeroBesideOtherUnits()
        {
            // Arrange
            var unit0 = _sut.AddUnit(0);
            unit0.HoldingRegisterCount = 10;
            unit0.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(SharedAddress, 0x0B0B));
            _unit1.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(SharedAddress, 0x0101));
            _sut.IsEnabled = true;
            Connect();

            // Act
            var atUnit0 = _client.ReadHoldingRegisters(0, SharedAddress, 1).ToArray();
            var atUnit1 = _client.ReadHoldingRegisters(1, SharedAddress, 1).ToArray();

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x0B, 0x0B }, atUnit0);
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x01 }, atUnit1);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-021.2")]
        public void BoundEachUnitByItsOwnExtents()
        {
            // Arrange
            _sut.IsEnabled = true;
            Connect();
            _client.ReadHoldingRegisters(1, SharedAddress, 1); // inside unit 1's extent — answered
            _client.WriteSingleRegister(200, 4, new byte[] { 0x00, 0x01 }); // last address unit 200 serves — accepted

            // Act
            var read = Assert.ThrowsExactly<ModbusException>(() => _client.ReadHoldingRegisters(200, SharedAddress, 1));
            var write = Assert.ThrowsExactly<ModbusException>(() => _client.WriteSingleRegister(200, 5, new byte[] { 0x00, 0x01 }));

            // Assert
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, read.ExceptionCode);
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, write.ExceptionCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-021.3")]
        public void AnswerUndeclaredUnitWithGatewayExceptionAndKeepConnection()
        {
            // Arrange
            _unit1.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(SharedAddress, 0x0101));
            _sut.IsEnabled = true;
            Connect();

            // Act
            var read = Assert.ThrowsExactly<ModbusException>(() => _client.ReadHoldingRegisters(3, SharedAddress, 1));
            var write = Assert.ThrowsExactly<ModbusException>(() => _client.WriteSingleRegister(3, SharedAddress, new byte[] { 0x00, 0x2A }));
            var next = _client.ReadHoldingRegisters(1, SharedAddress, 1).ToArray();

            // Assert
            Assert.AreEqual(ModbusExceptionCode.GatewayTargetDeviceFailedToRespond, read.ExceptionCode);
            Assert.AreEqual(ModbusExceptionCode.GatewayTargetDeviceFailedToRespond, write.ExceptionCode);
            CollectionAssert.AreEqual(new byte[] { 0x01, 0x01 }, next);
            Assert.AreEqual(1, _sut.ConnectionCount);
            Assert.IsNull(_sut.LastClientWriteAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-021.4")]
        public void StampLastClientWritePerUnit()
        {
            // Arrange
            // No polling: the server stores a write and its change notification returns before the client's write
            // completes (FluentModbusWriteOrderingShould pins that premise for this configuration).
            _sut.IsEnabled = true;
            Connect();
            var firstWriteAt = _clock.GetUtcNow();
            _client.WriteSingleRegister(1, 0, new byte[] { 0x00, 0x01 });
            _client.WriteSingleRegister(2, 0, new byte[] { 0x00, 0x01 });
            _clock.Advance(TimeSpan.FromSeconds(1));

            // Act
            // A master re-writing an unchanged value at one unit must still count as alive there, and only there.
            _client.WriteSingleRegister(1, 0, new byte[] { 0x00, 0x01 });

            // Assert
            Assert.AreEqual(firstWriteAt.AddSeconds(1), _unit1.LastClientWriteAt);
            Assert.AreEqual(firstWriteAt, _unit2.LastClientWriteAt);
            Assert.IsNull(_unit200.LastClientWriteAt);
            Assert.AreEqual(firstWriteAt.AddSeconds(1), _sut.LastClientWriteAt);
        }

        private static LogicBlockModbusTcpServer Compose(TimeProvider clock)
        {
            var dataConverter = new ServiceCollection().AddDaleModbusCoreSdk().BuildServiceProvider().GetRequiredService<IModbusDataConverter>();

            return new LogicBlockModbusTcpServer(new ModbusTcpServerProxy(NullLogger<ModbusTcpServerProxy>.Instance, clock),
                                                 dataConverter,
                                                 NullLogger<LogicBlockModbusTcpServer>.Instance);
        }

        private void Connect()
        {
            _client.Connect(new IPEndPoint(IPAddress.Loopback, _port), ModbusEndianness.BigEndian);
        }
    }
}