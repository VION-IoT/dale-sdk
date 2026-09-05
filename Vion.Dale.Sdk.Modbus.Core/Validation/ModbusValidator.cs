using System.Globalization;
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
            if (quantity == 0)
            {
                // Bounded from above only, a zero quantity was dispatched and came back a code-less frame fault,
                // which closes the socket - the same nothing-to-do a count of zero is refused for.
                throw new InvalidCountException(quantity, "Quantity 0 addresses nothing; the device was not contacted.");
            }

            if (quantity > (uint)protocolLimit)
            {
                throw new InvalidCountException(quantity,
                                                string.Format(CultureInfo.InvariantCulture,
                                                              "Quantity {0} exceeds the Modbus protocol limit of {1} for a single request; the device was not contacted.",
                                                              quantity,
                                                              protocolLimit));
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