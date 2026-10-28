using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Server;
using Vion.Dale.Sdk.Modbus.Tcp.Server.Implementation;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit
{
    /// <summary>
    ///     In-memory <see cref="IModbusTcpServerProxy" />: full-range register buffers without any sockets.
    ///     The simulate-client methods replay what a connected Modbus master would do, including the extent
    ///     validation the real server performs (out-of-map access throws a <see cref="ModbusException" /> with
    ///     <see cref="ModbusExceptionCode.IllegalDataAddress" />, mirroring the wire behavior) and the
    ///     <see cref="LastClientWriteAt" /> bookkeeping.
    /// </summary>
    /// <remarks>
    ///     A server that declares units gets a register map per unit here, as on the wire. The unit-addressed
    ///     simulate-client overloads then answer from the addressed unit's map, and a unit the server did not declare
    ///     with the <see cref="ModbusExceptionCode.GatewayTargetDeviceFailedToRespond" /> exception a master receives.
    ///     A master always sends some unit identifier, so the unaddressed overloads refuse on such a server. On a server
    ///     that declares no unit, both kinds answer from its one map.
    /// </remarks>
    [PublicApi]
    public sealed class FakeModbusTcpServerProxy : IModbusTcpServerProxy
    {
        private readonly Dictionary<byte, DateTimeOffset> _lastClientWriteAtByUnit = new();

        private readonly RegisterMap _map = new();

        private readonly Dictionary<byte, RegisterMap> _units = new();

        private Dictionary<byte, ModbusServerAreaExtents> _unitExtents = new();

        /// <summary>
        ///     The extents passed to the most recent <see cref="Start(IPAddress, int, ModbusServerAreaExtents)" /> call.
        ///     Drives the simulate-client validation of a server that declares no unit.
        /// </summary>
        public ModbusServerAreaExtents Extents { get; private set; }

        /// <summary>
        ///     The clock stamping <see cref="LastClientWriteAt" /> on simulated client writes.
        ///     Default is <see cref="TimeProvider.System" />; assign a <c>FakeTimeProvider</c> for deterministic tests.
        /// </summary>
        public TimeProvider TimeProvider { get; set; } = TimeProvider.System;

        /// <inheritdoc />
        public bool IsListening { get; private set; }

        /// <summary>
        ///     The number of connected clients reported to the server. Settable so tests can shape diagnostics.
        /// </summary>
        public int ConnectionCount { get; set; }

        /// <summary>
        ///     The last-client-write timestamp reported to the server. Set automatically by the simulate-client
        ///     write methods (via <see cref="TimeProvider" />); settable so tests can shape diagnostics directly.
        /// </summary>
        public DateTimeOffset? LastClientWriteAt { get; set; }

        /// <inheritdoc />
        public object Lock { get; } = new();

        /// <inheritdoc />
        public void Start(IPAddress listenAddress, int port, ModbusServerAreaExtents extents)
        {
            Extents = extents;
            IsListening = true;
        }

        /// <inheritdoc />
        public void Start(IPAddress listenAddress, int port, IReadOnlyDictionary<byte, ModbusServerAreaExtents> unitExtents)
        {
            _unitExtents = unitExtents.ToDictionary(pair => pair.Key, pair => pair.Value);
            IsListening = true;
        }

        /// <inheritdoc />
        public void Stop()
        {
            IsListening = false;
        }

        /// <inheritdoc />
        public void AddUnit(byte unitId)
        {
            if (!_units.ContainsKey(unitId))
            {
                _units.Add(unitId, new RegisterMap());
            }
        }

        /// <inheritdoc />
        public DateTimeOffset? GetLastClientWriteAt(byte unitId)
        {
            return _lastClientWriteAtByUnit.TryGetValue(unitId, out var lastClientWriteAt) ? lastClientWriteAt : null;
        }

        /// <inheritdoc />
        public Span<byte> GetHoldingRegisterBuffer()
        {
            return _map.HoldingRegisters;
        }

        /// <inheritdoc />
        public Span<byte> GetInputRegisterBuffer()
        {
            return _map.InputRegisters;
        }

        /// <inheritdoc />
        public Span<byte> GetCoilBuffer()
        {
            return _map.Coils;
        }

        /// <inheritdoc />
        public Span<byte> GetDiscreteInputBuffer()
        {
            return _map.DiscreteInputs;
        }

        /// <inheritdoc />
        public Span<byte> GetHoldingRegisterBuffer(byte unitId)
        {
            return MapOf(unitId).HoldingRegisters;
        }

        /// <inheritdoc />
        public Span<byte> GetInputRegisterBuffer(byte unitId)
        {
            return MapOf(unitId).InputRegisters;
        }

        /// <inheritdoc />
        public Span<byte> GetCoilBuffer(byte unitId)
        {
            return MapOf(unitId).Coils;
        }

        /// <inheritdoc />
        public Span<byte> GetDiscreteInputBuffer(byte unitId)
        {
            return MapOf(unitId).DiscreteInputs;
        }

        /// <inheritdoc />
        public void Dispose()
        {
            Stop();
        }

        /// <summary>
        ///     Simulates a connected master writing holding registers (function codes 6/16).
        /// </summary>
        /// <param name="startingAddress">The register address to start writing at.</param>
        /// <param name="registerBytes">The register bytes in wire order (2 bytes per register).</param>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents — the exception a real master receives on the wire.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown when the server declares units.</exception>
        public void SimulateClientWriteHoldingRegisters(ushort startingAddress, byte[] registerBytes)
        {
            WriteHoldingRegisters(null, startingAddress, registerBytes);
        }

        /// <summary>
        ///     Simulates a connected master writing holding registers (function codes 6/16) at one unit identifier.
        /// </summary>
        /// <param name="unitId">The unit identifier the request addresses.</param>
        /// <param name="startingAddress">The register address to start writing at.</param>
        /// <param name="registerBytes">The register bytes in wire order (2 bytes per register).</param>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     addressed map's extents, or with <see cref="ModbusExceptionCode.GatewayTargetDeviceFailedToRespond" />
        ///     when the server declares units and not this one — the exceptions a real master receives on the wire.
        /// </exception>
        public void SimulateClientWriteHoldingRegisters(byte unitId, ushort startingAddress, byte[] registerBytes)
        {
            WriteHoldingRegisters(unitId, startingAddress, registerBytes);
        }

        /// <summary>
        ///     Simulates a connected master writing a single coil (function code 5).
        /// </summary>
        /// <param name="address">The coil address to write.</param>
        /// <param name="value">The coil value to write.</param>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the address lies outside the
        ///     declared extents — the exception a real master receives on the wire.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown when the server declares units.</exception>
        public void SimulateClientWriteSingleCoil(ushort address, bool value)
        {
            WriteSingleCoil(null, address, value);
        }

        /// <summary>
        ///     Simulates a connected master writing a single coil (function code 5) at one unit identifier.
        /// </summary>
        /// <param name="unitId">The unit identifier the request addresses.</param>
        /// <param name="address">The coil address to write.</param>
        /// <param name="value">The coil value to write.</param>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the address lies outside the
        ///     addressed map's extents, or with <see cref="ModbusExceptionCode.GatewayTargetDeviceFailedToRespond" />
        ///     when the server declares units and not this one — the exceptions a real master receives on the wire.
        /// </exception>
        public void SimulateClientWriteSingleCoil(byte unitId, ushort address, bool value)
        {
            WriteSingleCoil(unitId, address, value);
        }

        /// <summary>
        ///     Simulates a connected master reading holding registers (function code 3).
        /// </summary>
        /// <param name="startingAddress">The register address to start reading from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <returns>The register bytes in wire order (2 bytes per register).</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents — the exception a real master receives on the wire.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown when the server declares units.</exception>
        public byte[] SimulateClientReadHoldingRegisters(ushort startingAddress, ushort quantity)
        {
            return ReadRegisters(null, ModbusServerArea.HoldingRegisters, startingAddress, quantity);
        }

        /// <summary>
        ///     Simulates a connected master reading holding registers (function code 3) at one unit identifier.
        /// </summary>
        /// <param name="unitId">The unit identifier the request addresses.</param>
        /// <param name="startingAddress">The register address to start reading from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <returns>The register bytes in wire order (2 bytes per register).</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     addressed map's extents, or with <see cref="ModbusExceptionCode.GatewayTargetDeviceFailedToRespond" />
        ///     when the server declares units and not this one — the exceptions a real master receives on the wire.
        /// </exception>
        public byte[] SimulateClientReadHoldingRegisters(byte unitId, ushort startingAddress, ushort quantity)
        {
            return ReadRegisters(unitId, ModbusServerArea.HoldingRegisters, startingAddress, quantity);
        }

        /// <summary>
        ///     Simulates a connected master reading input registers (function code 4).
        /// </summary>
        /// <param name="startingAddress">The register address to start reading from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <returns>The register bytes in wire order (2 bytes per register).</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents — the exception a real master receives on the wire.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown when the server declares units.</exception>
        public byte[] SimulateClientReadInputRegisters(ushort startingAddress, ushort quantity)
        {
            return ReadRegisters(null, ModbusServerArea.InputRegisters, startingAddress, quantity);
        }

        /// <summary>
        ///     Simulates a connected master reading input registers (function code 4) at one unit identifier.
        /// </summary>
        /// <param name="unitId">The unit identifier the request addresses.</param>
        /// <param name="startingAddress">The register address to start reading from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <returns>The register bytes in wire order (2 bytes per register).</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     addressed map's extents, or with <see cref="ModbusExceptionCode.GatewayTargetDeviceFailedToRespond" />
        ///     when the server declares units and not this one — the exceptions a real master receives on the wire.
        /// </exception>
        public byte[] SimulateClientReadInputRegisters(byte unitId, ushort startingAddress, ushort quantity)
        {
            return ReadRegisters(unitId, ModbusServerArea.InputRegisters, startingAddress, quantity);
        }

        /// <summary>
        ///     Simulates a connected master reading coils (function code 1).
        /// </summary>
        /// <param name="startingAddress">The coil address to start reading from.</param>
        /// <param name="quantity">The number of coils to read.</param>
        /// <returns>One boolean per coil.</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents — the exception a real master receives on the wire.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown when the server declares units.</exception>
        public bool[] SimulateClientReadCoils(ushort startingAddress, ushort quantity)
        {
            return ReadBits(null, ModbusServerArea.Coils, startingAddress, quantity);
        }

        /// <summary>
        ///     Simulates a connected master reading coils (function code 1) at one unit identifier.
        /// </summary>
        /// <param name="unitId">The unit identifier the request addresses.</param>
        /// <param name="startingAddress">The coil address to start reading from.</param>
        /// <param name="quantity">The number of coils to read.</param>
        /// <returns>One boolean per coil.</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     addressed map's extents, or with <see cref="ModbusExceptionCode.GatewayTargetDeviceFailedToRespond" />
        ///     when the server declares units and not this one — the exceptions a real master receives on the wire.
        /// </exception>
        public bool[] SimulateClientReadCoils(byte unitId, ushort startingAddress, ushort quantity)
        {
            return ReadBits(unitId, ModbusServerArea.Coils, startingAddress, quantity);
        }

        /// <summary>
        ///     Simulates a connected master reading discrete inputs (function code 2).
        /// </summary>
        /// <param name="startingAddress">The discrete input address to start reading from.</param>
        /// <param name="quantity">The number of discrete inputs to read.</param>
        /// <returns>One boolean per discrete input.</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents — the exception a real master receives on the wire.
        /// </exception>
        /// <exception cref="InvalidOperationException">Thrown when the server declares units.</exception>
        public bool[] SimulateClientReadDiscreteInputs(ushort startingAddress, ushort quantity)
        {
            return ReadBits(null, ModbusServerArea.DiscreteInputs, startingAddress, quantity);
        }

        /// <summary>
        ///     Simulates a connected master reading discrete inputs (function code 2) at one unit identifier.
        /// </summary>
        /// <param name="unitId">The unit identifier the request addresses.</param>
        /// <param name="startingAddress">The discrete input address to start reading from.</param>
        /// <param name="quantity">The number of discrete inputs to read.</param>
        /// <returns>One boolean per discrete input.</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     addressed map's extents, or with <see cref="ModbusExceptionCode.GatewayTargetDeviceFailedToRespond" />
        ///     when the server declares units and not this one — the exceptions a real master receives on the wire.
        /// </exception>
        public bool[] SimulateClientReadDiscreteInputs(byte unitId, ushort startingAddress, ushort quantity)
        {
            return ReadBits(unitId, ModbusServerArea.DiscreteInputs, startingAddress, quantity);
        }

        private void WriteHoldingRegisters(byte? unitId, ushort startingAddress, byte[] registerBytes)
        {
            if (registerBytes.Length % 2 != 0)
            {
                // Impossible on the real wire: FC16 payloads are always 2 bytes per register.
                throw new ArgumentException("Register data must be a multiple of 2 bytes (one register is 2 bytes).", nameof(registerBytes));
            }

            var map = ResolveMap(unitId, ModbusServerArea.HoldingRegisters, startingAddress, (uint)(registerBytes.Length / 2));
            lock (Lock)
            {
                registerBytes.CopyTo(map.HoldingRegisters.AsSpan(startingAddress * 2));
            }

            StampClientWrite(unitId);
        }

        private void WriteSingleCoil(byte? unitId, ushort address, bool value)
        {
            var map = ResolveMap(unitId, ModbusServerArea.Coils, address, 1);
            lock (Lock)
            {
                if (value)
                {
                    map.Coils[address / 8] |= (byte)(1 << (address % 8));
                }
                else
                {
                    map.Coils[address / 8] &= (byte)~(1 << (address % 8));
                }
            }

            StampClientWrite(unitId);
        }

        private byte[] ReadRegisters(byte? unitId, ModbusServerArea area, ushort startingAddress, ushort quantity)
        {
            var map = ResolveMap(unitId, area, startingAddress, quantity);
            var buffer = area == ModbusServerArea.HoldingRegisters ? map.HoldingRegisters : map.InputRegisters;
            lock (Lock)
            {
                return buffer.AsSpan(startingAddress * 2, quantity * 2).ToArray();
            }
        }

        private bool[] ReadBits(byte? unitId, ModbusServerArea area, ushort startingAddress, ushort quantity)
        {
            var map = ResolveMap(unitId, area, startingAddress, quantity);
            var buffer = area == ModbusServerArea.Coils ? map.Coils : map.DiscreteInputs;
            lock (Lock)
            {
                var values = new bool[quantity];
                for (var i = 0; i < quantity; i++)
                {
                    var address = startingAddress + i;
                    values[i] = (buffer[address / 8] & (1 << (address % 8))) != 0;
                }

                return values;
            }
        }

        /// <summary>
        ///     The map a simulated request is answered from, after the checks the real server makes before it reads
        ///     one: listening, the unit addressed, then the range against that map's extents.
        /// </summary>
        private RegisterMap ResolveMap(byte? unitId, ModbusServerArea area, ushort startingAddress, uint quantity)
        {
            if (!IsListening)
            {
                throw new InvalidOperationException("The fake server is not listening — enable the server before simulating client requests.");
            }

            if (_units.Count == 0)
            {
                // A server that declares no unit answers every unit identifier from its one map.
                ValidateRange(Extents, area, startingAddress, quantity);

                return _map;
            }

            if (unitId is not { } id)
            {
                throw new
                    InvalidOperationException("This fake server declares units, and a master always addresses one — simulate the request at a unit identifier (FakeModbusTcpServerClient.ForUnit).");
            }

            if (!_unitExtents.TryGetValue(id, out var extents))
            {
                throw new ModbusException(ModbusExceptionCode.GatewayTargetDeviceFailedToRespond, $"Client access to unit {id}, which the server does not declare.");
            }

            ValidateRange(extents, area, startingAddress, quantity);

            return _units[id];
        }

        private static void ValidateRange(ModbusServerAreaExtents extents, ModbusServerArea area, ushort startingAddress, uint quantity)
        {
            if (!extents.Covers(area, startingAddress, quantity))
            {
                throw new ModbusException(ModbusExceptionCode.IllegalDataAddress,
                                          $"Client access to {area} at address {startingAddress} (quantity {quantity}) lies outside the declared extents.");
            }
        }

        private void StampClientWrite(byte? unitId)
        {
            var now = TimeProvider.GetUtcNow();
            LastClientWriteAt = now;
            if (unitId is { } id)
            {
                _lastClientWriteAtByUnit[id] = now;
            }
        }

        private RegisterMap MapOf(byte unitId)
        {
            // As on the wire, a unit identifier with no map of its own reaches the one map.
            return _units.TryGetValue(unitId, out var map) ? map : _map;
        }

        /// <summary>Full-range buffers for the four areas: what one unit, or a server's one map, holds.</summary>
        private sealed class RegisterMap
        {
            private const int RegisterBufferSize = 2 * 65536;

            private const int BitBufferSize = 65536 / 8;

            public byte[] Coils { get; } = new byte[BitBufferSize];

            public byte[] DiscreteInputs { get; } = new byte[BitBufferSize];

            public byte[] HoldingRegisters { get; } = new byte[RegisterBufferSize];

            public byte[] InputRegisters { get; } = new byte[RegisterBufferSize];
        }
    }
}
