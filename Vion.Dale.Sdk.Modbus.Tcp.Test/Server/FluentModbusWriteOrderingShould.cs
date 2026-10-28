using System;
using System.Collections.Generic;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Modbus.Core.Server;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;
using Vion.Dale.Sdk.Modbus.Tcp.Test.TestHelpers;

namespace Vion.Dale.Sdk.Modbus.Tcp.Test.Server
{
    /// <summary>
    ///     The ordering the hosted Modbus TCP server inherits from FluentModbus: a client's register or coil write is stored
    ///     and its change notification raised before the response is sent, so the client's write does not complete while the
    ///     notification is still running.
    ///     <para>
    ///         No test here cites a criterion: they pin the premise the stepped host's accounting rests on — the client's
    ///         counted request spans the server's handling of the write, so the server counts nothing of its own
    ///         (<c>AC-SCEN-012.5</c>, and the prose under <c>AC-MODB-020.1</c>). A FluentModbus upgrade that answered before
    ///         notifying would leave a stepped settle free to return before the server had stored the write.
    ///     </para>
    ///     <para>
    ///         The ordering is pinned twice: for unit 0 alone, as a server that declares no unit serves every unit
    ///         identifier, and for several declared units with a map each, driven through the hosted proxy itself, as a
    ///         server that declares units serves them.
    ///     </para>
    /// </summary>
    [TestClass]
    public class FluentModbusWriteOrderingShould
    {
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        // How long a write is watched for completing while the notification is parked. A write that completes early is
        // what would falsify the premise, and a slow machine cannot cause that.
        private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(200);

        [TestMethod]
        [DataRow(ModbusFunctionCode.WriteSingleRegister, DisplayName = "WriteSingleRegister")]
        [DataRow(ModbusFunctionCode.WriteMultipleRegisters, DisplayName = "WriteMultipleRegisters")]
        [DataRow(ModbusFunctionCode.WriteSingleCoil, DisplayName = "WriteSingleCoil")]
        [DataRow(ModbusFunctionCode.WriteMultipleCoils, DisplayName = "WriteMultipleCoils")]
        public async Task HoldClientWriteWhileChangeNotificationRuns(ModbusFunctionCode functionCode)
        {
            // Arrange — notification handlers that park until released, recording whether the write was stored when they ran.
            using var entered = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            var storedWhenNotified = false;
            var server = new ModbusTcpServer { EnableRaisingEvents = true, AlwaysRaiseChangedEvent = true };
            server.AddUnit(0);
            server.RegistersChanged += (_, _) => Park(() => server.GetHoldingRegisters()[1] == 42);
            server.CoilsChanged += (_, _) => Park(() => (server.GetCoils()[0] & 0b10) != 0);
            var port = LoopbackPorts.Free();
            server.Start(new IPEndPoint(IPAddress.Loopback, port));
            using var client = new ModbusTcpClient();
            client.Connect(new IPEndPoint(IPAddress.Loopback, port), ModbusEndianness.LittleEndian);

            try
            {
                // Act / Assert — inseparable: the write must be seen not completing while the notification is still parked.
                var write = Task.Run(() => Write(client, 0, functionCode));
                Assert.IsTrue(entered.Wait(Timeout));
                await Assert.ThrowsExactlyAsync<TimeoutException>(() => write.WaitAsync(Window));
                released.Set();
                await write.WaitAsync(Timeout);

                // Assert — the value was already stored when the notification ran.
                Assert.IsTrue(storedWhenNotified);
            }
            finally
            {
                released.Set();
                server.Stop();
            }

            void Park(Func<bool> stored)
            {
                storedWhenNotified = stored();
                entered.Set();
                released.Wait(Timeout);
            }
        }

