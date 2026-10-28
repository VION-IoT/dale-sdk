using System;
using Vion.Dale.Sdk.Modbus.Core.Server;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock
{
    internal sealed class LogicBlockModbusTcpServerUnit : ILogicBlockModbusTcpServerUnit
    {
        private readonly IModbusTcpServerProxy _proxy;

        private readonly LogicBlockModbusTcpServer _server;

        public LogicBlockModbusTcpServerUnit(LogicBlockModbusTcpServer server, IModbusTcpServerProxy proxy, byte unitId)
        {
            _server = server;
            _proxy = proxy;
            UnitId = unitId;
        }

        /// <inheritdoc />
        public byte UnitId { get; }

        /// <inheritdoc />
        public ushort HoldingRegisterCount
        {
            get;

            set
            {
                _server.EnsureDisabled(nameof(HoldingRegisterCount));
                field = value;
            }
        }

        /// <inheritdoc />
        public ushort InputRegisterCount
        {
            get;

            set
            {
                _server.EnsureDisabled(nameof(InputRegisterCount));
                field = value;
            }
        }

        /// <inheritdoc />
        public ushort CoilCount
        {
            get;

            set
            {
                _server.EnsureDisabled(nameof(CoilCount));
                field = value;
            }
        }

        /// <inheritdoc />
        public ushort DiscreteInputCount
        {
            get;

            set
            {
                _server.EnsureDisabled(nameof(DiscreteInputCount));
                field = value;
            }
        }

        /// <inheritdoc />
        public DateTimeOffset? LastClientWriteAt
        {
            get => _proxy.GetLastClientWriteAt(UnitId);
        }

        internal ModbusServerAreaExtents Extents
        {
            get => new(HoldingRegisterCount, InputRegisterCount, CoilCount, DiscreteInputCount);
        }

        /// <inheritdoc />
        public void Sync(Action<IModbusServerSnapshot> access)
        {
            _server.RunSync(UnitId, Extents, access);
        }

        /// <inheritdoc />
        public T Sync<T>(Func<IModbusServerSnapshot, T> access)
        {
            return _server.RunSync(UnitId, Extents, access);
        }
    }
}
