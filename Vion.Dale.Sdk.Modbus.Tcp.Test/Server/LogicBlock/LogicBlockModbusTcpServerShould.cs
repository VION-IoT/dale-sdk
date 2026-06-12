using System;
using System.Net;
using System.Threading;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Modbus.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Server;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Server.LogicBlock
{
    [TestClass]
    public class LogicBlockModbusTcpServerShould
    {
        private StubModbusTcpServerProxy _proxy = null!;

        private LogicBlockModbusTcpServer _sut = null!;

        [TestInitialize]
        public void Initialize()
        {
            _proxy = new StubModbusTcpServerProxy();
            var dataConverter = new ServiceCollection().AddDaleModbusCoreSdk().BuildServiceProvider().GetRequiredService<IModbusDataConverter>();
            _sut = new LogicBlockModbusTcpServer(_proxy, dataConverter, NullLogger<LogicBlockModbusTcpServer>.Instance);
        }

        [TestMethod]
        public void StartTheProxyWithParsedConfigurationWhenEnabled()
        {
            _sut.ListenAddress = "127.0.0.1";
            _sut.Port = 1502;
            _sut.HoldingRegisterCount = 10;
            _sut.InputRegisterCount = 20;
            _sut.DiscreteInputCount = 1;

            _sut.IsEnabled = true;

            Assert.AreEqual(1, _proxy.StartCalls);
            Assert.AreEqual(IPAddress.Loopback, _proxy.LastListenAddress);
            Assert.AreEqual(1502, _proxy.LastPort);
            Assert.AreEqual(new ModbusServerAreaExtents(10, 20, 0, 1), _proxy.LastExtents);
            Assert.IsTrue(_sut.IsEnabled);
        }

        [TestMethod]
        public void StopTheProxyWhenDisabled()
        {
            _sut.IsEnabled = true;

            _sut.IsEnabled = false;

            Assert.AreEqual(1, _proxy.StopCalls);
            Assert.IsFalse(_sut.IsEnabled);
        }

        [TestMethod]
        public void BeIdempotentOnRepeatedEnable()
        {
            _sut.IsEnabled = true;
            _sut.IsEnabled = true;

            Assert.AreEqual(1, _proxy.StartCalls);
        }

        [TestMethod]
        public void NotStopWhenAlreadyDisabled()
        {
            _sut.IsEnabled = false;

            Assert.AreEqual(0, _proxy.StopCalls);
        }

        [TestMethod]
        public void PropagateBindFailures()
        {
            _proxy.ThrowOnStart = new InvalidOperationException("address in use");

            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.IsEnabled = true);
            Assert.IsFalse(_sut.IsEnabled);
        }

        [TestMethod]
        public void RejectConfigurationChangesWhileEnabled()
        {
            _sut.IsEnabled = true;

            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.ListenAddress = "10.0.0.1");
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.Port = 503);
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.HoldingRegisterCount = 1);
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.InputRegisterCount = 1);
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.CoilCount = 1);
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.DiscreteInputCount = 1);
        }

        [TestMethod]
        public void AllowReconfigurationAfterDisabling()
        {
            _sut.IsEnabled = true;
            _sut.IsEnabled = false;

            _sut.Port = 1503;
            _sut.IsEnabled = true;

            Assert.AreEqual(1503, _proxy.LastPort);
        }

        [TestMethod]
        public void RejectInvalidListenAddress()
        {
            Assert.ThrowsExactly<FormatException>(() => _sut.ListenAddress = "not-an-ip");
            Assert.ThrowsExactly<FormatException>(() => _sut.ListenAddress = null);
            Assert.ThrowsExactly<FormatException>(() => _sut.ListenAddress = " ");
        }

        [TestMethod]
        public void RejectInvalidPort()
        {
            Assert.ThrowsExactly<FormatException>(() => _sut.Port = -1);
            Assert.ThrowsExactly<FormatException>(() => _sut.Port = 65536);
        }

        [TestMethod]
        public void DefaultToAnyAddressAndPort502()
        {
            _sut.IsEnabled = true;

            Assert.AreEqual("0.0.0.0", _sut.ListenAddress);
            Assert.AreEqual(IPAddress.Any, _proxy.LastListenAddress);
            Assert.AreEqual(502, _proxy.LastPort);
        }

        [TestMethod]
        public void ExecuteSyncUnderTheProxyLock()
        {
            var lockHeld = false;

            _sut.Sync(_ => lockHeld = Monitor.IsEntered(_proxy.Lock));

            Assert.IsTrue(lockHeld);
            Assert.IsFalse(Monitor.IsEntered(_proxy.Lock));
        }

        [TestMethod]
        public void ExposeAllFourAreasInTheSnapshot()
        {
            _sut.HoldingRegisterCount = 10;
            _sut.InputRegisterCount = 10;
            _sut.CoilCount = 8;
            _sut.DiscreteInputCount = 8;

            _sut.Sync(snapshot =>
                      {
                          snapshot.HoldingRegisters.WriteAsUShort(0, 0x1234);
                          snapshot.InputRegisters.WriteAsUShort(1, 0xBEEF);
                          snapshot.Coils.Write(0, true);
                          snapshot.DiscreteInputs.Write(3, true);
                      });

            CollectionAssert.AreEqual(new byte[] { 0x12, 0x34 }, new[] { _proxy.HoldingRegisters[0], _proxy.HoldingRegisters[1] });
            CollectionAssert.AreEqual(new byte[] { 0xBE, 0xEF }, new[] { _proxy.InputRegisters[2], _proxy.InputRegisters[3] });
            Assert.AreEqual(0b0000_0001, _proxy.Coils[0]);
            Assert.AreEqual(0b0000_1000, _proxy.DiscreteInputs[0]);
        }

        [TestMethod]
        public void ReturnTheSyncCallbackResult()
        {
            _sut.HoldingRegisterCount = 1;
            _proxy.HoldingRegisters[0] = 0x00;
            _proxy.HoldingRegisters[1] = 0x2A;

            var value = _sut.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(0));

            Assert.AreEqual((ushort)42, value);
        }

        [TestMethod]
        public void AllowSyncWhileDisabled()
        {
            _sut.HoldingRegisterCount = 1;

            _sut.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(0, 7));

            Assert.IsFalse(_sut.IsEnabled);
            Assert.AreEqual(7, _proxy.HoldingRegisters[1]);
        }

        [TestMethod]
        public void EnforceTheConfiguredExtentsInTheSnapshot()
        {
            _sut.HoldingRegisterCount = 10;

            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(10)));
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.Sync(snapshot => snapshot.InputRegisters.ReadAsUShort(0)));
        }

        [TestMethod]
        public void PassDiagnosticsThrough()
        {
            _proxy.IsListening = true;
            _proxy.ConnectionCount = 3;
            var writeTime = DateTimeOffset.UtcNow;
            _proxy.LastClientWriteAt = writeTime;

            Assert.IsTrue(_sut.IsListening);
            Assert.AreEqual(3, _sut.ConnectionCount);
            Assert.AreEqual(writeTime, _sut.LastClientWriteAt);
        }

        [TestMethod]
        public void DisposeTheProxy()
        {
            _sut.Dispose();

            Assert.AreEqual(1, _proxy.DisposeCalls);
        }

        [TestMethod]
        public void RejectDisablingFromInsideASyncCallback()
        {
            // Stopping the listener joins request-handler threads that may be waiting for the server lock the
            // callback holds — allowing this would deadlock the actor thread permanently.
            _sut.IsEnabled = true;

            _sut.Sync(_ => Assert.ThrowsExactly<InvalidOperationException>(() => _sut.IsEnabled = false));

            Assert.IsTrue(_sut.IsEnabled);
            Assert.AreEqual(0, _proxy.StopCalls);
        }

        [TestMethod]
        public void RejectEnablingFromInsideASyncCallback()
        {
            _sut.Sync(_ => Assert.ThrowsExactly<InvalidOperationException>(() => _sut.IsEnabled = true));

            Assert.AreEqual(0, _proxy.StartCalls);
        }

        [TestMethod]
        public void RejectDisposingFromInsideASyncCallback()
        {
            _sut.Sync(_ => Assert.ThrowsExactly<InvalidOperationException>(() => _sut.Dispose()));

            Assert.AreEqual(0, _proxy.DisposeCalls);
        }

        [TestMethod]
        public void AllowDisablingAfterTheSyncCallbackReturns()
        {
            _sut.IsEnabled = true;

            _sut.Sync(_ => { });
            _sut.IsEnabled = false;

            Assert.AreEqual(1, _proxy.StopCalls);
        }

        private sealed class StubModbusTcpServerProxy : IModbusTcpServerProxy
        {
            public byte[] Coils { get; } = new byte[65536 / 8];

            public byte[] DiscreteInputs { get; } = new byte[65536 / 8];

            public int DisposeCalls { get; private set; }

            public byte[] HoldingRegisters { get; } = new byte[2 * 65536];

            public byte[] InputRegisters { get; } = new byte[2 * 65536];

            public ModbusServerAreaExtents LastExtents { get; private set; }

            public IPAddress? LastListenAddress { get; private set; }

            public int LastPort { get; private set; }

            public int StartCalls { get; private set; }

            public int StopCalls { get; private set; }

            public Exception? ThrowOnStart { get; set; }

            public int ConnectionCount { get; set; }

            public bool IsListening { get; set; }

            public DateTimeOffset? LastClientWriteAt { get; set; }

            public object Lock { get; } = new();

            public void Dispose()
            {
                DisposeCalls++;
            }

            public Span<byte> GetCoilBuffer()
            {
                return Coils;
            }

            public Span<byte> GetDiscreteInputBuffer()
            {
                return DiscreteInputs;
            }

            public Span<byte> GetHoldingRegisterBuffer()
            {
                return HoldingRegisters;
            }

            public Span<byte> GetInputRegisterBuffer()
            {
                return InputRegisters;
            }

            public void Start(IPAddress listenAddress, int port, ModbusServerAreaExtents extents)
            {
                if (ThrowOnStart != null)
                {
                    throw ThrowOnStart;
                }

                StartCalls++;
                LastListenAddress = listenAddress;
                LastPort = port;
                LastExtents = extents;
                IsListening = true;
            }

            public void Stop()
            {
                StopCalls++;
                IsListening = false;
            }
        }
    }
}