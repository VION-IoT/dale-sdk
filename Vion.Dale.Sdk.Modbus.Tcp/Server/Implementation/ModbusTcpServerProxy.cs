using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading;
using FluentModbus;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Modbus.Core.Server;

namespace Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation
{
    internal partial class ModbusTcpServerProxy : IModbusTcpServerProxy
    {
        // FluentModbus keeps one buffer set per unit identifier in these private maps. To serve the same
        // register map under every unit identifier (see ctor), entries for ids 1-255 are aliased to unit 0's
        // buffers. Verified against the pinned FluentModbus version; if an upgrade changes these internals the
        // constructor fails fast (and the real-socket integration tests fail loudly on the served behavior).
        private static readonly string[] BufferMapFieldNames =
        {
            "_inputRegisterBufferMap",
            "_holdingRegisterBufferMap",
            "_coilBufferMap",
            "_discreteInputBufferMap",
        };

        private readonly ILogger<ModbusTcpServerProxy> _logger;

        private readonly ModbusTcpServer _server;

        private readonly TimeProvider _timeProvider;

        private bool _disposed;

        private ModbusServerAreaExtents _extents;

        // UTC ticks of the most recent client write; 0 = never. A long with Volatile semantics instead of a
        // DateTimeOffset? field because the writers are FluentModbus request threads while the reader is the
        // block's actor thread — multi-word struct copies can tear, an aligned long cannot.
        private long _lastClientWriteAtUtcTicks;

        public ModbusTcpServerProxy(ILogger<ModbusTcpServerProxy> logger, TimeProvider timeProvider)
        {
            _logger = logger;
            _timeProvider = timeProvider;

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
        public DateTimeOffset? LastClientWriteAt
        {
            get
            {
                var utcTicks = Volatile.Read(ref _lastClientWriteAtUtcTicks);

                return utcTicks == 0 ? null : new DateTimeOffset(utcTicks, TimeSpan.Zero);
            }
        }

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
            // AlwaysRaiseChangedEvent is required because single-value writes (FC5/FC6) that do not change the
            // stored value would otherwise raise no event — a master cyclically re-writing an unchanged setpoint
            // must still count as alive for comm surveillance.
            _server.EnableRaisingEvents = true;
            _server.AlwaysRaiseChangedEvent = true;
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
            TryStopServer();

            // FluentModbus disposes its request handlers without holding the server lock, so a handler accepted
            // (or skipped after a mid-iteration exception) in the stop window can survive the first Stop and keep
            // serving its master. A second Stop disposes the stragglers; give the accept path a brief moment.
            for (var attempt = 0; attempt < 3 && _server.ConnectionCount > 0; attempt++)
            {
                Thread.Sleep(10);
                TryStopServer();
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
                    // Fail fast instead of degrading to a unit-0-only server: a silent degradation would look
                    // green in DevHost and TestKit paths while every fielded master (which sends its own unit
                    // identifier) breaks. Refusing to construct turns an unverified FluentModbus upgrade into
                    // an immediate, visible failure.
                    throw new NotSupportedException($"FluentModbus internals changed ('{fieldName}' is not the expected per-unit buffer map) — " +
                                                    "the unit-id-agnostic endpoint cannot be set up, so the Modbus TCP server cannot start. " +
                                                    "Re-verify the buffer aliasing in ModbusTcpServerProxy against the upgraded FluentModbus version.");
                }

                for (var unitIdentifier = 1; unitIdentifier <= byte.MaxValue; unitIdentifier++)
                {
                    bufferMap[(byte)unitIdentifier] = unitZeroBuffer;
                }
            }
        }

        private void TryStopServer()
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
        }

        private void OnClientWrite(object? sender, RegistersChangedEventArgs e)
        {
            Volatile.Write(ref _lastClientWriteAtUtcTicks, _timeProvider.GetUtcNow().UtcTicks);
        }

        private void OnClientWriteCoils(object? sender, CoilsChangedEventArgs e)
        {
            Volatile.Write(ref _lastClientWriteAtUtcTicks, _timeProvider.GetUtcNow().UtcTicks);
        }

        [LoggerMessage(Level = LogLevel.Information, Message = "Modbus TCP server listening on {ListenAddress}:{Port}")]
        partial void LogStarted(IPAddress listenAddress, int port);

        [LoggerMessage(Level = LogLevel.Information, Message = "Modbus TCP server stopped")]
        partial void LogStopped();

        [LoggerMessage(Level = LogLevel.Warning, Message = "Swallowed a Modbus TCP server teardown race — verifying no request handler survived")]
        partial void LogTeardownRace(Exception exception);
    }
}