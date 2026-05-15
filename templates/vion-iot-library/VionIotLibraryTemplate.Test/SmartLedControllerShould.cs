using Vion.Dale.Sdk.DigitalIo.TestKit;
using Vion.Dale.Sdk.TestKit;
using Moq;
using Xunit;

namespace VionIotLibraryTemplate.Test
{
    public class SmartLedControllerShould
    {
        public SmartLedControllerShould()
        {
            var loggerMock = LogicBlockTestHelper.CreateLoggerMock();
            _smartLedController = new SmartLedController(loggerMock.Object);
            _testContext = _smartLedController.InitializeForTest();
        }

        private readonly SmartLedController _smartLedController;

        private readonly LogicBlockTestContext<SmartLedController> _testContext;

        [Fact]
        public void ButtonInput_Pressed_IncrementsButtonPressCount()
        {
            // Arrange
            var buttonPressCountBefore = _smartLedController.ButtonPressCount;

            // Act
            _smartLedController.Button.RaiseInputChanged(true); // press

            // Assert
            var buttonPressCountAfter = _smartLedController.ButtonPressCount;
            Assert.Equal(buttonPressCountBefore + 1, buttonPressCountAfter);
        }

        [Fact]
        public void ButtonInput_Released_DoesNotIncrementButtonPressCount()
        {
            // Arrange
            var buttonPressCountBefore = _smartLedController.ButtonPressCount;

            // Act
            _smartLedController.Button.RaiseInputChanged(false); // release

            // Assert
            var buttonPressCountAfter = _smartLedController.ButtonPressCount;
            Assert.Equal(buttonPressCountBefore, buttonPressCountAfter);
        }

        [Fact]
        public void LedEnabled_SetToTrueInManualMode_SetsLedOutputToTrue()
        {
            // Arrange
            _smartLedController.Mode = SmartLedController.LedMode.Manual;

            // Act
            _smartLedController.LedEnabled = true;

            // Assert
            _testContext.VerifyDigitalOutputSet(_smartLedController.Led, true, Times.Once()); // use test context to verify i/o side effects
        }
    }
}