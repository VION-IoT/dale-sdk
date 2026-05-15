using System;

namespace Vion.Dale.Sdk.Modbus.Core.Exceptions
{
    /// <summary>
    ///     Represents an exception thrown when a Modbus response does not have the correct byte alignment for the requested data type.
    /// </summary>
    /// <remarks>
    ///     This occurs when the number of bytes received does not match the expected amount for the requested registers.
    ///     For example, reading 2 registers expects 4 bytes; if 5 bytes are returned, this exception is thrown.
    /// </remarks>
    public class ModbusResponseAlignmentException : Exception
    {
        /// <summary>
        ///     Gets the unit identifier (slave address) from which the response was received.
        /// </summary>
        public int UnitIdentifier { get; }

        /// <summary>
        ///     Gets the starting address of the read operation.
        /// </summary>
        public ushort StartingAddress { get; }

        /// <summary>
        ///     Gets the number of bytes received in the response.
        /// </summary>
        public int ByteCount { get; }

        /// <summary>
        ///     Gets the number of bytes per value that was expected.
        /// </summary>
        public int BytesPerValue { get; }

        /// <summary>
        ///     Initializes a new instance of the <see cref="ModbusResponseAlignmentException" /> class.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address).</param>
        /// <param name="startingAddress">The starting address of the read operation.</param>
        /// <param name="byteCount">The number of bytes received.</param>
        /// <param name="bytesPerValue">The expected number of bytes per value.</param>
        public ModbusResponseAlignmentException(int unitIdentifier, ushort startingAddress, int byteCount, int bytesPerValue) :
            base($"Invalid response from unit {unitIdentifier} starting at address {startingAddress}: " +
                 $"received {byteCount} bytes, but {bytesPerValue * 8}-bit values require a multiple of {bytesPerValue} bytes.")
        {
            UnitIdentifier = unitIdentifier;
            StartingAddress = startingAddress;
            ByteCount = byteCount;
            BytesPerValue = bytesPerValue;
        }
    }
}