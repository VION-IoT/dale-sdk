using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Core.Validation
{
    /// <summary>
    ///     Provides validation for Modbus protocol parameters and responses.
    /// </summary>
    public class ModbusValidator : IModbusValidator
    {
        /// <inheritdoc />
        public void ValidateUnitIdentifier(int unitIdentifier)
        {
            if (unitIdentifier is > byte.MaxValue or < 0)
            {
                throw new InvalidUnitIdentifierException(unitIdentifier);
            }
        }

        /// <inheritdoc />
        public void ValidateQuantity(uint quantity, int protocolLimit)
        {
            if (quantity > (uint)protocolLimit)
            {
                throw new InvalidCountException(quantity,
                                                $"Quantity {quantity} exceeds the Modbus protocol limit of {protocolLimit} for a single request; the device was not contacted.");
            }
        }

        /// <inheritdoc />
        public void ValidateResponseAlignment(int byteCount, int bytesPerValue, int unitIdentifier, ushort startingAddress)
        {
            if (byteCount % bytesPerValue != 0)
            {
                throw new ModbusResponseAlignmentException(unitIdentifier, startingAddress, byteCount, bytesPerValue);
            }
        }
    }
}