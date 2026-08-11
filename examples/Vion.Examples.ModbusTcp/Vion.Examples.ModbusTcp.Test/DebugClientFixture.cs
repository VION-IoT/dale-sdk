using System;
using System.Collections.Generic;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Tcp;
using Vion.Dale.Sdk.Modbus.Tcp.Client.LogicBlock;
using Vion.Dale.Sdk.Modbus.Tcp.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.ModbusTcp.LogicBlocks;

namespace Vion.Examples.ModbusTcp.Test
{
    /// <summary>
    ///     Builds a <see cref="ModbusTcpDebugClient" /> on top of two independent fake connections — one for
    ///     the poll loop, one for manual commands — matching the two clients the block creates. Keeping the
    ///     two fakes separate is what lets a test say which connection carried a given request.
    /// </summary>
    internal sealed class DebugClientFixture : IDisposable
    {
        private readonly ServiceProvider _serviceProvider;

        /// <summary>Register store and history of the polling connection.</summary>
        public FakeModbusTcpClientProxy PollProxy { get; } = new();

        /// <summary>Register store and history of the manual command connection.</summary>
        public FakeModbusTcpClientProxy CommandProxy { get; } = new();

        public FakeModbusTcpHarness PollHarness { get; }

        public FakeModbusTcpHarness CommandHarness { get; }

        public ModbusTcpDebugClient Sut { get; }

        public DebugClientFixture()
        {
            PollHarness = new FakeModbusTcpHarness(PollProxy);
            CommandHarness = new FakeModbusTcpHarness(CommandProxy);

            // The converter is resolved from a real SDK graph rather than constructed directly, so the
            // block decodes with exactly the implementation production uses.
            _serviceProvider = new ServiceCollection().AddDaleModbusTcpSdk().BuildServiceProvider();

            var factory = new QueuedClientFactory(PollHarness.Client, CommandHarness.Client);
            Sut = new ModbusTcpDebugClient(factory, _serviceProvider.GetRequiredService<IModbusDataConverter>(), LogicBlockTestHelper.CreateLoggerMock().Object);
        }

        public void Dispose()
        {
            PollHarness.Dispose();
            CommandHarness.Dispose();
            _serviceProvider.Dispose();
        }

        /// <summary>
        ///     Seeds the same holding registers on both connections, so it does not matter which one a test
        ///     ends up reading through.
        /// </summary>
        public void SeedHoldingRegisters(ushort startingAddress, byte[] registerBytes, int unitId = 1)
        {
            PollProxy.SetHoldingRegisters(unitId, startingAddress, registerBytes);
            CommandProxy.SetHoldingRegisters(unitId, startingAddress, registerBytes);
        }

        public void SeedInputRegisters(ushort startingAddress, byte[] registerBytes, int unitId = 1)
        {
            PollProxy.SetInputRegisters(unitId, startingAddress, registerBytes);
            CommandProxy.SetInputRegisters(unitId, startingAddress, registerBytes);
        }

        public void SeedCoil(ushort address, bool value, int unitId = 1)
        {
            PollProxy.SetCoil(unitId, address, value);
            CommandProxy.SetCoil(unitId, address, value);
        }

        public void SeedDiscreteInput(ushort address, bool value, int unitId = 1)
        {
            PollProxy.SetDiscreteInput(unitId, address, value);
            CommandProxy.SetDiscreteInput(unitId, address, value);
        }

        /// <summary>
        ///     Hands out the prepared clients in creation order: the block asks for the poll connection first,
        ///     then the command connection.
        /// </summary>
        private sealed class QueuedClientFactory : ILogicBlockModbusTcpClientFactory
        {
            private readonly Queue<ILogicBlockModbusTcpClient> _clients;

            public QueuedClientFactory(params ILogicBlockModbusTcpClient[] clients)
            {
                _clients = new Queue<ILogicBlockModbusTcpClient>(clients);
            }

            public ILogicBlockModbusTcpClient Create()
            {
                return _clients.Dequeue();
            }
        }
    }
}