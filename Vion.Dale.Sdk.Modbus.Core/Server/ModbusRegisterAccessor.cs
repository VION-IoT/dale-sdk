using System;
using Vion.Dale.Sdk.Modbus.Core.Conversion;
using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Core.Server
{
    /// <summary>
    ///     <see cref="IModbusRegisterAccessor" /> over a live server register buffer in wire order
    ///     (big-endian per 16-bit word), with all typed conversion delegated to <see cref="IModbusDataConverter" />.
    /// </summary>
    public class ModbusRegisterAccessor : IModbusRegisterAccessor
    {
        private const int BytesPer16BitValue = 2;

        private const int BytesPer32BitValue = 4;

        private const int BytesPer64BitValue = 8;

        private readonly ModbusServerArea _area;

        private readonly IModbusDataConverter _dataConverter;

        private readonly ModbusServerBufferAccessor _getBuffer;

        private readonly ushort _registerExtent;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusRegisterAccessor" /> class.
        /// </summary>
        /// <param name="getBuffer">Provides the live register buffer of the area, in wire order.</param>
        /// <param name="registerExtent">
        ///     The declared extent: register addresses 0 to <paramref name="registerExtent" /> - 1 are
        ///     accessible.
        /// </param>
        /// <param name="area">The area this accessor belongs to (used in error messages).</param>
        /// <param name="dataConverter">The converter performing all byte and word order conversion.</param>
        public ModbusRegisterAccessor(ModbusServerBufferAccessor getBuffer, ushort registerExtent, ModbusServerArea area, IModbusDataConverter dataConverter)
        {
            _getBuffer = getBuffer;
            _registerExtent = registerExtent;
            _area = area;
            _dataConverter = dataConverter;
        }

        /// <inheritdoc />
        public byte[] ReadRaw(ushort startingAddress, ushort quantity)
        {
            ValidateRange(startingAddress, quantity);

            return _getBuffer().Slice(startingAddress * 2, quantity * 2).ToArray();
        }

        /// <inheritdoc />
        public void WriteRaw(ushort startingAddress, byte[] registerBytes)
        {
            if (registerBytes.Length % 2 != 0)
            {
                throw new ArgumentException("Register data must be a multiple of 2 bytes (one register is 2 bytes).", nameof(registerBytes));
            }

            ValidateRange(startingAddress, (uint)(registerBytes.Length / 2));
            registerBytes.CopyTo(_getBuffer().Slice(startingAddress * 2, registerBytes.Length));
        }

        /// <inheritdoc />
        public short ReadAsShort(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb)
        {
            return ReadValue<short>(startingAddress, BytesPer16BitValue, bytes => _dataConverter.SwapBytes(bytes, byteOrder));
        }

        /// <inheritdoc />
        public void WriteAsShort(ushort startingAddress, short value, ByteOrder byteOrder = ByteOrder.MsbToLsb)
        {
            WriteValue(startingAddress, value, bytes => _dataConverter.SwapBytes(bytes, byteOrder));
        }

        /// <inheritdoc />
        public ushort ReadAsUShort(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb)
        {
            return ReadValue<ushort>(startingAddress, BytesPer16BitValue, bytes => _dataConverter.SwapBytes(bytes, byteOrder));
        }

        /// <inheritdoc />
        public void WriteAsUShort(ushort startingAddress, ushort value, ByteOrder byteOrder = ByteOrder.MsbToLsb)
        {
            WriteValue(startingAddress, value, bytes => _dataConverter.SwapBytes(bytes, byteOrder));
        }

        /// <inheritdoc />
        public int ReadAsInt(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw)
        {
            return ReadValue<int>(startingAddress, BytesPer32BitValue, Swap32(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public void WriteAsInt(ushort startingAddress, int value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw)
        {
            WriteValue(startingAddress, value, Swap32(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public uint ReadAsUInt(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw)
        {
            return ReadValue<uint>(startingAddress, BytesPer32BitValue, Swap32(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public void WriteAsUInt(ushort startingAddress, uint value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw)
        {
            WriteValue(startingAddress, value, Swap32(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public float ReadAsFloat(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw)
        {
            return ReadValue<float>(startingAddress, BytesPer32BitValue, Swap32(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public void WriteAsFloat(ushort startingAddress, float value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder32 wordOrder = WordOrder32.MswToLsw)
        {
            WriteValue(startingAddress, value, Swap32(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public long ReadAsLong(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD)
        {
            return ReadValue<long>(startingAddress, BytesPer64BitValue, Swap64(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public void WriteAsLong(ushort startingAddress, long value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD)
        {
            WriteValue(startingAddress, value, Swap64(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public ulong ReadAsULong(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD)
        {
            return ReadValue<ulong>(startingAddress, BytesPer64BitValue, Swap64(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public void WriteAsULong(ushort startingAddress, ulong value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD)
        {
            WriteValue(startingAddress, value, Swap64(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public double ReadAsDouble(ushort startingAddress, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD)
        {
            return ReadValue<double>(startingAddress, BytesPer64BitValue, Swap64(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public void WriteAsDouble(ushort startingAddress, double value, ByteOrder byteOrder = ByteOrder.MsbToLsb, WordOrder64 wordOrder = WordOrder64.ABCD)
        {
            WriteValue(startingAddress, value, Swap64(byteOrder, wordOrder));
        }

        /// <inheritdoc />
        public string ReadAsString(ushort startingAddress, ushort quantity, TextEncoding textEncoding, ByteOrder byteOrder = ByteOrder.MsbToLsb)
        {
            var bytes = ReadRaw(startingAddress, quantity);
            _dataConverter.SwapBytes(bytes, byteOrder);

            return _dataConverter.ConvertBytesToString(bytes, textEncoding);
        }

        /// <inheritdoc />
        public void WriteAsString(ushort startingAddress, string value, TextEncoding textEncoding, ByteOrder byteOrder = ByteOrder.MsbToLsb)
        {
            var bytes = _dataConverter.ConvertStringToBytes(value, textEncoding);
            _dataConverter.SwapBytes(bytes, byteOrder);
            WriteRaw(startingAddress, bytes);
        }

        private Action<Memory<byte>> Swap32(ByteOrder byteOrder, WordOrder32 wordOrder)
        {
            return bytes =>
                   {
                       _dataConverter.SwapBytes(bytes, byteOrder);
                       _dataConverter.SwapWords(bytes, wordOrder);
                   };
        }

        private Action<Memory<byte>> Swap64(ByteOrder byteOrder, WordOrder64 wordOrder)
        {
            return bytes =>
                   {
                       _dataConverter.SwapBytes(bytes, byteOrder);
                       _dataConverter.SwapWords(bytes, wordOrder);
                   };
        }

        private T ReadValue<T>(ushort startingAddress, int byteCount, Action<Memory<byte>> swap)
            where T : unmanaged
        {
            ValidateRange(startingAddress, (uint)(byteCount / 2));

            var bytes = new byte[byteCount];
            _getBuffer().Slice(startingAddress * 2, byteCount).CopyTo(bytes);
            swap(bytes);

            return _dataConverter.CastFromBytes<T>(bytes)[0];
        }

        private void WriteValue<T>(ushort startingAddress, T value, Action<Memory<byte>> swap)
            where T : unmanaged
        {
            var bytes = _dataConverter.CastToBytes(new[] { value });
            ValidateRange(startingAddress, (uint)(bytes.Length / 2));
            swap(bytes);
            bytes.CopyTo(_getBuffer().Slice(startingAddress * 2, bytes.Length));
        }

        private void ValidateRange(ushort startingAddress, uint quantity)
        {
            if (quantity == 0 || startingAddress + (long)quantity > _registerExtent)
            {
                throw new InvalidServerAddressException(_area, startingAddress, quantity, _registerExtent);
            }
        }
    }
}