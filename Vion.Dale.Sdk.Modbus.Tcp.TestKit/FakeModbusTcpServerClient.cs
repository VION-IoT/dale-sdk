using System;
using System.Buffers.Binary;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Tcp.TestKit
{
    /// <summary>
    ///     The test-side master view of a fake Modbus TCP server: drives the wire side of a server-fronted block
    ///     without sockets. Method names follow the Modbus TCP client surface so wire-contract tests read like
    ///     client code. Typed values are deliberately encoded with <see cref="BinaryPrimitives" /> (big-endian),
    ///     independent of the SDK's converter — so a conversion bug cannot cancel itself out in a test.
    /// </summary>
    /// <remarks>
    ///     The harness's view addresses no unit, which a server that declares no unit answers from its one map. A
    ///     server that declares units needs the unit a master addresses: take that view with <see cref="ForUnit" />.
    ///     Every member's exceptions then also include <see cref="ModbusExceptionCode.GatewayTargetDeviceFailedToRespond" />
    ///     for a unit the server does not declare, and the unaddressed view throws
    ///     <see cref="InvalidOperationException" /> on such a server.
    /// </remarks>
    [PublicApi]
    public sealed class FakeModbusTcpServerClient
    {
        private readonly FakeModbusTcpServerProxy _proxy;

        private readonly byte? _unitId;

        internal FakeModbusTcpServerClient(FakeModbusTcpServerProxy proxy, byte? unitId = null)
        {
            _proxy = proxy;
            _unitId = unitId;
        }

        /// <summary>
        ///     Gets a master view whose requests address <paramref name="unitId" />.
        /// </summary>
        /// <param name="unitId">The unit identifier every request of the returned view carries.</param>
        /// <returns>
        ///     A view answered from that unit's map on a server that declares units, and from the one map on a
        ///     server that declares none.
        /// </returns>
        public FakeModbusTcpServerClient ForUnit(byte unitId)
        {
            return new FakeModbusTcpServerClient(_proxy, unitId);
        }

        /// <summary>
        ///     Writes a single holding register (function code 6).
        /// </summary>
        /// <param name="address">The register address to write.</param>
        /// <param name="value">The register value.</param>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the address lies outside the
        ///     declared extents.
        /// </exception>
        public void WriteSingleHoldingRegister(ushort address, ushort value)
        {
            var bytes = new byte[2];
            BinaryPrimitives.WriteUInt16BigEndian(bytes, value);
            WriteHoldingRegisters(address, bytes);
        }

        /// <summary>
        ///     Writes multiple holding registers from raw wire bytes (function code 16).
        /// </summary>
        /// <param name="startingAddress">The register address to start writing at.</param>
        /// <param name="registerBytes">The register bytes in wire order (2 bytes per register).</param>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents.
        /// </exception>
        public void WriteMultipleHoldingRegistersRaw(ushort startingAddress, byte[] registerBytes)
        {
            WriteHoldingRegisters(startingAddress, registerBytes);
        }

        /// <summary>
        ///     Writes a single coil (function code 5).
        /// </summary>
        /// <param name="address">The coil address to write.</param>
        /// <param name="value">The coil value.</param>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the address lies outside the
        ///     declared extents.
        /// </exception>
        public void WriteSingleCoil(ushort address, bool value)
        {
            if (_unitId is { } unitId)
            {
                _proxy.SimulateClientWriteSingleCoil(unitId, address, value);
            }
            else
            {
                _proxy.SimulateClientWriteSingleCoil(address, value);
            }
        }

        /// <summary>
        ///     Reads holding registers as raw wire bytes (function code 3).
        /// </summary>
        /// <param name="startingAddress">The register address to start reading from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <returns>The register bytes in wire order (2 bytes per register).</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents.
        /// </exception>
        public byte[] ReadHoldingRegistersRaw(ushort startingAddress, ushort quantity)
        {
            return _unitId is { } unitId ? _proxy.SimulateClientReadHoldingRegisters(unitId, startingAddress, quantity) : _proxy.SimulateClientReadHoldingRegisters(startingAddress, quantity);
        }

        /// <summary>
        ///     Reads input registers as raw wire bytes (function code 4).
        /// </summary>
        /// <param name="startingAddress">The register address to start reading from.</param>
        /// <param name="quantity">The number of registers to read.</param>
        /// <returns>The register bytes in wire order (2 bytes per register).</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents.
        /// </exception>
        public byte[] ReadInputRegistersRaw(ushort startingAddress, ushort quantity)
        {
            return _unitId is { } unitId ? _proxy.SimulateClientReadInputRegisters(unitId, startingAddress, quantity) : _proxy.SimulateClientReadInputRegisters(startingAddress, quantity);
        }

        /// <summary>
        ///     Reads coils (function code 1).
        /// </summary>
        /// <param name="startingAddress">The coil address to start reading from.</param>
        /// <param name="quantity">The number of coils to read.</param>
        /// <returns>One boolean per coil.</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents.
        /// </exception>
        public bool[] ReadCoils(ushort startingAddress, ushort quantity)
        {
            return _unitId is { } unitId ? _proxy.SimulateClientReadCoils(unitId, startingAddress, quantity) : _proxy.SimulateClientReadCoils(startingAddress, quantity);
        }

        /// <summary>
        ///     Reads discrete inputs (function code 2).
        /// </summary>
        /// <param name="startingAddress">The discrete input address to start reading from.</param>
        /// <param name="quantity">The number of discrete inputs to read.</param>
        /// <returns>One boolean per discrete input.</returns>
        /// <exception cref="ModbusException">
        ///     Thrown with <see cref="ModbusExceptionCode.IllegalDataAddress" /> when the range lies outside the
        ///     declared extents.
        /// </exception>
        public bool[] ReadDiscreteInputs(ushort startingAddress, ushort quantity)
        {
            return _unitId is { } unitId ? _proxy.SimulateClientReadDiscreteInputs(unitId, startingAddress, quantity) : _proxy.SimulateClientReadDiscreteInputs(startingAddress, quantity);
        }

        private void WriteHoldingRegisters(ushort startingAddress, byte[] registerBytes)
        {
            if (_unitId is { } unitId)
            {
                _proxy.SimulateClientWriteHoldingRegisters(unitId, startingAddress, registerBytes);
            }
            else
            {
                _proxy.SimulateClientWriteHoldingRegisters(startingAddress, registerBytes);
            }
        }
    }
}