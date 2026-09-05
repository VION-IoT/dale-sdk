using System.Linq;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.AnalogIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.AnalogIo.Test.Output
{
    /// <summary>
    ///     The face a block holds to drive a analog output: the message a command becomes, what a block can
    ///     observe when the configuration mapped nothing to it, and what a message meant for another face does.
    /// </summary>
    [TestClass]
    public class AnalogOutputShould
    {
        private readonly ContractHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-IO-003.2")]
        [DataRow(0.0)]
        [DataRow(4.2)]
        [DataRow(-12.5)]
        public void SendCommandCarryingValueToLinkedHandler(double value)
        {
            // Arrange
            var output = _harness.Output();

            // Act
            output.Set(value);

            // Assert
            Assert.AreEqual(value, _harness.Sent.OfType<ContractMessage<SetAnalogOutput>>().Single().Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-003.2")]
        public void SendNothingWhenConfigurationMappedNothingToFace()
        {
            // Arrange — a face a configuration never mapped: the block still holds it and it still accepts a command.
            var output = _harness.UnmappedOutput();

            // Act
            output.Set(4.2);

            // Assert — the command is dropped and the block is told nothing; there is no answer, no throw and
            // no way to ask whether the face is mapped.
            Assert.IsEmpty(_harness.Sent);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-003.3")]
        public void RaiseNothingWhenDeliveredMessageBelongsToAnotherFace()
        {
            // Arrange
            var output = _harness.Output();
            var raised = 0;
            output.OutputChanged += (_, _) => raised++;

            // Act
            ContractHarness.Deliver(output, "ao0", new AnalogInputChanged(4.2));

            // Assert
            Assert.AreEqual(0, raised);
        }
    }
}