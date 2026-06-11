using System;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Core.Server
{
    /// <summary>
    ///     Typed access to one register-addressed server area (holding or input registers) inside a server snapshot.
    /// </summary>
    /// <remarks>
    ///     <para>
    ///         Only valid inside the snapshot callback that provided it — the server lock is held for the duration
    ///         of the callback and released afterwards. Do not capture accessors outside the callback.
    ///     </para>
    ///     <para>
    ///         Method names, parameters, and defaults mirror the typed method family of the Modbus TCP client
    ///         (<c>Raw</c>/<c>As&lt;Type&gt;</c>); all conversions are converter-backed, so byte and word order
    ///         are explicit parameters and the underlying buffer is only ever touched in wire order.
    ///     </para>
    /// </remarks>
    [PublicApi]
    public interface IModbusRegisterAccessor
    {
        /// <summary>
        ///     Reads registers as raw bytes in wire order (big-endian per 16-bit word).
        /// </summary>
        /// <param name="startingAddress">The register address to start reading from.</param>
        /// <param name="quantity">The number of registers (16 bit per register) to read.</param>
        /// <returns>A copy of the register bytes (2 bytes per register).</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        byte[] ReadRaw(ushort startingAddress, ushort quantity);

        /// <summary>
        ///     Writes raw register bytes in wire order (big-endian per 16-bit word).
        /// </summary>
        /// <param name="startingAddress">The register address to start writing at.</param>
        /// <param name="registerBytes">The register bytes to write (2 bytes per register).</param>
        /// <exception cref="ArgumentException">
        ///     Thrown when <paramref name="registerBytes" /> is not a multiple of 2 bytes.
        /// </exception>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        void WriteRaw(ushort startingAddress, byte[] registerBytes);

        /// <summary>
        ///     Reads one register as a signed 16-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address to read.</param>
        /// <param name="byteOrder">The byte order the data is stored in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <returns>The value.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the address lies outside the declared extent of the area.
        /// </exception>
        short ReadAsShort(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb);

        /// <summary>
        ///     Writes one register as a signed 16-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to store the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the address lies outside the declared extent of the area.
        /// </exception>
        void WriteAsShort(ushort startingAddress, short value, ByteOrder byteOrder = ByteOrder.MsbToLsb);

        /// <summary>
        ///     Reads one register as an unsigned 16-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address to read.</param>
        /// <param name="byteOrder">The byte order the data is stored in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <returns>The value.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the address lies outside the declared extent of the area.
        /// </exception>
        ushort ReadAsUShort(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb);

        /// <summary>
        ///     Writes one register as an unsigned 16-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address to write.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to store the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the address lies outside the declared extent of the area.
        /// </exception>
        void WriteAsUShort(ushort startingAddress, ushort value, ByteOrder byteOrder = ByteOrder.MsbToLsb);

        /// <summary>
        ///     Reads two consecutive registers as a signed 32-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="byteOrder">The byte order the data is stored in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is stored in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <returns>The value.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        int ReadAsInt(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw);

        /// <summary>
        ///     Writes two consecutive registers as a signed 32-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to store the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to store the data in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        void WriteAsInt(ushort startingAddress, int value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw);

        /// <summary>
        ///     Reads two consecutive registers as an unsigned 32-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="byteOrder">The byte order the data is stored in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is stored in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <returns>The value.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        uint ReadAsUInt(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw);

        /// <summary>
        ///     Writes two consecutive registers as an unsigned 32-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to store the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to store the data in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        void WriteAsUInt(ushort startingAddress, uint value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw);

        /// <summary>
        ///     Reads two consecutive registers as a 32-bit floating-point number.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="byteOrder">The byte order the data is stored in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is stored in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <returns>The value.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        float ReadAsFloat(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw);

        /// <summary>
        ///     Writes two consecutive registers as a 32-bit floating-point number.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to store the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to store the data in. Default is <see cref="WordOrder32.MswToLsw" />.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        void WriteAsFloat(ushort startingAddress, float value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw);

        /// <summary>
        ///     Reads four consecutive registers as a signed 64-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="byteOrder">The byte order the data is stored in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is stored in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <returns>The value.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        long ReadAsLong(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD);

        /// <summary>
        ///     Writes four consecutive registers as a signed 64-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to store the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to store the data in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        void WriteAsLong(ushort startingAddress, long value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD);

        /// <summary>
        ///     Reads four consecutive registers as an unsigned 64-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="byteOrder">The byte order the data is stored in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is stored in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <returns>The value.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        ulong ReadAsULong(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD);

        /// <summary>
        ///     Writes four consecutive registers as an unsigned 64-bit integer.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to store the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to store the data in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        void WriteAsULong(ushort startingAddress, ulong value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD);

        /// <summary>
        ///     Reads four consecutive registers as a 64-bit floating-point number.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="byteOrder">The byte order the data is stored in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order the data is stored in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <returns>The value.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        double ReadAsDouble(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD);

        /// <summary>
        ///     Writes four consecutive registers as a 64-bit floating-point number.
        /// </summary>
        /// <param name="startingAddress">The register address of the first register.</param>
        /// <param name="value">The value to write.</param>
        /// <param name="byteOrder">The byte order to store the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <param name="wordOrder">The word order to store the data in. Default is <see cref="WordOrder64.ABCD" />.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        void WriteAsDouble(ushort startingAddress, double value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD);

        /// <summary>
        ///     Reads consecutive registers as a string.
        /// </summary>
        /// <param name="startingAddress">The register address to start reading from.</param>
        /// <param name="quantity">The number of registers (16 bit per register) to read.</param>
        /// <param name="textEncoding">The text encoding of the stored data.</param>
        /// <param name="byteOrder">The byte order the data is stored in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <returns>The decoded string, including any padding characters stored on the wire.</returns>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        /// <exception cref="UnsupportedTextEncodingException">
        ///     Thrown when an unsupported <paramref name="textEncoding" /> value is specified.
        /// </exception>
        string ReadAsString(ushort startingAddress, ushort quantity, TextEncoding textEncoding, ByteOrder byteOrder = ByteOrder.MsbToLsb);

        /// <summary>
        ///     Writes a string to consecutive registers.
        /// </summary>
        /// <param name="startingAddress">The register address to start writing at.</param>
        /// <param name="value">The string to write.</param>
        /// <param name="textEncoding">The text encoding to store the data in.</param>
        /// <param name="byteOrder">The byte order to store the data in. Default is <see cref="ByteOrder.MsbToLsb" />.</param>
        /// <exception cref="InvalidServerAddressException">
        ///     Thrown when the range lies outside the declared extent of the area.
        /// </exception>
        /// <exception cref="UnsupportedTextEncodingException">
        ///     Thrown when an unsupported <paramref name="textEncoding" /> value is specified.
        /// </exception>
        /// <remarks>
        ///     If the encoded byte sequence has an odd length, a null byte (0x00) is appended to align it to
        ///     Modbus register boundaries (2 bytes per register).
        /// </remarks>
        void WriteAsString(ushort startingAddress, string value, TextEncoding textEncoding, ByteOrder byteOrder = ByteOrder.MsbToLsb);
    }
}