using Vion.Dale.Sdk.DigitalIo.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;
using Vion.Examples.ToggleLight.Contracts;
using Vion.Examples.ToggleLight.LogicBlocks;
using Xunit;

namespace Vion.Examples.ToggleLight.Test
{
    public class ToggleShould
    {
        public ToggleShould()
        {
            _sut = new Toggle(LogicBlockTestHelper.CreateLoggerMock().Object);
            _testContext = _sut.CreateTestContext().WithLogicInterfaceMapping(lb => lb, _mappedLight).Build();
        }

        private readonly InterfaceId _mappedLight = new("light-id", "IToggleable");

        private readonly LogicBlockTestContext<Toggle> _testContext;

        private readonly Toggle _sut;

        [Fact]
        public void DigitalInput_ChangedToFalseInInvertedMode_SendEvent()
        {
            // Arrange
            _sut.Mode = Toggle.SignalMode.Inverted;
            _sut.DigitalInput.RaiseInputChanged(true);

            _testContext.ClearRecordedMessages();

            // Act
            _sut.DigitalInput.RaiseInputChanged(false); // release

            // Assert
            _testContext.VerifySendStateUpdate<Toggling.TogglePressed>();
        }

        [Fact]
        public void DigitalInput_ChangedToFalseInNormalMode_SendEvent()
        {
            // Arrange
            _sut.Mode = Toggle.SignalMode.Normal;
            _sut.DigitalInput.RaiseInputChanged(true);

            _testContext.ClearRecordedMessages();

            // Act
            _sut.DigitalInput.RaiseInputChanged(false); // release

            // Assert
            _testContext.VerifySendStateUpdate<Toggling.ToggleReleased>();
        }

        [Fact]
        public void DigitalInput_ChangedToTrueInInvertedMode_SendEvent()
        {
            // Arrange
            _sut.Mode = Toggle.SignalMode.Inverted;

            // Act
            _sut.DigitalInput.RaiseInputChanged(true); // press

            // Assert
            _testContext.VerifySendStateUpdate<Toggling.ToggleReleased>();
        }

        [Fact]
        public void DigitalInput_ChangedToTrueInNormalMode_SendEvent()
        {
            // Arrange
            _sut.Mode = Toggle.SignalMode.Normal;

            // Act
            _sut.DigitalInput.RaiseInputChanged(true); // press

            // Assert
            _testContext.VerifySendStateUpdate<Toggling.TogglePressed>();
        }
    }
}