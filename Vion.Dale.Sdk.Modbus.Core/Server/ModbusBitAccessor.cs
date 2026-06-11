using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Core.Server
{
    /// <summary>
    ///     <see cref="IModbusBitAccessor" /> over a live bit-packed server buffer
    ///     (Modbus packs bits starting at the least significant bit of each byte).
    /// </summary>
    public class ModbusBitAccessor : IModbusBitAccessor
    {
        private readonly ModbusServerArea _area;

        private readonly ushort _bitExtent;

        private readonly ModbusServerBufferAccessor _getBuffer;

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusBitAccessor" /> class.
        /// </summary>
        /// <param name="getBuffer">Provides the live bit-packed buffer of the area.</param>
        /// <param name="bitExtent">The declared extent: bit addresses 0 to <paramref name="bitExtent" /> - 1 are accessible.</param>
        /// <param name="area">The area this accessor belongs to (used in error messages).</param>
        public ModbusBitAccessor(ModbusServerBufferAccessor getBuffer, ushort bitExtent, ModbusServerArea area)
        {
            _getBuffer = getBuffer;
            _bitExtent = bitExtent;
            _area = area;
        }

        /// <inheritdoc />
        public bool Read(ushort address)
        {
            ValidateAddress(address);

            return (_getBuffer()[address / 8] & (1 << (address % 8))) != 0;
        }

        /// <inheritdoc />
        public void Write(ushort address, bool value)
        {
            ValidateAddress(address);

            var buffer = _getBuffer();
            if (value)
            {
                buffer[address / 8] |= (byte)(1 << (address % 8));
            }
            else
            {
                buffer[address / 8] &= (byte)~(1 << (address % 8));
            }
        }

        private void ValidateAddress(ushort address)
        {
            if (address >= _bitExtent)
            {
                throw new InvalidServerAddressException(_area, address, 1, _bitExtent);
            }
        }
    }
}
