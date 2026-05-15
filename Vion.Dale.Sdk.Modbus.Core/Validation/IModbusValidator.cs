using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Core.Validation
{
    /// <summary>
    ///     Provides validation for Modbus parameters and responses.
    /// </summary>
    public interface IModbusValidator
    {
        /// <summary>
        ///     Validates that a unit identifier is within the valid Modbus range.
        /// </summary>
        /// <param name="unitIdentifier">The unit identifier (slave address) to validate.</param>
        /// <exception cref="InvalidUnitIdentifierException">
        ///     Thrown when <paramref name="unitIdentifier" /> is less than 0 or greater than 255.
        /// </exception>
        void ValidateUnitIdentifier(int unitIdentifier);

        /// <summary>
        ///     Validates that a response has the correct byte alignment for the requested data type.
        /// </summary>
        /// <param name="byteCount">The number of bytes received in the response.</param>
        /// <param name="bytesPerValue">The expected number of bytes per value.</param>
        /// <param name="unitIdentifier">The unit identifier (slave address) from which the response was received.</param>
        /// <param name="startingAddress">The starting address of the read operation.</param>
        /// <exception cref="ModbusResponseAlignmentException">
        ///     Thrown when <paramref name="byteCount" /> is not a multiple of <paramref name="bytesPerValue" />.
        /// </exception>
        void ValidateResponseAlignment(int byteCount, int bytesPerValue, int unitIdentifier, ushort startingAddress);
    }
}