using System;
using System.Net;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Server;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.LogicBlock
{
    internal partial class LogicBlockModbusTcpServer : ILogicBlockModbusTcpServer
    {
        private const int MaxPort = 65535;

        private readonly IModbusDataConverter _dataConverter;

        private readonly ILogger<LogicBlockModbusTcpServer> _logger;

        private readonly IModbusTcpServerProxy _proxy;

        private IPAddress _parsedListenAddress = IPAddress.Any;

        public LogicBlockModbusTcpServer(IModbusTcpServerProxy proxy, IModbusDataConverter dataConverter, ILogger<LogicBlockModbusTcpServer> logger)
        {
            _proxy = proxy;
            _dataConverter = dataConverter;
            _logger = logger;
        }

        /// <inheritdoc />
        public bool IsEnabled
        {
            get;

            set
            {
                if (field == value)
                {
                    return;
                }

                if (value)
                {
                    _proxy.Start(_parsedListenAddress, Port, new ModbusServerAreaExtents(HoldingRegisterCount, InputRegisterCount, CoilCount, DiscreteInputCount));
                    LogEnabled(ListenAddress!, Port);
                }
                else
                {
                    _proxy.Stop();
                    LogDisabled();
                }

                field = value;
            }
        }

        /// <inheritdoc />
        public string? ListenAddress
        {
            get;

            set
            {
                EnsureDisabled(nameof(ListenAddress));
                if (string.IsNullOrWhiteSpace(value) || !IPAddress.TryParse(value, out var parsed))
                {
                    throw new FormatException($"'{value}' is not a valid IP address.");
                }

                _parsedListenAddress = parsed;
                field = value;
                LogListenAddressSet(value!);
            }
        } = "0.0.0.0";

        /// <inheritdoc />
        public int Port
        {
            get;

            set
            {
                EnsureDisabled(nameof(Port));
                if (value is < 0 or > MaxPort)
                {
                    throw new FormatException($"Port {value} is outside the valid range (0-{MaxPort}).");
                }

                field = value;
                LogPortSet(value);
            }
        } = 502;

        /// <inheritdoc />
        public ushort HoldingRegisterCount
        {
            get;

            set
            {
                EnsureDisabled(nameof(HoldingRegisterCount));
                field = value;
            }
        }

        /// <inheritdoc />
        public ushort InputRegisterCount
        {
            get;

            set
            {
                EnsureDisabled(nameof(InputRegisterCount));
                field = value;
            }
        }

        /// <inheritdoc />
        public ushort CoilCount
        {
            get;

            set
            {
                EnsureDisabled(nameof(CoilCount));
                field = value;
            }
        }

        /// <inheritdoc />
        public ushort DiscreteInputCount
        {
            get;

            set
            {
                EnsureDisabled(nameof(DiscreteInputCount));
                field = value;
            }
        }

        /// <inheritdoc />
        public bool IsListening
        {
            get => _proxy.IsListening;
        }

        /// <inheritdoc />
        public int ConnectionCount
        {
            get => _proxy.ConnectionCount;
        }

        /// <inheritdoc />
        public DateTimeOffset? LastClientWriteAt
        {
            get => _proxy.LastClientWriteAt;
        }

        /// <inheritdoc />
        public void Sync(Action<IModbusServerSnapshot> access)
        {
            lock (_proxy.Lock)
            {
                access(CreateSnapshot());
            }
        }

        /// <inheritdoc />
        public T Sync<T>(Func<IModbusServerSnapshot, T> access)
        {
            lock (_proxy.Lock)
            {
                return access(CreateSnapshot());
            }
        }

        public void Dispose()
        {
            _proxy.Dispose();
        }

        private IModbusServerSnapshot CreateSnapshot()
        {
            return new ModbusServerSnapshot(new ModbusRegisterAccessor(() => _proxy.GetHoldingRegisterBuffer(),
                                                                       HoldingRegisterCount,
                                                                       ModbusServerArea.HoldingRegisters,
                                                                       _dataConverter),
                                            new ModbusRegisterAccessor(() => _proxy.GetInputRegisterBuffer(), InputRegisterCount, ModbusServerArea.InputRegisters, _dataConverter),
                                            new ModbusBitAccessor(() => _proxy.GetCoilBuffer(), CoilCount, ModbusServerArea.Coils),
                                            new ModbusBitAccessor(() => _proxy.GetDiscreteInputBuffer(), DiscreteInputCount, ModbusServerArea.DiscreteInputs));
        }

        private void EnsureDisabled(string propertyName)
        {
            if (IsEnabled)
            {
                throw new
                    InvalidOperationException($"{propertyName} can only be changed while the server is disabled. Disable the server, update the configuration, then re-enable it.");
            }
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Modbus TCP server enabled on {ListenAddress}:{Port}")]
        partial void LogEnabled(string listenAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Modbus TCP server disabled")]
        partial void LogDisabled();

        [LoggerMessage(Level = LogLevel.Debug, Message = "Listen address set to {ListenAddress}")]
        partial void LogListenAddressSet(string listenAddress);

        [LoggerMessage(Level = LogLevel.Debug, Message = "Port set to {Port}")]
        partial void LogPortSet(int port);
    }
}