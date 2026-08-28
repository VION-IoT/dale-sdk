using Moq;
using Vion.Dale.Sdk.TestKit;

namespace Vion.Dale.Sdk.DigitalIo.TestKit.Test
{
    /// <summary>
    ///     The digital provider faces bind, auto-map and round-trip through the unchanged contract machinery —
    ///     driven with the TestKit's <c>RaiseSetReceived</c> and asserted with the new <c>Verify…</c> helpers.
    /// </summary>
    [TestClass]
    public class ProviderFacesShould
    {
        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Confirm_the_value_a_digital_output_commanded(bool commanded)
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.DigitalOutputProvider.RaiseSetReceived(commanded);

            // Assert
            testContext.VerifyDigitalOutputConfirmed(logicBlock.DigitalOutputProvider, commanded);
        }

        [TestMethod]
        [DataRow(true)]
        [DataRow(false)]
        public void Drive_the_value_a_digital_input_observes(bool commanded)
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.DigitalOutputProvider.RaiseSetReceived(commanded);

            // Assert
            testContext.VerifyDigitalInputDriven(logicBlock.DigitalInputProvider, commanded);
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
            testContext.VerifyDigitalOutputConfirmed(logicBlock.DigitalOutputProvider, times: Times.Never());
            testContext.VerifyDigitalInputDriven(logicBlock.DigitalInputProvider, times: Times.Never());
        }

        [TestMethod]
        public void Confirm_once_per_command()
        {
            // Arrange
            var logicBlock = LogicBlockTestHelper.Create<SampleProviderLogicBlock>();
            var testContext = logicBlock.InitializeForTest();

            // Act
            logicBlock.DigitalOutputProvider.RaiseSetReceived(true);
            logicBlock.DigitalOutputProvider.RaiseSetReceived(false);
            logicBlock.DigitalOutputProvider.RaiseSetReceived(true);

            // Assert
            testContext.VerifyDigitalOutputConfirmed(logicBlock.DigitalOutputProvider, true, Times.Exactly(2));
            testContext.VerifyDigitalOutputConfirmed(logicBlock.DigitalOutputProvider, false, Times.Once());
        }
    }
}