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
    [PublicApi]
    public sealed class FakeModbusTcpServerClient
    {
        private readonly FakeModbusTcpServerProxy _proxy;

        internal FakeModbusTcpServerClient(FakeModbusTcpServerProxy proxy)
        {
            _proxy = proxy;
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
            _proxy.SimulateClientWriteHoldingRegisters(address, bytes);
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
            _proxy.SimulateClientWriteHoldingRegisters(startingAddress, registerBytes);
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
            _proxy.SimulateClientWriteSingleCoil(address, value);
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
            return _proxy.SimulateClientReadHoldingRegisters(startingAddress, quantity);
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
            return _proxy.SimulateClientReadInputRegisters(startingAddress, quantity);
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
            return _proxy.SimulateClientReadCoils(startingAddress, quantity);
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
            return _proxy.SimulateClientReadDiscreteInputs(startingAddress, quantity);
        }
    }
}
