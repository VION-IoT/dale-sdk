using System;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Core.Conversion
{
    /// <summary>
    ///     Provides data conversion operations for Modbus register and coil data.
    /// </summary>
    public interface IModbusDataConverter
    {
        /// <summary>
        ///     Converts a count of values to the number of registers required.
        /// </summary>
        /// <param name="count">The number of values.</param>
        /// <param name="bytesPerCount">The number of bytes per value.</param>
        /// <returns>The number of registers required to hold the specified count of values.</returns>
        /// <exception cref="InvalidCountException">
        ///     Thrown when <paramref name="count" /> is 0 or when the calculated number of registers exceeds <see cref="ushort.MaxValue" />.
        /// </exception>
        ushort ConvertCountToQuantity(uint count, int bytesPerCount);

        /// <summary>
        ///     Swaps bytes according to the specified byte order if it differs from the system's endianness.
        /// </summary>
        /// <param name="bytes">The byte array to swap.</param>
        /// <param name="byteOrder">The byte order of the data. For reads, this is the order the data is currently in. For writes, this is the target order to convert to.</param>
        /// <exception cref="UnsupportedByteOrderException">
        ///     Thrown when an unsupported <paramref name="byteOrder" /> value is specified.
        /// </exception>
        /// <remarks>
        ///     Swapping only occurs when the system's endianness does not match the specified byte order.
        ///     For example, if <paramref name="byteOrder" /> is <see cref="ByteOrder.MsbToLsb" /> (big-endian) and the system is little-endian, bytes are swapped.
        ///     If <paramref name="byteOrder" /> is <see cref="ByteOrder.LsbToMsb" /> (little-endian) and the system is little-endian, no swap occurs.
        /// </remarks>
        void SwapBytes(Memory<byte> bytes, ByteOrder byteOrder);

        /// <summary>
        ///     Swaps 16-bit words within 32-bit values according to the specified word order if it differs from the system's endianness.
        /// </summary>
        /// <param name="bytes">The byte array containing 32-bit values.</param>
        /// <param name="wordOrder">The word order of the data. For reads, this is the order the data is currently in. For writes, this is the target order to convert to.</param>
        /// <exception cref="UnsupportedWordOrder32Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified.
        /// </exception>
        /// <remarks>
        ///     Swapping only occurs when the system's endianness does not match the specified word order.
        ///     For example, if <paramref name="wordOrder" /> is <see cref="WordOrder32.MswToLsw" /> (big-endian) and the system is little-endian, words are swapped.
        ///     If <paramref name="wordOrder" /> is <see cref="WordOrder32.LswToMsw" /> (little-endian) and the system is little-endian, no swap occurs.
        /// </remarks>
        void SwapWords(Memory<byte> bytes, WordOrder32 wordOrder);

        /// <summary>
        ///     Swaps 16-bit words within 64-bit values according to the specified word order.
        /// </summary>
        /// <param name="bytes">The byte array containing 64-bit values.</param>
        /// <param name="wordOrder">The word order of the data. For reads, this is the order the data is currently in. For writes, this is the target order to convert to.</param>
        /// <exception cref="UnsupportedWordOrder64Exception">
        ///     Thrown when an unsupported <paramref name="wordOrder" /> value is specified.
        /// </exception>
        /// <remarks>
        ///     For <see cref="WordOrder64.ABCD" /> and <see cref="WordOrder64.DCBA" />, swapping only occurs when the system's endianness
        ///     does not match the specified word order (same behavior as 32-bit word swapping).
        ///     For mid-endian orders (<see cref="WordOrder64.CDAB" /> and <see cref="WordOrder64.BADC" />), swapping always occurs,
        ///     with the swap operation depending on the system's endianness to produce the correct result.
        /// </remarks>
        void SwapWords(Memory<byte> bytes, WordOrder64 wordOrder);

        /// <summary>
        ///     Converts a byte array to a string using the specified text encoding.
        /// </summary>
        /// <param name="bytes">The byte array to convert.</param>
        /// <param name="textEncoding">The text encoding to use.</param>
        /// <returns>The decoded string.</returns>
        /// <exception cref="UnsupportedTextEncodingException">
        ///     Thrown when an unsupported <paramref name="textEncoding" /> value is specified.
        /// </exception>
        string ConvertBytesToString(Memory<byte> bytes, TextEncoding textEncoding);

        /// <summary>
        ///     Converts a string to a byte array using the specified text encoding.
        /// </summary>
        /// <param name="value">The string to convert.</param>
        /// <param name="textEncoding">The text encoding to use.</param>
        /// <returns>The encoded byte array.</returns>
        /// <exception cref="UnsupportedTextEncodingException">
        ///     Thrown when an unsupported <paramref name="textEncoding" /> value is specified.
        /// </exception>
        /// <remarks>
        ///     If the encoded byte array has an odd length, a null byte (0x00) is appended at the end
        ///     to ensure the result aligns to Modbus register boundaries (2 bytes per register).
        /// </remarks>
        byte[] ConvertStringToBytes(string value, TextEncoding textEncoding);

        /// <summary>
        ///     Converts packed bits into a boolean array, unpacking values starting from the least significant bit.
        /// </summary>
        /// <param name="bytes">The byte array containing packed boolean values.</param>
        /// <param name="quantity">The number of boolean values to extract.</param>
        /// <returns>An array of boolean values extracted from the packed bytes.</returns>
        /// <exception cref="InvalidBitQuantityException">
        ///     Thrown when the requested quantity exceeds the available bits in the byte array.
        /// </exception>
        /// <remarks>
        ///     Modbus packs values starting at the least significant bit (bit 0 / 2^0) of each byte.
        ///     If <paramref name="quantity" /> does not fill the last byte (e.g. quantity = 10 results in 2 bytes,
        ///     where only 2 bits of the last byte are used), the remaining higher-significance bits are padding and are ignored.
        /// </remarks>
        bool[] ConvertBitsToBools(Memory<byte> bytes, ushort quantity);

        /// <summary>
        ///     Casts an array of unmanaged values to a byte array.
        /// </summary>
        /// <typeparam name="T">The unmanaged type.</typeparam>
        /// <param name="values">The values to cast.</param>
        /// <returns>A byte array representation of the values.</returns>
        byte[] CastToBytes<T>(T[] values)
            where T : unmanaged;

        /// <summary>
        ///     Casts a byte array to an array of unmanaged values.
        /// </summary>
        /// <typeparam name="T">The unmanaged type.</typeparam>
        /// <param name="bytes">The byte array to cast.</param>
        /// <returns>An array of the specified unmanaged type.</returns>
        T[] CastFromBytes<T>(Memory<byte> bytes)
            where T : unmanaged;

        /// <summary>
        ///     Converts a signed 16-bit integer to a byte array.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>A byte array containing the converted value.</returns>
        byte[] GetBytes(short value);

        /// <summary>
        ///     Converts an unsigned 16-bit integer to a byte array.
        /// </summary>
        /// <param name="value">The value to convert.</param>
        /// <returns>A byte array containing the converted value.</returns>
        byte[] GetBytes(ushort value);

        /// <summary>
        ///     Converts a boolean value to a byte representation.
        /// </summary>
        /// <param name="value">The boolean value to convert.</param>
        /// <returns>A byte value: 1 for <c>true</c>, 0 for <c>false</c>.</returns>
        byte ToByte(bool value);
    }
}