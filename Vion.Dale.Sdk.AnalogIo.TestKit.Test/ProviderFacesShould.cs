using Moq;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.AnalogIo.TestKit.Test
{
    /// <summary>
    ///     The analog provider faces bind, auto-map and round-trip through the unchanged contract machinery —
    ///     driven with the TestKit's <c>RaiseSetReceived</c> and asserted with the new <c>Verify…</c> helpers.
    /// </summary>
    [TestClass]
    public class ProviderFacesShould
    {
        [TestMethod]
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void Confirm_the_value_an_analog_output_commanded(double commanded)
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.AnalogOutputProvider.RaiseSetReceived(commanded);

            // Assert
            testContext.VerifyAnalogOutputConfirmed(logicBlock.AnalogOutputProvider, commanded, 0.001);
        }

        [TestMethod]
        [DataRow(0.0)]
        [DataRow(3.3)]
        [DataRow(-12.5)]
        public void Drive_the_value_an_analog_input_observes(double commanded)
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.AnalogOutputProvider.RaiseSetReceived(commanded);

            // Assert
            testContext.VerifyAnalogInputDriven(logicBlock.AnalogInputProvider, commanded, 0.001);
        }

        // A provider that never received a command must not have written anything — the negative half, so the
        // assertions above cannot pass on a fixture that confirms unconditionally.
        [TestMethod]
        public void Write_nothing_until_a_command_arrives()
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Assert
            testContext.VerifyAnalogOutputConfirmed(logicBlock.AnalogOutputProvider, times: Times.Never());
            testContext.VerifyAnalogInputDriven(logicBlock.AnalogInputProvider, times: Times.Never());
        }

        [TestMethod]
        public void Confirm_once_per_command()
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.AnalogOutputProvider.RaiseSetReceived(1.5);
            logicBlock.AnalogOutputProvider.RaiseSetReceived(1.5);
            logicBlock.AnalogOutputProvider.RaiseSetReceived(2.5);

            // Assert
            testContext.VerifyAnalogOutputConfirmed(logicBlock.AnalogOutputProvider, 1.5, 0.001, Times.Exactly(2));
            testContext.VerifyAnalogOutputConfirmed(logicBlock.AnalogOutputProvider, 2.5, 0.001, Times.Once());
        }
    }
}