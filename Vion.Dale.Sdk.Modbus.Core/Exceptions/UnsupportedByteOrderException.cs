using System;
using Vion.Dale.Sdk.Modbus.Core.Conversion;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Exception thrown when an unsupported byte order value is specified.
    /// </summary>
    public class UnsupportedByteOrderException : Exception
    {
        /// <summary>
        ///     Gets the unsupported byte order value.
        /// </summary>
        public ByteOrder ByteOrder { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="UnsupportedByteOrderException" /> class.
        /// </summary>
        /// <param name="byteOrder">The unsupported byte order value.</param>
        public UnsupportedByteOrderException(ByteOrder byteOrder) : base($"Unsupported byte order specified: {byteOrder}.")
        {
            ByteOrder = byteOrder;
        }
    }
}