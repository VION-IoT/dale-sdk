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