using Vion.Dale.Sdk.TestKit;
using Moq;

namespace Vion.Dale.Sdk.AnalogIo.TestKit.Test
{
    [TestClass]
    public class LogicBlockTestContextShould
    {
        [TestMethod]
        public void RaiseAnalogInputChanged()
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.AnalogInput.RaiseInputChanged(5.0);

            // Assert
            testContext.VerifyAnalogOutputSet(logicBlock.AnalogOutput, 10.0, 0.001); // Logic block doubles the input value on the analog output
        }

        [TestMethod]
        public void RaiseAnalogInputChangedMultipleTimes()
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.AnalogInput.RaiseInputChanged(1.0);
            logicBlock.AnalogInput.RaiseInputChanged(2.0);
            logicBlock.AnalogInput.RaiseInputChanged(3.0);

            // Assert
            testContext.VerifyAnalogOutputSet(times: Times.Exactly(3));
        }
    }
}