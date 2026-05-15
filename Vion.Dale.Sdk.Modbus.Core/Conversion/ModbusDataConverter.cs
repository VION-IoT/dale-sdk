using System;
using System.Runtime.InteropServices;
using System.Text;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Core.Conversion
{
    /// <summary>
    ///     Provides methods for converting between Modbus register byte representations and .NET types, handling byte/word ordering, text encoding, and bit-level operations.
    /// </summary>
    public class ModbusDataConverter : IModbusDataConverter
    {
        private readonly IBitConverterProxy _bitConverter;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusDataConverter" /> class.
        /// </summary>
        /// <param name="bitConverter">The bit converter proxy used to determine system endianness and perform byte conversions.</param>
        public ModbusDataConverter(IBitConverterProxy bitConverter)
        {
            _bitConverter = bitConverter;
        }

        /// <inheritdoc />
        public ushort ConvertCountToQuantity(uint count, int bytesPerCount)
        {
            if (count == 0)
            {
                throw new InvalidCountException(count, "Count must be greater than 0.");
            }

            const int bytesPerRegister = 2;
            var registersPerValue = bytesPerCount / bytesPerRegister;
            var quantity = registersPerValue * count;
            if (quantity > ushort.MaxValue)
            {
                var maxCount = ushort.MaxValue / registersPerValue;
                throw new InvalidCountException(count,
                                                $"Count of {count} exceeds maximum of {maxCount} for {bytesPerCount * 8}-bit values " +
                                                $"(would require {quantity} registers, exceeding the maximum of {ushort.MaxValue}).");
            }

            return (ushort)quantity;
        }

        /// <inheritdoc />
        public void SwapBytes(Memory<byte> bytes, ByteOrder byteOrder)
        {
            if (byteOrder is not (ByteOrder.MsbToLsb or ByteOrder.LsbToMsb))
            {
                throw new UnsupportedByteOrderException(byteOrder);
            }

            var swapBytes = (byteOrder == ByteOrder.MsbToLsb && _bitConverter.IsLittleEndian) || (byteOrder == ByteOrder.LsbToMsb && !_bitConverter.IsLittleEndian);
            if (!swapBytes)
            {
                return;
            }

            var bytesSpan = bytes.Span;
            for (var i = 0; i < bytes.Length; i += 2)
            {
                (bytesSpan[i], bytesSpan[i + 1]) = (bytesSpan[i + 1], bytesSpan[i]);
            }
        }

        /// <inheritdoc />
        public void SwapWords(Memory<byte> bytes, WordOrder32 wordOrder)
        {
            if (wordOrder is not (WordOrder32.MswToLsw or WordOrder32.LswToMsw))
            {
                throw new UnsupportedWordOrder32Exception(wordOrder);
            }

            var swapWords = (wordOrder == WordOrder32.MswToLsw && _bitConverter.IsLittleEndian) || (wordOrder == WordOrder32.LswToMsw && !_bitConverter.IsLittleEndian);
            if (!swapWords)
            {
                return;
            }

            var bytesSpan = bytes.Span;
            for (var i = 0; i < bytes.Length; i += 4)
            {
                (bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 2], bytesSpan[i + 3]) = (bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i], bytesSpan[i + 1]);
            }
        }

        /// <inheritdoc />
        public void SwapWords(Memory<byte> bytes, WordOrder64 wordOrder)
        {
            var bytesSpan = bytes.Span;
            for (var i = 0; i < bytes.Length; i += 8)
            {
                switch (wordOrder)
                {
                    case WordOrder64.ABCD:
                        if (_bitConverter.IsLittleEndian)
                        {
                            // Reading ABCD -> DCBA
                            // Writing DCBA -> ABCD
                            (bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 6], bytesSpan[i + 7]) =
                                (bytesSpan[i + 6], bytesSpan[i + 7], bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i], bytesSpan[i + 1]);
                        }

                        break;

                    case WordOrder64.DCBA:
                        if (!_bitConverter.IsLittleEndian)
                        {
                            // Reading DCBA -> ABCD
                            // Writing ABCD -> DCBA
                            (bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 6], bytesSpan[i + 7]) =
                                (bytesSpan[i + 6], bytesSpan[i + 7], bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i], bytesSpan[i + 1]);
                        }

                        break;

                    case WordOrder64.CDAB:
                        if (_bitConverter.IsLittleEndian)
                        {
                            // Reading CDAB -> DCBA
                            // Writing DCBA -> CDAB
                            (bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 6], bytesSpan[i + 7]) =
                                (bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 6], bytesSpan[i + 7], bytesSpan[i + 4], bytesSpan[i + 5]);
                        }
                        else
                        {
                            // Reading CDAB -> ABCD
                            // Writing ABCD -> CDAB
                            (bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 6], bytesSpan[i + 7]) =
                                (bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 6], bytesSpan[i + 7], bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 2], bytesSpan[i + 3]);
                        }

                        break;

                    case WordOrder64.BADC:
                        if (_bitConverter.IsLittleEndian)
                        {
                            // Reading BADC -> DCBA
                            // Writing DCBA -> BADC
                            (bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 6], bytesSpan[i + 7]) =
                                (bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 6], bytesSpan[i + 7], bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 2], bytesSpan[i + 3]);
                        }
                        else
                        {
                            // Reading BADC -> ABCD
                            // Writing ABCD -> BADC
                            (bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i + 4], bytesSpan[i + 5], bytesSpan[i + 6], bytesSpan[i + 7]) =
                                (bytesSpan[i + 2], bytesSpan[i + 3], bytesSpan[i], bytesSpan[i + 1], bytesSpan[i + 6], bytesSpan[i + 7], bytesSpan[i + 4], bytesSpan[i + 5]);
                        }

                        break;
                    default: throw new UnsupportedWordOrder64Exception(wordOrder);
                }
            }
        }

        /// <inheritdoc />
        public string ConvertBytesToString(Memory<byte> bytes, TextEncoding textEncoding)
        {
            var bytesSpan = bytes.Span;
            return textEncoding switch
            {
                TextEncoding.Utf8 => Encoding.UTF8.GetString(bytesSpan),
                TextEncoding.Utf16Le => Encoding.Unicode.GetString(bytesSpan),
                TextEncoding.Utf16Be => Encoding.BigEndianUnicode.GetString(bytesSpan),
                TextEncoding.Ascii => Encoding.ASCII.GetString(bytesSpan),
                _ => throw new UnsupportedTextEncodingException(textEncoding),
            };
        }

        /// <inheritdoc />
        public byte[] ConvertStringToBytes(string value, TextEncoding textEncoding)
        {
            var valueAsBytes = textEncoding switch
            {
                TextEncoding.Utf8 => Encoding.UTF8.GetBytes(value),
                TextEncoding.Utf16Le => Encoding.Unicode.GetBytes(value),
                TextEncoding.Utf16Be => Encoding.BigEndianUnicode.GetBytes(value),
                TextEncoding.Ascii => Encoding.ASCII.GetBytes(value),
                _ => throw new UnsupportedTextEncodingException(textEncoding),
            };

            if (valueAsBytes.Length % 2 != 0)
            {
                Array.Resize(ref valueAsBytes, valueAsBytes.Length + 1);
            }

            return valueAsBytes;
        }

        /// <inheritdoc />
        public bool[] ConvertBitsToBools(Memory<byte> bytes, ushort quantity)
        {
            var availableBits = bytes.Length * 8;
            if (quantity > availableBits)
            {
                throw new InvalidBitQuantityException(quantity, availableBits);
            }

            var bytesSpan = bytes.Span;
            var bools = new bool[quantity];
            for (var i = 0; i < quantity; i++)
            {
                var byteIndex = i / 8;

                // Bit index within the byte, is based on bit significance (2^n).
                // Modbus packs discrete inputs and coils starting at the least significant bit (2^0).
                //
                // Example: if quantity = 10
                // - byte 0 contains values 0–7
                // - byte 1 contains values 8–9 in its least significant bits (bitIndex 0–1)
                // - remaining bits in byte 1 are padding and ignored
                var bitIndex = i % 8;

                // Build a single-bit mask selecting the bit with significance 2^bitIndex.
                // Example: bitIndex = 3 -> (1 << 3) = 00001000
                var bitMask = 1 << bitIndex;

                // Mask out all other bits to check whether this specific bit is set.
                var maskedBitValue = bytesSpan[byteIndex] & bitMask;

                // Non-zero means the bit was set (true); zero means it was not set (false).
                bools[i] = maskedBitValue != 0;
            }

            return bools;
        }

        /// <inheritdoc />
        public byte[] CastToBytes<T>(T[] values)
            where T : unmanaged
        {
            return MemoryMarshal.Cast<T, byte>(values).ToArray();
        }

        /// <inheritdoc />
        public T[] CastFromBytes<T>(Memory<byte> bytes)
            where T : unmanaged
        {
            return MemoryMarshal.Cast<byte, T>(bytes.Span).ToArray();
        }

        /// <inheritdoc />
        public byte[] GetBytes(short value)
        {
            return _bitConverter.GetBytes(value);
        }

        /// <inheritdoc />
        public byte[] GetBytes(ushort value)
        {
            return _bitConverter.GetBytes(value);
        }

        /// <inheritdoc />
        public byte ToByte(bool value)
        {
            return Convert.ToByte(value);
        }
    }
}