namespace Vion.Dale.Sdk.MBus.Test
{
    [TestClass]
    public class MBusStatusShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-MBUS-006.8")]
        [DataRow((byte)0x00, false, DisplayName = "no error")]
        [DataRow((byte)0x01, false, DisplayName = "application busy")]
        [DataRow((byte)0x02, true, DisplayName = "any application error")]
        [DataRow((byte)0x03, true, DisplayName = "abnormal condition")]
        [DataRow((byte)0x04, false, DisplayName = "power low")]
        [DataRow((byte)0x08, true, DisplayName = "permanent error")]
        [DataRow((byte)0x10, true, DisplayName = "temporary error")]
        [DataRow((byte)0x20, false, DisplayName = "manufacturer bit 5, as the EM530 sends")]
        [DataRow((byte)0xE0, false, DisplayName = "every manufacturer bit")]
        public void ReportErrorFromStandardBitsOnly(byte value, bool expected)
        {
            // Arrange
            var status = new MBusStatus(value);

            // Act
            var hasError = status.HasError;

            // Assert
            Assert.AreEqual(expected, hasError);
        }
    }
}