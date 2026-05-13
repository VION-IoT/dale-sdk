using Vion.Dale.Sdk.DigitalIo.TestKit;
using Vion.Dale.Sdk.TestKit;
using Vion.Dale.Sdk.Utils;
using Moq;
using Vion.Examples.ToggleLight.Contracts;
using Vion.Examples.ToggleLight.LogicBlocks;
using Xunit;

namespace Vion.Examples.ToggleLight.Test
{
    public class LightShould
    {
        public LightShould()
        {
            _sut = new Light(LogicBlockTestHelper.CreateLoggerMock().Object);
            _testContext = _sut.CreateTestContext().WithLogicInterfaceMapping(lb => lb, _mappedToggle).Build();
        }

        private readonly InterfaceId _mappedToggle = new("toggle-id", "IToggler");

        private readonly LogicBlockTestContext<Light> _testContext;

        private readonly Light _sut;

        [Fact]
        public void HandleTogglePressed_InToggleOnPressedMode_SetsDigitalOutput_ToTrue()
        {
            // Arrange
            _sut.ToggleMode = Light.Mode.ToggleOnPressed;

            // Act
            _sut.HandleStateUpdate(_mappedToggle, new Toggling.TogglePressed());

            // Assert
            _testContext.VerifyDigitalOutputSet(_sut.DigitalOutput, true);
        }

        [Fact]
        public void HandleTogglePressed_InToggleOnReleasedMode_DoNothing()
        {
            // Arrange
            _sut.ToggleMode = Light.Mode.ToggleOnReleased;

            // Act
            _sut.HandleStateUpdate(_mappedToggle, new Toggling.TogglePressed());

            // Assert
            _testContext.VerifyDigitalOutputSet(_sut.DigitalOutput, true, Times.Never());
        }

        [Fact]
        public void HandleToggleReleased_InToggleOnPressedMode_DoNothing()
        {
            // Arrange
            _sut.ToggleMode = Light.Mode.ToggleOnPressed;

            // Act
            _sut.HandleStateUpdate(_mappedToggle, new Toggling.ToggleReleased());

            // Assert
            _testContext.VerifyDigitalOutputSet(_sut.DigitalOutput, true, Times.Never());
        }

        [Fact]
        public void HandleToggleReleased_InToggleOnReleasedMode_SetsDigitalOutput_ToTrue()
        {
            // Arrange
            _sut.ToggleMode = Light.Mode.ToggleOnReleased;

            // Act
            _sut.HandleStateUpdate(_mappedToggle, new Toggling.ToggleReleased());

            // Assert
            _testContext.VerifyDigitalOutputSet(_sut.DigitalOutput, true);
        }

        [Fact]
        public void On_SetToFalse_SetDigitalOutput_ToFalse()
        {
            // Arrange
            _sut.On = true;
            _testContext.ClearRecordedMessages();

            // Act
            _sut.On = false;

            // Assert
            _testContext.VerifyDigitalOutputSet(_sut.DigitalOutput, false);
        }

        [Fact]
        public void On_SetToTrue_SetDigitalOutput_ToTrue()
        {
            // Arrange

            // Act
            _sut.On = true;

            // Assert
            _testContext.VerifyDigitalOutputSet(_sut.DigitalOutput, true);
        }
    }
}