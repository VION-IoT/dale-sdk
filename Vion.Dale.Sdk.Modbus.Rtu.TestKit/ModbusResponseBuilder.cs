using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Modbus.Rtu.TestKit
{
    /// <summary>
    ///     Helpers for constructing Modbus response byte arrays in big-endian (MSB-first) order,
    ///     matching the default Modbus wire format (<c>ByteOrder.MsbToLsb</c>).
    /// </summary>
    [PublicApi]
    public static class ModbusResponseBuilder
    {
        /// <summary>
        ///     Converts float values to big-endian bytes (4 bytes each).
        /// </summary>
        public static byte[] FromFloats(params float[] values)
        {
            var result = new byte[values.Length * 4];
            for (var i = 0; i < values.Length; i++)
            {
                var bytes = BitConverter.GetBytes(values[i]);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                Buffer.BlockCopy(bytes, 0, result, i * 4, 4);
            }

            return result;
        }

        /// <summary>
        ///     Converts short values to big-endian bytes (2 bytes each).
        /// </summary>
        public static byte[] FromShorts(params short[] values)
        {
            var result = new byte[values.Length * 2];
            for (var i = 0; i < values.Length; i++)
            {
                var bytes = BitConverter.GetBytes(values[i]);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                Buffer.BlockCopy(bytes, 0, result, i * 2, 2);
            }

            return result;
        }

        /// <summary>
        ///     Converts ushort values to big-endian bytes (2 bytes each).
        /// </summary>
        public static byte[] FromUShorts(params ushort[] values)
        {
            var result = new byte[values.Length * 2];
            for (var i = 0; i < values.Length; i++)
            {
                var bytes = BitConverter.GetBytes(values[i]);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                Buffer.BlockCopy(bytes, 0, result, i * 2, 2);
            }

            return result;
        }

        /// <summary>
        ///     Converts int values to big-endian bytes (4 bytes each).
        /// </summary>
        public static byte[] FromInts(params int[] values)
        {
            var result = new byte[values.Length * 4];
            for (var i = 0; i < values.Length; i++)
            {
                var bytes = BitConverter.GetBytes(values[i]);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                Buffer.BlockCopy(bytes, 0, result, i * 4, 4);
            }

            return result;
        }

        /// <summary>
        ///     Converts double values to big-endian bytes (8 bytes each).
        /// </summary>
        public static byte[] FromDoubles(params double[] values)
        {
            var result = new byte[values.Length * 8];
            for (var i = 0; i < values.Length; i++)
            {
                var bytes = BitConverter.GetBytes(values[i]);
                if (BitConverter.IsLittleEndian)
                {
                    Array.Reverse(bytes);
                }

                Buffer.BlockCopy(bytes, 0, result, i * 8, 8);
            }

            return result;
        }

        /// <summary>
        ///     Packs boolean values into bytes using Modbus coil/discrete input bit packing.
        ///     Each byte holds up to 8 coils, LSB first within each byte.
        /// </summary>
        public static byte[] FromBools(params bool[] values)
        {
            var byteCount = (values.Length + 7) / 8;
            var result = new byte[byteCount];
            for (var i = 0; i < values.Length; i++)
            {
                if (values[i])
                {
                    result[i / 8] |= (byte)(1 << (i % 8));
                }
            }

            return result;
        }
    }
}