using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Validation;

namespace Vion.Dale.Sdk.Modbus.Core.Test.Validation
{
    [TestClass]
    public class ModbusValidatorShould
    {
        private readonly ModbusValidator _sut = new();

        [TestMethod]
        [DataRow(-1)]
        [DataRow(256)]
        [DataRow(1000)]
        public void ThrowExceptionWhenUnitIdentifierIsInvalid(int unitId)
        {
            // Arrange

            // Act & Assert
            Assert.Throws<InvalidUnitIdentifierException>(() => _sut.ValidateUnitIdentifier(unitId));
        }

        [TestMethod]
        [DataRow(0)]
        [DataRow(10)]
        [DataRow(255)]
        public void NotThrowExceptionWhenUnitIdentifierIsValid(int unitId)
        {
            // Arrange

            // Act & Assert
            _sut.ValidateUnitIdentifier(unitId);
        }

        [TestMethod]
        [DataRow(126u, ModbusProtocolLimits.MaxRegistersPerRead, DisplayName = "One register past the read limit")]
        [DataRow(124u, ModbusProtocolLimits.MaxRegistersPerWrite, DisplayName = "One register past the write limit")]
        [DataRow(2001u, ModbusProtocolLimits.MaxBitsPerRead, DisplayName = "One bit past the read limit")]
        [DataRow(1969u, ModbusProtocolLimits.MaxBitsPerWrite, DisplayName = "One bit past the write limit")]
        [DataRow(65535u, ModbusProtocolLimits.MaxRegistersPerRead, DisplayName = "The former ceiling")]
        public void ThrowExceptionWhenQuantityExceedsTheProtocolLimit(uint quantity, int protocolLimit)
        {
            // Arrange

            // Act & Assert
            Assert.ThrowsExactly<InvalidCountException>(() => _sut.ValidateQuantity(quantity, protocolLimit));
        }

        [TestMethod]
        [DataRow(125u, ModbusProtocolLimits.MaxRegistersPerRead, DisplayName = "Exactly the read limit")]
        [DataRow(123u, ModbusProtocolLimits.MaxRegistersPerWrite, DisplayName = "Exactly the write limit")]
        [DataRow(2000u, ModbusProtocolLimits.MaxBitsPerRead, DisplayName = "Exactly the bit read limit")]
        [DataRow(1968u, ModbusProtocolLimits.MaxBitsPerWrite, DisplayName = "Exactly the bit write limit")]
        [DataRow(1u, ModbusProtocolLimits.MaxRegistersPerWrite, DisplayName = "A single register")]
        public void NotThrowExceptionWhenQuantityIsWithinTheProtocolLimit(uint quantity, int protocolLimit)
        {
            // Arrange

            // Act & Assert
            _sut.ValidateQuantity(quantity, protocolLimit);
        }

        [TestMethod]
        public void ThrowExceptionWhenResponseAlignmentIsInvalid()
        {
            // Arrange
            const int byteCount = 3;
            const int bytesPerValue = 2;

            // Act & Assert
            Assert.Throws<ModbusResponseAlignmentException>(() => _sut.ValidateResponseAlignment(byteCount, bytesPerValue, 0, 0));
        }

        [TestMethod]
        public void NotThrowExceptionWhenResponseAlignmentIsValid()
        {
            // Arrange
            const int byteCount = 4;
            const int bytesPerValue = 2;

            // Act & Assert
            _sut.ValidateResponseAlignment(byteCount, bytesPerValue, 0, 0);
        }
    }
}