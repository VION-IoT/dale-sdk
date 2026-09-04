using Vion.Dale.Sdk.Modbus.Core.Exceptions;
using Vion.Dale.Sdk.Modbus.Core.Validation;

namespace Vion.Dale.Sdk.Modbus.Core.Test.Validation
{
    [TestClass]
    public class ModbusValidatorShould
    {
        private readonly ModbusValidator _sut = new();

        [TestMethod]
        [TestProperty("spec", "AC-MODB-004.1")]
        [DataRow(-1)]
        [DataRow(256)]
        [DataRow(1000)]
        public void ThrowExceptionWhenUnitIdentifierInvalid(int unitId)
        {
            // Arrange

            // Act & Assert
            Assert.Throws<InvalidUnitIdentifierException>(() => _sut.ValidateUnitIdentifier(unitId));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-004.2")]
        [DataRow(0)]
        [DataRow(10)]
        [DataRow(255)]
        public void NotThrowExceptionWhenUnitIdentifierValid(int unitId)
        {
            // Arrange

            // Act & Assert
            _sut.ValidateUnitIdentifier(unitId);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-004.4")]
        [DataRow(126u, ModbusProtocolLimits.MaxRegistersPerRead, DisplayName = "One register past the read limit")]
        [DataRow(124u, ModbusProtocolLimits.MaxRegistersPerWrite, DisplayName = "One register past the write limit")]
        [DataRow(2001u, ModbusProtocolLimits.MaxBitsPerRead, DisplayName = "One bit past the read limit")]
        [DataRow(1969u, ModbusProtocolLimits.MaxBitsPerWrite, DisplayName = "One bit past the write limit")]
        [DataRow(65535u, ModbusProtocolLimits.MaxRegistersPerRead, DisplayName = "The former ceiling")]
        public void ThrowExceptionWhenQuantityExceedsProtocolLimit(uint quantity, int protocolLimit)
        {
            // Arrange

            // Act & Assert
            Assert.ThrowsExactly<InvalidCountException>(() => _sut.ValidateQuantity(quantity, protocolLimit));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-004.4")]
        [DataRow(125u, ModbusProtocolLimits.MaxRegistersPerRead, DisplayName = "Exactly the read limit")]
        [DataRow(123u, ModbusProtocolLimits.MaxRegistersPerWrite, DisplayName = "Exactly the write limit")]
        [DataRow(2000u, ModbusProtocolLimits.MaxBitsPerRead, DisplayName = "Exactly the bit read limit")]
        [DataRow(1968u, ModbusProtocolLimits.MaxBitsPerWrite, DisplayName = "Exactly the bit write limit")]
        [DataRow(1u, ModbusProtocolLimits.MaxRegistersPerWrite, DisplayName = "A single register")]
        public void NotThrowExceptionWhenQuantityWithinProtocolLimit(uint quantity, int protocolLimit)
        {
            // Arrange

            // Act & Assert
            _sut.ValidateQuantity(quantity, protocolLimit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.4")]
        public void ThrowExceptionWhenResponseAlignmentInvalid()
        {
            // Arrange
            const int byteCount = 3;
            const int bytesPerValue = 2;

            // Act & Assert
            Assert.Throws<ModbusResponseAlignmentException>(() => _sut.ValidateResponseAlignment(byteCount, bytesPerValue, 0, 0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-005.4")]
        public void NotThrowExceptionWhenResponseAlignmentValid()
        {
            // Arrange
            const int byteCount = 4;
            const int bytesPerValue = 2;

            // Act & Assert
            _sut.ValidateResponseAlignment(byteCount, bytesPerValue, 0, 0);
        }
    }
}