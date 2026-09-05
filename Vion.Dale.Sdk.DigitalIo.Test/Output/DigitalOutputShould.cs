using System.Linq;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.DigitalIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.DigitalIo.Test.Output
{
    /// <summary>
    ///     The face a block holds to drive a digital output: the message a command becomes, what a block can
    ///     observe when the configuration mapped nothing to it, and what a message meant for another face does.
    /// </summary>
    [TestClass]
    public class DigitalOutputShould
    {
        private readonly ContractHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-IO-003.2")]
        [DataRow(true)]
        [DataRow(false)]
        public void SendCommandCarryingValueToLinkedHandler(bool value)
        {
            // Arrange
            var output = _harness.Output();

            // Act
            output.Set(value);

            // Assert
            Assert.AreEqual(value, _harness.Sent.OfType<ContractMessage<SetDigitalOutput>>().Single().Data.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-003.2")]
        public void SendNothingWhenConfigurationMappedNothingToFace()
        {
            // Arrange — a face a configuration never mapped: the block still holds it and it still accepts a command.
            var output = _harness.UnmappedOutput();

            // Act
            output.Set(true);

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
            ContractHarness.Deliver(output, "do0", new DigitalInputChanged(true));

            // Assert
            Assert.AreEqual(0, raised);
        }
    }
}