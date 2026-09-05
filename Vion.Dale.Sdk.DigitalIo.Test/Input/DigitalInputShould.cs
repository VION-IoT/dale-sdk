using System.Collections.Generic;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.DigitalIo.Test.TestHelpers;

namespace Vion.Dale.Sdk.DigitalIo.Test.Input
{
    /// <summary>
    ///     The face a block holds to read a digital input: what it raises, when, and what it does with a
    ///     message meant for another face.
    /// </summary>
    [TestClass]
    public class DigitalInputShould
    {
        private readonly ContractHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.7")]
        public void RaiseInputChangedForEveryDeliveryIncludingRepeatedValue()
        {
            // Arrange
            var input = _harness.Input();
            var observed = new List<bool>();
            input.InputChanged += (_, value) => observed.Add(value);

            // Act
            ContractHarness.Deliver(input, "di0", new DigitalInputChanged(true));
            ContractHarness.Deliver(input, "di0", new DigitalInputChanged(true));
            ContractHarness.Deliver(input, "di0", new DigitalInputChanged(false));

            // Assert — no deadband and no de-duplication here, unlike a service property's emission: a block
            // that wants edges compares for itself.
            CollectionAssert.AreEqual(new[] { true, true, false }, observed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-003.3")]
        public void RaiseNothingWhenDeliveredMessageBelongsToAnotherFace()
        {
            // Arrange
            var input = _harness.Input();
            var raised = 0;
            input.InputChanged += (_, _) => raised++;

            // Act
            ContractHarness.Deliver(input, "di0", new DigitalOutputChanged(true));

            // Assert
            Assert.AreEqual(0, raised);
        }
    }
}