        [TestMethod]
        [DataRow(ModbusFunctionCode.WriteSingleRegister, DisplayName = "WriteSingleRegister")]
        [DataRow(ModbusFunctionCode.WriteMultipleRegisters, DisplayName = "WriteMultipleRegisters")]
        [DataRow(ModbusFunctionCode.WriteSingleCoil, DisplayName = "WriteSingleCoil")]
        [DataRow(ModbusFunctionCode.WriteMultipleCoils, DisplayName = "WriteMultipleCoils")]
        public async Task HoldClientWriteToOneOfSeveralHostedUnitsWhileChangeNotificationRuns(ModbusFunctionCode functionCode)
        {
            // Arrange — the hosted proxy serving units 1 and 2 at the same addresses, and a third unit. Unit 1 sits at the
            // written address untouched, which is what tells a map per unit from one map shared by all. The proxy reads
            // its clock from inside the change notification, so a clock that parks is the notification parking.
            using var entered = new ManualResetEventSlim();
            using var released = new ManualResetEventSlim();
            var storedWhenNotified = false;
            var siblingUntouchedWhenNotified = false;
            var clock = new ParkingClock();
            var server = new ModbusTcpServerProxy(NullLogger<ModbusTcpServerProxy>.Instance, clock);
            var unitExtents = new Dictionary<byte, ModbusServerAreaExtents>();
            foreach (var unitId in new byte[] { 1, 2, 200 })
            {
                server.AddUnit(unitId);
                unitExtents[unitId] = new ModbusServerAreaExtents(10, 0, 10, 0);
            }

            clock.Parked = () => Park(() => BitConverter.ToInt16(server.GetHoldingRegisterBuffer(2).Slice(2, 2)) == 42 || (server.GetCoilBuffer(2)[0] & 0b10) != 0,
                                      () => BitConverter.ToInt16(server.GetHoldingRegisterBuffer(1).Slice(2, 2)) == 0 && (server.GetCoilBuffer(1)[0] & 0b10) == 0);
            var port = LoopbackPorts.Free();
            server.Start(IPAddress.Loopback, port, unitExtents);
            using var client = new ModbusTcpClient();
            client.Connect(new IPEndPoint(IPAddress.Loopback, port), ModbusEndianness.LittleEndian);

            try
            {
                // Act / Assert — inseparable: the write must be seen not completing while the notification is still parked.
                var write = Task.Run(() => Write(client, 2, functionCode));
                Assert.IsTrue(entered.Wait(Timeout));
                await Assert.ThrowsExactlyAsync<TimeoutException>(() => write.WaitAsync(Window));
                released.Set();
                await write.WaitAsync(Timeout);

                // Assert — the value was already stored in the written unit's map, and only there, when the notification ran.
                Assert.IsTrue(storedWhenNotified);
                Assert.IsTrue(siblingUntouchedWhenNotified);
            }
            finally
            {
                released.Set();
                server.Dispose();
            }

            void Park(Func<bool> stored, Func<bool> siblingUntouched)
            {
                storedWhenNotified = stored();
                siblingUntouchedWhenNotified = siblingUntouched();
                entered.Set();
                released.Wait(Timeout);
            }
        }

        private static void Write(ModbusTcpClient client, byte unitIdentifier, ModbusFunctionCode functionCode)
        {
            switch (functionCode)
            {
                case ModbusFunctionCode.WriteSingleRegister:
                    client.WriteSingleRegister(unitIdentifier, 1, 42);
                    break;
                case ModbusFunctionCode.WriteMultipleRegisters:
                    client.WriteMultipleRegisters(unitIdentifier, 1, new short[] { 42 });
                    break;
                case ModbusFunctionCode.WriteSingleCoil:
                    client.WriteSingleCoil(unitIdentifier, 1, true);
                    break;
                case ModbusFunctionCode.WriteMultipleCoils:
                    client.WriteMultipleCoils(unitIdentifier, 1, new[] { true });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(functionCode), functionCode, "Not a write this test drives.");
            }
        }

        /// <summary>The system clock, running <see cref="Parked" /> on the caller's thread each time it is read.</summary>
        private sealed class ParkingClock : TimeProvider
        {
            public Action? Parked { get; set; }

            public override DateTimeOffset GetUtcNow()
            {
                Parked?.Invoke();

                return base.GetUtcNow();
            }
        }
    }
}