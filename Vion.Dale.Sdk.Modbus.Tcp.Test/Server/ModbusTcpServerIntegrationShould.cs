using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using FluentModbus;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Modbus.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock;
using WordOrder32 = Vion.Dale.Sdk.Modbus.Core.Conversion.WordOrder32;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Server
{
    /// <summary>
    ///     Real-socket coverage: a FluentModbus client against the real server stack over loopback.
    ///     Verifies the id-agnostic endpoint, wire-exact bytes, extent validation, and restartability —
    ///     the byte-level sanity checks the in-memory paths cannot provide.
    /// </summary>
    [TestClass]
    public class ModbusTcpServerIntegrationShould
    {
        private ModbusTcpClient _client = null!;

        private int _port;

        private LogicBlockModbusTcpServer _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            var dataConverter = new ServiceCollection().AddDaleModbusCoreSdk().BuildServiceProvider().GetRequiredService<IModbusDataConverter>();
            _sut = new LogicBlockModbusTcpServer(new ModbusTcpServerProxy(NullLogger<ModbusTcpServerProxy>.Instance, TimeProvider.System),
                                                 dataConverter,
                                                 NullLogger<LogicBlockModbusTcpServer>.Instance);
            _port = GetFreePort();
            _sut.ListenAddress = "127.0.0.1";
            _sut.Port = _port;
            _sut.HoldingRegisterCount = 10;
            _sut.InputRegisterCount = 20;
            _sut.CoilCount = 7;
            _sut.DiscreteInputCount = 1;
            _client = new ModbusTcpClient();
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
        public void ServeAnyUnitIdentifier()
        {
            _sut.IsEnabled = true;
            Connect();

            _client.WriteSingleRegister(7, 1, new byte[] { 0x00, 0x2A });
            var viaUnit255 = _client.ReadHoldingRegisters(0xFF, 1, 1);

            Assert.AreEqual((ushort)42, _sut.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(1)));
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x2A }, viaUnit255.ToArray());
        }

        [TestMethod]
        public void PublishInputRegistersToTheWire()
        {
            _sut.IsEnabled = true;
            Connect();

            _sut.Sync(snapshot =>
                      {
                          snapshot.InputRegisters.WriteAsInt(0, -123456, wordOrder: WordOrder32.LswToMsw);
                          snapshot.InputRegisters.WriteAsUShort(2, 0xBEEF);
                      });

            var wireBytes = _client.ReadInputRegisters(1, 0, 3).ToArray();

            // -123456 = 0xFFFE1DC0; LswToMsw: register 0 = LSW 0x1DC0, register 1 = MSW 0xFFFE — each big-endian on the wire.
            CollectionAssert.AreEqual(new byte[] { 0x1D, 0xC0, 0xFF, 0xFE, 0xBE, 0xEF }, wireBytes);
        }

        [TestMethod]
        public void RejectOutOfExtentAccessWithIllegalDataAddress()
        {
            _sut.IsEnabled = true;
            Connect();

            _client.WriteSingleRegister(1, 9, new byte[] { 0x00, 0x01 }); // last served address — accepted

            var write = Assert.ThrowsExactly<ModbusException>(() => _client.WriteSingleRegister(1, 10, new byte[] { 0x00, 0x01 }));
            var read = Assert.ThrowsExactly<ModbusException>(() => _client.ReadInputRegisters(1, 20, 1));

            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, write.ExceptionCode);
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, read.ExceptionCode);
        }

        [TestMethod]
        public void RoundTripTheVgtShapedCycle()
        {
            _sut.IsEnabled = true;
            Connect();

            // The trading center writes a heartbeat and a 32-bit setpoint (low-word-first, Beckhoff layout).
            _client.WriteSingleRegister(1, 0, new byte[] { 0x00, 0x07 });
            _client.WriteMultipleRegisters(1, 1, new byte[] { 0x1D, 0xC0, 0xFF, 0xFE }); // -123456 LswToMsw

            // The block's tick: read commands, echo the heartbeat, publish readiness — one atomic Sync.
            var (heartbeat, setpoint) = _sut.Sync(snapshot =>
                                                  {
                                                      var receivedHeartbeat = snapshot.HoldingRegisters.ReadAsUShort(0);
                                                      var receivedSetpoint = snapshot.HoldingRegisters.ReadAsInt(1, wordOrder: WordOrder32.LswToMsw);
                                                      snapshot.InputRegisters.WriteAsUShort(0, receivedHeartbeat);
                                                      snapshot.DiscreteInputs.Write(0, true);

                                                      return (receivedHeartbeat, receivedSetpoint);
                                                  });

            Assert.AreEqual((ushort)7, heartbeat);
            Assert.AreEqual(-123456, setpoint);
            CollectionAssert.AreEqual(new byte[] { 0x00, 0x07 }, _client.ReadInputRegisters(1, 0, 1).ToArray());
            Assert.AreEqual(0b0000_0001, _client.ReadDiscreteInputs(1, 0, 1).ToArray()[0]);
        }

        [TestMethod]
        public void ValidateBothRangesOfReadWriteMultipleRegisters()
        {
            // FC23 carries independent read and write ranges; FluentModbus invokes the RequestValidator once
            // per range (verified in the 5.3.2 source), so BOTH must pass the declared holding extent. This
            // test pins that wire behavior so a FluentModbus upgrade cannot silently drop one of the checks.
            _sut.IsEnabled = true;
            Connect();

            // Both ranges inside the extent (holding registers 0-9): succeeds, write lands, read echoes.
            var echoed = _client.ReadWriteMultipleRegisters(1, 2, 1, 2, new byte[] { 0xAB, 0xCD }).ToArray();
            CollectionAssert.AreEqual(new byte[] { 0xAB, 0xCD }, echoed);

            // Read range out of extent: rejected even though the write range is fine.
            var readOutOfRange = Assert.ThrowsExactly<ModbusException>(() => _client.ReadWriteMultipleRegisters(1, 10, 1, 0, new byte[] { 0x00, 0x01 }));
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, readOutOfRange.ExceptionCode);

            // Write range out of extent: rejected even though the read range is fine.
            var writeOutOfRange = Assert.ThrowsExactly<ModbusException>(() => _client.ReadWriteMultipleRegisters(1, 0, 1, 10, new byte[] { 0x00, 0x01 }));
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, writeOutOfRange.ExceptionCode);
        }

        [TestMethod]
        public void SurviveDisableEnableCycles()
        {
            _sut.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(5, 0xCAFE)); // seeded while disabled

            _sut.IsEnabled = true;
            Connect();
            CollectionAssert.AreEqual(new byte[] { 0xCA, 0xFE }, _client.ReadHoldingRegisters(1, 5, 1).ToArray());
            _client.Disconnect();

            _sut.IsEnabled = false;
            Assert.IsFalse(_sut.IsListening);

            _sut.IsEnabled = true;
            Assert.IsTrue(_sut.IsListening);
            Connect();
            CollectionAssert.AreEqual(new byte[] { 0xCA, 0xFE }, _client.ReadHoldingRegisters(1, 5, 1).ToArray());
        }

        [TestMethod]
        public void TrackLastClientWriteAt()
        {
            _sut.IsEnabled = true;
            Connect();
            Assert.IsNull(_sut.LastClientWriteAt);

            var before = DateTimeOffset.UtcNow;
            _client.WriteSingleRegister(1, 0, new byte[] { 0x00, 0x01 });

            var first = WaitForLastClientWriteAt(null);
            Assert.IsNotNull(first);
            Assert.IsTrue(first >= before.AddSeconds(-1));

            // A master re-writing an UNCHANGED value must still count as alive (FC6 raises no change event
            // unless AlwaysRaiseChangedEvent is set — the comm-surveillance contract depends on it).
            _client.WriteSingleRegister(1, 0, new byte[] { 0x00, 0x01 });

            var second = WaitForLastClientWriteAt(first);
            Assert.IsNotNull(second);
            Assert.IsTrue(second > first);
        }

        private DateTimeOffset? WaitForLastClientWriteAt(DateTimeOffset? after)
        {
            // The change notification fires on the server's request thread — poll briefly.
            var stopwatch = Stopwatch.StartNew();
            while ((_sut.LastClientWriteAt == null || _sut.LastClientWriteAt <= after) && stopwatch.ElapsedMilliseconds < 1000)
            {
                Thread.Sleep(10);
            }

            return _sut.LastClientWriteAt;
        }

        private void Connect()
        {
            _client.Connect(new IPEndPoint(IPAddress.Loopback, _port), ModbusEndianness.BigEndian);
        }

        private static int GetFreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }
    }
}