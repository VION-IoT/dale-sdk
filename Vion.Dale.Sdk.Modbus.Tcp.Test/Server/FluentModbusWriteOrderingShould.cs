using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using FluentModbus;

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
            var port = FreePort();
            server.Start(new IPEndPoint(IPAddress.Loopback, port));
            using var client = new ModbusTcpClient();
            client.Connect(new IPEndPoint(IPAddress.Loopback, port), ModbusEndianness.LittleEndian);

            try
            {
                // Act / Assert — inseparable: the write must be seen not completing while the notification is still parked.
                var write = Task.Run(() => Write(client, functionCode));
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

        private static void Write(ModbusTcpClient client, ModbusFunctionCode functionCode)
        {
            switch (functionCode)
            {
                case ModbusFunctionCode.WriteSingleRegister:
                    client.WriteSingleRegister(0, 1, 42);
                    break;
                case ModbusFunctionCode.WriteMultipleRegisters:
                    client.WriteMultipleRegisters(0, 1, new short[] { 42 });
                    break;
                case ModbusFunctionCode.WriteSingleCoil:
                    client.WriteSingleCoil(0, 1, true);
                    break;
                case ModbusFunctionCode.WriteMultipleCoils:
                    client.WriteMultipleCoils(0, 1, new[] { true });
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(functionCode), functionCode, "Not a write this test drives.");
            }
        }

        private static int FreePort()
        {
            var listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            var port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();

            return port;
        }
    }
}