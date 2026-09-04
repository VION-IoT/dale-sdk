using Vion.Dale.Sdk.Modbus.Core.Exceptions;

namespace Vion.Dale.Sdk.Modbus.Core.Test.Exceptions
{
    /// <summary>
    ///     A Modbus failure carries a device exception code only when the device actually refused the request. The
    ///     underlying library reports a frame fault as the same exception type with no code, and the two mean opposite
    ///     things about the link — so the distinction has to be readable, not inferred from a magic value.
    /// </summary>
    [TestClass]
    public class ModbusExceptionShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-MODB-008.3")]
        public void ReportDeviceCodeWhenConstructedWithOne()
        {
            // Arrange / Act
            var exception = new ModbusException(ModbusExceptionCode.IllegalDataAddress, "address out of range");

            // Assert
            Assert.IsTrue(exception.HasExceptionCode);
            Assert.AreEqual(ModbusExceptionCode.IllegalDataAddress, exception.ExceptionCode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-MODB-008.3")]
        public void ReportNoDeviceCodeWhenConstructedWithoutOne()
        {
            // Arrange / Act
            var exception = new ModbusException("no specific exception code");

            // Assert
            Assert.IsFalse(exception.HasExceptionCode);
            Assert.AreEqual((ModbusExceptionCode)0xFF, exception.ExceptionCode, "The sentinel value is unchanged; HasExceptionCode is the test to use.");
        }
    }
}