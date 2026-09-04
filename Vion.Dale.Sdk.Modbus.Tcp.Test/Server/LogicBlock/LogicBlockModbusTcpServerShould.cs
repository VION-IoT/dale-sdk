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
        [TestProperty("spec", "AC-MODB-002.1")]
        public void StartProxyWithParsedConfigurationWhenEnabled()
        {
            // Arrange
            _sut.ListenAddress = "127.0.0.1";
            _sut.Port = 1502;
            _sut.HoldingRegisterCount = 10;
            _sut.InputRegisterCount = 20;
            _sut.DiscreteInputCount = 1;

            // Act
            _sut.IsEnabled = true;

            // Assert
            Assert.AreEqual(1, _proxy.StartCalls);
            Assert.AreEqual(IPAddress.Loopback, _proxy.LastListenAddress);
            Assert.AreEqual(1502, _proxy.LastPort);
            Assert.AreEqual(new ModbusServerAreaExtents(10, 20, 0, 1), _proxy.LastExtents);
            Assert.IsTrue(_sut.IsEnabled);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.1")]
        public void StopProxyWhenDisabled()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act
            _sut.IsEnabled = false;

            // Assert
            Assert.AreEqual(1, _proxy.StopCalls);
            Assert.IsFalse(_sut.IsEnabled);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.1")]
        public void BeIdempotentOnRepeatedEnable()
        {
            // Arrange

            // Act
            _sut.IsEnabled = true;
            _sut.IsEnabled = true;

            // Assert
            Assert.AreEqual(1, _proxy.StartCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-002.1")]
        public void NotStopWhenAlreadyDisabled()
        {
            // Arrange

            // Act
            _sut.IsEnabled = false;

            // Assert
            Assert.AreEqual(0, _proxy.StopCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-011.3")]
        public void PropagateBindFailures()
        {
            // Arrange
            _proxy.ThrowOnStart = new InvalidOperationException("address in use");

            // Act & Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.IsEnabled = true);
            Assert.IsFalse(_sut.IsEnabled);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-011.1")]
        public void RejectConfigurationChangesWhileEnabled()
        {
            // Arrange

            // Act
            _sut.IsEnabled = true;

            // Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.ListenAddress = "10.0.0.1");
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.Port = 503);
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.HoldingRegisterCount = 1);
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.InputRegisterCount = 1);
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.CoilCount = 1);
            Assert.ThrowsExactly<InvalidOperationException>(() => _sut.DiscreteInputCount = 1);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-011.1")]
        public void AllowReconfigurationAfterDisabling()
        {
            // Arrange
            _sut.IsEnabled = true;
            _sut.IsEnabled = false;

            // Act
            _sut.Port = 1503;
            _sut.IsEnabled = true;

            // Assert
            Assert.AreEqual(1503, _proxy.LastPort);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-006.4")]
        public void RejectInvalidListenAddress()
        {
            // Act & Assert
            Assert.ThrowsExactly<FormatException>(() => _sut.ListenAddress = "not-an-ip");
            Assert.ThrowsExactly<FormatException>(() => _sut.ListenAddress = null);
            Assert.ThrowsExactly<FormatException>(() => _sut.ListenAddress = " ");
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-006.3")]
        public void RejectInvalidPort()
        {
            // Act & Assert
            Assert.ThrowsExactly<FormatException>(() => _sut.Port = -1);
            Assert.ThrowsExactly<FormatException>(() => _sut.Port = 0);
            Assert.ThrowsExactly<FormatException>(() => _sut.Port = 65536);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-011.2")]
        public void DefaultToAnyAddressAndPort502()
        {
            // Arrange

            // Act
            _sut.IsEnabled = true;

            // Assert
            Assert.AreEqual("0.0.0.0", _sut.ListenAddress);
            Assert.AreEqual(IPAddress.Any, _proxy.LastListenAddress);
            Assert.AreEqual(502, _proxy.LastPort);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.1")]
        public void ExecuteSyncUnderProxyLock()
        {
            // Arrange
            var lockHeld = false;

            // Act
            _sut.Sync(_ => lockHeld = Monitor.IsEntered(_proxy.Lock));

            // Assert
            Assert.IsTrue(lockHeld);
            Assert.IsFalse(Monitor.IsEntered(_proxy.Lock));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-012.1")]
        public void ExposeAllFourAreasInSnapshot()
        {
            // Arrange
            _sut.HoldingRegisterCount = 10;
            _sut.InputRegisterCount = 10;
            _sut.CoilCount = 8;
            _sut.DiscreteInputCount = 8;

            // Act
            _sut.Sync(snapshot =>
                      {
                          snapshot.HoldingRegisters.WriteAsUShort(0, 0x1234);
                          snapshot.InputRegisters.WriteAsUShort(1, 0xBEEF);
                          snapshot.Coils.Write(0, true);
                          snapshot.DiscreteInputs.Write(3, true);
                      });

            // Assert
            CollectionAssert.AreEqual(new byte[] { 0x12, 0x34 }, new[] { _proxy.HoldingRegisters[0], _proxy.HoldingRegisters[1] });
            CollectionAssert.AreEqual(new byte[] { 0xBE, 0xEF }, new[] { _proxy.InputRegisters[2], _proxy.InputRegisters[3] });
            Assert.AreEqual(0b0000_0001, _proxy.Coils[0]);
            Assert.AreEqual(0b0000_1000, _proxy.DiscreteInputs[0]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.1")]
        public void ReturnSyncCallbackResult()
        {
            // Arrange
            _sut.HoldingRegisterCount = 1;
            _proxy.HoldingRegisters[0] = 0x00;
            _proxy.HoldingRegisters[1] = 0x2A;

            // Act
            var value = _sut.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(0));

            // Assert
            Assert.AreEqual((ushort)42, value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.2")]
        public void AllowSyncWhileDisabled()
        {
            // Arrange
            _sut.HoldingRegisterCount = 1;

            // Act
            _sut.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(0, 7));

            // Assert
            Assert.IsFalse(_sut.IsEnabled);
            Assert.AreEqual(7, _proxy.HoldingRegisters[1]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-012.3")]
        public void EnforceConfiguredExtentsInSnapshot()
        {
            // Arrange

            // Act
            _sut.HoldingRegisterCount = 10;

            // Assert
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(10)));
            Assert.ThrowsExactly<InvalidServerAddressException>(() => _sut.Sync(snapshot => snapshot.InputRegisters.ReadAsUShort(0)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-014.5")]
        public void PassDiagnosticsThrough()
        {
            // Arrange
            _proxy.IsListening = true;
            _proxy.ConnectionCount = 3;
            var writeTime = DateTimeOffset.UtcNow;
            _proxy.LastClientWriteAt = writeTime;

            // Act & Assert
            Assert.IsTrue(_sut.IsListening);
            Assert.AreEqual(3, _sut.ConnectionCount);
            Assert.AreEqual(writeTime, _sut.LastClientWriteAt);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-011.4")]
        public void DisposeProxy()
        {
            // Arrange

            // Act
            _sut.Dispose();

            // Assert
            Assert.AreEqual(1, _proxy.DisposeCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.4")]
        public void KeepRejectingDisablingAfterNestedSyncCallbackReturns()
        {
            // Arrange
            // A nested Sync returning must not disarm the outer callback's guard: the server lock is re-entrant,
            // so the outer callback still holds it and stopping the listener there still deadlocks.
            _sut.IsEnabled = true;

            // Act
            _sut.Sync(_ =>
                      {
                          _sut.Sync(_ => { });
                          Assert.ThrowsExactly<InvalidOperationException>(() => _sut.IsEnabled = false);
                      });

            // Assert
            Assert.IsTrue(_sut.IsEnabled);
            Assert.AreEqual(0, _proxy.StopCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.4")]
        public void KeepRejectingDisposalAfterNestedSyncCallbackReturns()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act
            _sut.Sync(_ =>
                      {
                          _sut.Sync(_ => { });
                          Assert.ThrowsExactly<InvalidOperationException>(() => _sut.Dispose());
                      });

            // Assert
            Assert.AreEqual(0, _proxy.DisposeCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-011.4")]
        public void ReadAsDisabledOnceDisposed()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act
            _sut.Dispose();

            // Assert
            Assert.IsFalse(_sut.IsEnabled);
            Assert.AreEqual(1, _proxy.DisposeCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-011.4")]
        public void StaySilentWhenDisposedTwice()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act
            _sut.Dispose();
            _sut.Dispose();

            // Assert
            Assert.IsFalse(_sut.IsEnabled);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.5")]
        public void RefuseSnapshotCapturedPastItsCallback()
        {
            // Arrange
            // The snapshot's accessors write the live server buffer without the lock once the callback has
            // returned, which the interface warns against and nothing enforced.
            _sut.HoldingRegisterCount = 10;
            _sut.CoilCount = 10;
            IModbusServerSnapshot? captured = null;

            // Act
            _sut.Sync(snapshot => captured = snapshot);

            // Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => captured!.HoldingRegisters.ReadAsUShort(0));
            Assert.ThrowsExactly<InvalidOperationException>(() => captured!.HoldingRegisters.WriteAsUShort(0, 1));
            Assert.ThrowsExactly<InvalidOperationException>(() => captured!.Coils.Read(0));
            Assert.ThrowsExactly<InvalidOperationException>(() => captured!.Coils.Write(0, true));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.1")]
        public void GiveEachSyncCallbackItsOwnLiveSnapshot()
        {
            // Arrange
            _sut.HoldingRegisterCount = 10;

            // Act
            _sut.Sync(snapshot => snapshot.HoldingRegisters.WriteAsUShort(0, 4242));
            var readBack = _sut.Sync(snapshot => snapshot.HoldingRegisters.ReadAsUShort(0));

            // Assert
            Assert.AreEqual((ushort)4242, readBack);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.3")]
        public void RejectDisablingFromInsideSyncCallback()
        {
            // Arrange
            // Stopping the listener joins request-handler threads that may be waiting for the server lock the
            // callback holds — allowing this would deadlock the actor thread permanently.
            _sut.IsEnabled = true;

            // Act
            _sut.Sync(_ => Assert.ThrowsExactly<InvalidOperationException>(() => _sut.IsEnabled = false));

            // Assert
            Assert.IsTrue(_sut.IsEnabled);
            Assert.AreEqual(0, _proxy.StopCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.3")]
        public void RejectEnablingFromInsideSyncCallback()
        {
            // Arrange

            // Act
            _sut.Sync(_ => Assert.ThrowsExactly<InvalidOperationException>(() => _sut.IsEnabled = true));

            // Assert
            Assert.AreEqual(0, _proxy.StartCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.3")]
        public void RejectDisposingFromInsideSyncCallback()
        {
            // Arrange

            // Act
            _sut.Sync(_ => Assert.ThrowsExactly<InvalidOperationException>(() => _sut.Dispose()));

            // Assert
            Assert.AreEqual(0, _proxy.DisposeCalls);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-013.3")]
        public void AllowDisablingAfterSyncCallbackReturns()
        {
            // Arrange
            _sut.IsEnabled = true;

            // Act
            _sut.Sync(_ => { });
            _sut.IsEnabled = false;

            // Assert
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