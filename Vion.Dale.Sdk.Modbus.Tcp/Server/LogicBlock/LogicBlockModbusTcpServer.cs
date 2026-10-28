using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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

        // Port 0 binds an ephemeral port the block is never told, so IsListening reads true on an endpoint no
        // master can be pointed at. It is also what an unset configuration field binds to.
        private const int MinPort = 1;

        private readonly IModbusDataConverter _dataConverter;

        private readonly ILogger<LogicBlockModbusTcpServer> _logger;

        private readonly IModbusTcpServerProxy _proxy;

        private readonly Dictionary<byte, LogicBlockModbusTcpServerUnit> _units = new();

        private bool _isEnabled;

        private IPAddress _parsedListenAddress = IPAddress.Any;

        // A depth, not a flag: the server lock is re-entrant, so a nested Sync returning would clear a flag
        // while the outer callback still holds the lock — and the guard exists for exactly that callback.
        private int _syncCallbackDepth;

        public LogicBlockModbusTcpServer(IModbusTcpServerProxy proxy, IModbusDataConverter dataConverter, ILogger<LogicBlockModbusTcpServer> logger)
        {
            _proxy = proxy;
            _dataConverter = dataConverter;
            _logger = logger;
        }

        /// <inheritdoc />
        public bool IsEnabled
        {
            get => _isEnabled;

            set
            {
                EnsureNotInSyncCallback(nameof(IsEnabled));
                if (_isEnabled == value)
                {
                    return;
                }

                if (value)
                {
                    var extents = new ModbusServerAreaExtents(HoldingRegisterCount, InputRegisterCount, CoilCount, DiscreteInputCount);
                    if (_units.Count == 0)
                    {
                        _proxy.Start(_parsedListenAddress, Port, extents);
                    }
                    else
                    {
                        // A server-level extent on a per-unit server bounds a map nobody serves; ignoring it would hide
                        // a block that set the server's counts where it meant a unit's.
                        if (extents != default)
                        {
                            throw new
                                InvalidOperationException("This server declares units, so it has no register map of its own: leave its four extents at 0 and set each unit's extents on the handle AddUnit returned.");
                        }

                        _proxy.Start(_parsedListenAddress, Port, _units.Values.ToDictionary(unit => unit.UnitId, unit => unit.Extents));
                    }

                    LogEnabled(ListenAddress!, Port);
                }
                else
                {
                    _proxy.Stop();
                    LogDisabled();
                }

                _isEnabled = value;
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
                if (value is < MinPort or > MaxPort)
                {
                    throw new FormatException(string.Format(CultureInfo.InvariantCulture, "Port {0} is outside the valid range ({1}-{2}).", value, MinPort, MaxPort));
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
        public ILogicBlockModbusTcpServerUnit AddUnit(byte unitId)
        {
            if (IsEnabled)
            {
                throw new InvalidOperationException("Units can only be declared while the server is disabled. Disable the server, declare the unit, then re-enable it.");
            }

            if (_units.ContainsKey(unitId))
            {
                throw new ArgumentException(string.Format(CultureInfo.InvariantCulture, "Unit {0} is already declared on this server.", unitId), nameof(unitId));
            }

            _proxy.AddUnit(unitId);
            var unit = new LogicBlockModbusTcpServerUnit(this, _proxy, unitId);
            _units.Add(unitId, unit);

            return unit;
        }

        /// <inheritdoc />
        public void Sync(Action<IModbusServerSnapshot> access)
        {
            EnsureNoUnits();
            RunSync(null, new ModbusServerAreaExtents(HoldingRegisterCount, InputRegisterCount, CoilCount, DiscreteInputCount), access);
        }

        /// <inheritdoc />
        public T Sync<T>(Func<IModbusServerSnapshot, T> access)
        {
            EnsureNoUnits();

            return RunSync(null, new ModbusServerAreaExtents(HoldingRegisterCount, InputRegisterCount, CoilCount, DiscreteInputCount), access);
        }

        public void Dispose()
        {
            EnsureNotInSyncCallback(nameof(Dispose));

            // A disposed server is not an enabled one: leaving the flag set reported a state pair the surface does
            // not define, and a later IsEnabled = false ran Stop() on a disposed proxy. Set directly rather than
            // through the setter — disposal is not a disable, and the proxy stops itself one line down.
            _isEnabled = false;
            _proxy.Dispose();
        }

        /// <summary>
        ///     Runs one <c>Sync</c> callback, the server's (<paramref name="unitId" /> null) or a unit's, under the server
        ///     lock and the guards every callback shares.
        /// </summary>
        internal void RunSync(byte? unitId, ModbusServerAreaExtents extents, Action<IModbusServerSnapshot> access)
        {
            RunSync(unitId,
                    extents,
                    snapshot =>
                    {
                        access(snapshot);

                        return 0;
                    });
        }

        /// <inheritdoc cref="RunSync(byte?, ModbusServerAreaExtents, Action{IModbusServerSnapshot})" />
        internal T RunSync<T>(byte? unitId, ModbusServerAreaExtents extents, Func<IModbusServerSnapshot, T> access)
        {
            lock (_proxy.Lock)
            {
                _syncCallbackDepth++;
                var lifetime = new SnapshotLifetime();
                try
                {
                    return access(CreateSnapshot(lifetime, unitId, extents));
                }
                finally
                {
                    lifetime.End();
                    _syncCallbackDepth--;
                }
            }
        }

        internal void EnsureDisabled(string propertyName)
        {
            if (IsEnabled)
            {
                throw new
                    InvalidOperationException($"{propertyName} can only be changed while the server is disabled. Disable the server, update the configuration, then re-enable it.");
            }
        }

        /// <summary>
        ///     Builds the snapshot one callback sees. Every accessor reaches the live buffer through
        ///     <paramref name="lifetime" />, so an accessor kept past the callback refuses instead of writing the
        ///     server's buffers without the lock the callback held.
        /// </summary>
        private IModbusServerSnapshot CreateSnapshot(SnapshotLifetime lifetime, byte? unitId, ModbusServerAreaExtents extents)
        {
            return new ModbusServerSnapshot(new ModbusRegisterAccessor(() => lifetime.Buffer(unitId is { } id ? _proxy.GetHoldingRegisterBuffer(id) : _proxy.GetHoldingRegisterBuffer()),
                                                                       extents.HoldingRegisterCount,
                                                                       ModbusServerArea.HoldingRegisters,
                                                                       _dataConverter),
                                            new ModbusRegisterAccessor(() => lifetime.Buffer(unitId is { } id ? _proxy.GetInputRegisterBuffer(id) : _proxy.GetInputRegisterBuffer()),
                                                                       extents.InputRegisterCount,
                                                                       ModbusServerArea.InputRegisters,
                                                                       _dataConverter),
                                            new ModbusBitAccessor(() => lifetime.Buffer(unitId is { } id ? _proxy.GetCoilBuffer(id) : _proxy.GetCoilBuffer()),
                                                                  extents.CoilCount,
                                                                  ModbusServerArea.Coils),
                                            new ModbusBitAccessor(() => lifetime.Buffer(unitId is { } id ? _proxy.GetDiscreteInputBuffer(id) : _proxy.GetDiscreteInputBuffer()),
                                                                  extents.DiscreteInputCount,
                                                                  ModbusServerArea.DiscreteInputs));
        }

        private void EnsureNoUnits()
        {
            if (_units.Count > 0)
            {
                throw new
                    InvalidOperationException("This server declares units, so it has no register map of its own to sync. Sync through the handle AddUnit returned for the unit you mean.");
            }
        }

        private void EnsureNotInSyncCallback(string memberName)
        {
            // Stopping the listener joins the request-handler tasks, which may themselves be waiting for the
            // server lock the Sync callback holds — calling this from inside the callback would deadlock the
            // actor thread permanently. Fail fast instead; react to commands after the callback returns.
            if (_syncCallbackDepth > 0)
            {
                throw new
                    InvalidOperationException($"{memberName} must not be called from inside a Sync callback — the server lock is held there. React to client-written commands after the callback returns.");
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

        /// <summary>
        ///     How long one <c>Sync</c> callback's snapshot stays usable. The accessors close over it and pass every
        ///     buffer they fetch through <see cref="Buffer" />, so the check sits on the one path all of them take.
        /// </summary>
        private sealed class SnapshotLifetime
        {
            private bool _ended;

            public void End()
            {
                _ended = true;
            }

            public Span<byte> Buffer(Span<byte> buffer)
            {
                if (_ended)
                {
                    throw new
                        InvalidOperationException("This server snapshot belongs to a Sync callback that has already returned. Its accessors reach the live server buffers, which are only guarded while the callback runs — take a fresh snapshot inside a new Sync call.");
                }

                return buffer;
            }
        }
    }
}