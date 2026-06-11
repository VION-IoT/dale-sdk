using System;
using System.Net;
using FluentModbus;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation
{
    internal partial class ModbusTcpServerProxy : IModbusTcpServerProxy
    {
        private readonly ILogger<ModbusTcpServerProxy> _logger;

        private readonly ModbusTcpServer _server;

        private bool _disposed;

        private ModbusServerAreaExtents _extents;

        public ModbusTcpServerProxy(ILogger<ModbusTcpServerProxy> logger)
        {
            _logger = logger;

            // Registering exactly unit 0 and nothing else puts FluentModbus into its single-zero-unit mode:
            // the request handler accepts every incoming unit identifier and echoes it in the response — the
            // endpoint behavior the Modbus TCP specification intends for directly connected servers.
            _server = new ModbusTcpServer();
            _server.AddUnit(0);
        }

        /// <inheritdoc />
        public bool IsListening { get; private set; }

        /// <inheritdoc />
        public int ConnectionCount => _server.ConnectionCount;

        /// <inheritdoc />
        public DateTimeOffset? LastClientWriteAt { get; private set; }

        /// <inheritdoc />
        public object Lock => _server.Lock;

        /// <inheritdoc />
        public void Start(IPAddress listenAddress, int port, ModbusServerAreaExtents extents)
        {
            _extents = extents;
            _server.RequestValidator = (_, functionCode, startingAddress, quantity) => ValidateRequest(functionCode, startingAddress, quantity, _extents);

            // Change notifications stay inside the SDK: they only feed the LastClientWriteAt timestamp and are
            // never surfaced to consumer code (FluentModbus raises them on its background request threads).
            _server.EnableRaisingEvents = true;
            _server.RegistersChanged -= OnClientWrite;
            _server.RegistersChanged += OnClientWrite;
            _server.CoilsChanged -= OnClientWriteCoils;
            _server.CoilsChanged += OnClientWriteCoils;

            _server.Start(new IPEndPoint(listenAddress, port));
            IsListening = true;
            LogStarted(listenAddress, port);
        }

        /// <inheritdoc />
        public void Stop()
        {
            try
            {
                _server.Stop();
            }
            catch (Exception exception)
            {
                // FluentModbus can throw from a benign client-handler teardown race on Stop()/Dispose().
                LogTeardownRace(exception);
            }

            IsListening = false;
            LogStopped();
        }

        /// <inheritdoc />
        public Span<byte> GetHoldingRegisterBuffer()
        {
            return _server.GetHoldingRegisterBuffer();
        }

        /// <inheritdoc />
        public Span<byte> GetInputRegisterBuffer()
        {
            return _server.GetInputRegisterBuffer();
        }

        /// <inheritdoc />
        public Span<byte> GetCoilBuffer()
        {
            return _server.GetCoilBuffer();
        }

        /// <inheritdoc />
        public Span<byte> GetDiscreteInputBuffer()
        {
            return _server.GetDiscreteInputBuffer();
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
            Stop();
            try
            {
                _server.Dispose();
            }
            catch (Exception exception)
            {
                LogTeardownRace(exception);
            }
        }

        internal static ModbusExceptionCode ValidateRequest(ModbusFunctionCode functionCode, ushort startingAddress, ushort quantity, ModbusServerAreaExtents extents)
        {
            var area = functionCode switch
            {
                ModbusFunctionCode.ReadCoils => ModbusServerArea.Coils,
                ModbusFunctionCode.WriteSingleCoil => ModbusServerArea.Coils,
                ModbusFunctionCode.WriteMultipleCoils => ModbusServerArea.Coils,
                ModbusFunctionCode.ReadDiscreteInputs => ModbusServerArea.DiscreteInputs,
                ModbusFunctionCode.ReadInputRegisters => ModbusServerArea.InputRegisters,
                ModbusFunctionCode.ReadHoldingRegisters => ModbusServerArea.HoldingRegisters,
                ModbusFunctionCode.WriteSingleRegister => ModbusServerArea.HoldingRegisters,
                ModbusFunctionCode.WriteMultipleRegisters => ModbusServerArea.HoldingRegisters,
                ModbusFunctionCode.ReadWriteMultipleRegisters => ModbusServerArea.HoldingRegisters,
                _ => (ModbusServerArea?)null,
            };

            if (area is null)
            {
                // Unsupported function codes: let FluentModbus answer IllegalFunction itself.
                return ModbusExceptionCode.OK;
            }

            // Single-value writes touch exactly one address but may be reported with quantity 0.
            var effectiveQuantity = Math.Max(quantity, (ushort)1);

            return extents.Covers(area.Value, startingAddress, effectiveQuantity)
                ? ModbusExceptionCode.OK
                : ModbusExceptionCode.IllegalDataAddress;
        }

        private void OnClientWrite(object? sender, RegistersChangedEventArgs e)
        {
            LastClientWriteAt = DateTimeOffset.UtcNow;
        }

        private void OnClientWriteCoils(object? sender, CoilsChangedEventArgs e)
        {
            LastClientWriteAt = DateTimeOffset.UtcNow;
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Modbus TCP server listening on {ListenAddress}:{Port}")]
        partial void LogStarted(IPAddress listenAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Modbus TCP server stopped")]
        partial void LogStopped();

        [LoggerMessage(Level = LogLevel.Debug, Message = "Swallowed a benign Modbus TCP server teardown race")]
        partial void LogTeardownRace(Exception exception);
    }
}
