using System;
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
    [PublicApi]
    public sealed class FakeModbusTcpServerProxy : IModbusTcpServerProxy
    {
        private const int RegisterBufferSize = 2 * 65536;

        private const int BitBufferSize = 65536 / 8;

        private readonly byte[] _coils = new byte[BitBufferSize];

        private readonly byte[] _discreteInputs = new byte[BitBufferSize];

        private readonly byte[] _holdingRegisters = new byte[RegisterBufferSize];

        private readonly byte[] _inputRegisters = new byte[RegisterBufferSize];

        /// <summary>
        ///     The extents passed to the most recent <see cref="Start" /> call. Drives the simulate-client validation.
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
        public void Stop()
        {
            IsListening = false;
        }

        /// <inheritdoc />
        public Span<byte> GetHoldingRegisterBuffer()
        {
            return _holdingRegisters;
        }

        /// <inheritdoc />
        public Span<byte> GetInputRegisterBuffer()
        {
            return _inputRegisters;
        }

        /// <inheritdoc />
        public Span<byte> GetCoilBuffer()
        {
            return _coils;
        }

        /// <inheritdoc />
        public Span<byte> GetDiscreteInputBuffer()
        {
            return _discreteInputs;
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
        public void SimulateClientWriteHoldingRegisters(ushort startingAddress, byte[] registerBytes)
        {
            if (registerBytes.Length % 2 != 0)
            {
                // Impossible on the real wire: FC16 payloads are always 2 bytes per register.
                throw new ArgumentException("Register data must be a multiple of 2 bytes (one register is 2 bytes).", nameof(registerBytes));
            }

            EnsureListening();
            ValidateRange(ModbusServerArea.HoldingRegisters, startingAddress, (uint)(registerBytes.Length / 2));
            lock (Lock)
            {
                registerBytes.CopyTo(_holdingRegisters.AsSpan(startingAddress * 2));
            }

            LastClientWriteAt = TimeProvider.GetUtcNow();
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
        public void SimulateClientWriteSingleCoil(ushort address, bool value)
        {
            EnsureListening();
            ValidateRange(ModbusServerArea.Coils, address, 1);
            lock (Lock)
            {
                if (value)
                {
                    _coils[address / 8] |= (byte)(1 << (address % 8));
                }
                else
                {
                    _coils[address / 8] &= (byte)~(1 << (address % 8));
                }
            }

            LastClientWriteAt = TimeProvider.GetUtcNow();
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
        public byte[] SimulateClientReadHoldingRegisters(ushort startingAddress, ushort quantity)
        {
            return ReadRegisters(ModbusServerArea.HoldingRegisters, _holdingRegisters, startingAddress, quantity);
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
        public byte[] SimulateClientReadInputRegisters(ushort startingAddress, ushort quantity)
        {
            return ReadRegisters(ModbusServerArea.InputRegisters, _inputRegisters, startingAddress, quantity);
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
        public bool[] SimulateClientReadCoils(ushort startingAddress, ushort quantity)
        {
            return ReadBits(ModbusServerArea.Coils, _coils, startingAddress, quantity);
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
        public bool[] SimulateClientReadDiscreteInputs(ushort startingAddress, ushort quantity)
        {
            return ReadBits(ModbusServerArea.DiscreteInputs, _discreteInputs, startingAddress, quantity);
        }

        private byte[] ReadRegisters(ModbusServerArea area, byte[] buffer, ushort startingAddress, ushort quantity)
        {
            EnsureListening();
            ValidateRange(area, startingAddress, quantity);
            lock (Lock)
            {
                return buffer.AsSpan(startingAddress * 2, quantity * 2).ToArray();
            }
        }

        private bool[] ReadBits(ModbusServerArea area, byte[] buffer, ushort startingAddress, ushort quantity)
        {
            EnsureListening();
            ValidateRange(area, startingAddress, quantity);
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

        private void EnsureListening()
        {
            if (!IsListening)
            {
                throw new InvalidOperationException("The fake server is not listening — enable the server before simulating client requests.");
            }
        }

        private void ValidateRange(ModbusServerArea area, ushort startingAddress, uint quantity)
        {
            if (!Extents.Covers(area, startingAddress, quantity))
            {
                throw new ModbusException(ModbusExceptionCode.IllegalDataAddress,
                                          $"Client access to {area} at address {startingAddress} (quantity {quantity}) lies outside the declared extents.");
            }
        }
    }
}