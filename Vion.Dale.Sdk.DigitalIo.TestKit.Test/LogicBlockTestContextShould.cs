using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.DigitalIo.TestKit.Test
{
    [TestClass]
    public class LogicBlockTestContextShould
    {
        [TestMethod]
        public void RaiseDigitalInputChanged()
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.DigitalInput.RaiseInputChanged(true);

            // Assert
            testContext.VerifyDigitalOutputSet(logicBlock.DigitalOutput, true);
        }

        [TestMethod]
        public void VerifyDigitalOutputNotSetWhenInputFalse()
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.DigitalInput.RaiseInputChanged(false);

            // Assert
            testContext.VerifyDigitalOutputSet(logicBlock.DigitalOutput, false);
        }
    }
}