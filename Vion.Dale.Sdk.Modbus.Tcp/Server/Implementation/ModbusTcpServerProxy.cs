using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using FluentModbus;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation
{
    internal partial class ModbusTcpServerProxy : IModbusTcpServerProxy
    {
        // FluentModbus keeps one buffer set per unit identifier in these private maps. To serve the same
        // register map under every unit identifier (see ctor), entries for ids 1-255 are aliased to unit 0's
        // buffers. Verified against the pinned FluentModbus version; the real-socket integration tests fail
        // loudly if an upgrade changes these internals.
        private static readonly string[] BufferMapFieldNames =
        {
            "_inputRegisterBufferMap",
            "_holdingRegisterBufferMap",
            "_coilBufferMap",
            "_discreteInputBufferMap",
        };

        private readonly ILogger<ModbusTcpServerProxy> _logger;

        private readonly ModbusTcpServer _server;

        private bool _disposed;

        private ModbusServerAreaExtents _extents;

        public ModbusTcpServerProxy(ILogger<ModbusTcpServerProxy> logger)
        {
            _logger = logger;

            // Registering exactly unit 0 and nothing else puts FluentModbus into its single-zero-unit mode,
            // whose request filter accepts every incoming unit identifier and echoes it in the response — the
            // endpoint behavior the Modbus TCP specification intends for directly connected servers. Request
            // *processing* however still resolves buffers by the raw incoming identifier, so all 256 ids are
            // aliased to unit 0's buffers below (shared arrays — one register map, no extra memory).
            _server = new ModbusTcpServer();
            if (!_server.UnitIdentifiers.Contains((byte)0))
            {
                _server.AddUnit(0);
            }

            AliasAllUnitIdentifiersToUnitZero();
        }

        /// <inheritdoc />
        public bool IsListening { get; private set; }

        /// <inheritdoc />
        public int ConnectionCount
        {
            get => _server.ConnectionCount;
        }

        /// <inheritdoc />
        public DateTimeOffset? LastClientWriteAt { get; private set; }

        /// <inheritdoc />
        public object Lock
        {
            get => _server.Lock;
        }

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

            return extents.Covers(area.Value, startingAddress, effectiveQuantity) ? ModbusExceptionCode.OK : ModbusExceptionCode.IllegalDataAddress;
        }

        private void AliasAllUnitIdentifiersToUnitZero()
        {
            foreach (var fieldName in BufferMapFieldNames)
            {
                var field = typeof(ModbusServer).GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                if (field?.GetValue(_server) is not Dictionary<byte, byte[]> bufferMap || !bufferMap.TryGetValue(0, out var unitZeroBuffer))
                {
                    LogUnitAliasingUnavailable(fieldName);

                    return;
                }

                for (var unitIdentifier = 1; unitIdentifier <= byte.MaxValue; unitIdentifier++)
                {
                    bufferMap[(byte)unitIdentifier] = unitZeroBuffer;
                }
            }
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

        [LoggerMessage(Level = LogLevel.Warning,
                       Message = "FluentModbus internals changed ({FieldName} not found) — the server only answers requests with unit identifier 0 instead of any unit identifier")]
        partial void LogUnitAliasingUnavailable(string fieldName);
    }
}