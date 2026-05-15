using System;
using System.Diagnostics.CodeAnalysis;

namespace Vion.Dale.Sdk.Modbus.Core.Conversion
{
    [ExcludeFromCodeCoverage]
    internal class BitConverterProxy : IBitConverterProxy
    {
        /// <inheritdoc />
        public bool IsLittleEndian
        {
            get => BitConverter.IsLittleEndian;
        }

        /// <inheritdoc />
        public byte[] GetBytes(ushort value)
        {
            return BitConverter.GetBytes(value);
        }

        /// <inheritdoc />
        public byte[] GetBytes(short value)
        {
            return BitConverter.GetBytes(value);
        }
    }
}