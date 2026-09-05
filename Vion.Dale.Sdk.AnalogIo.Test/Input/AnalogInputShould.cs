using System.Collections.Generic;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.AnalogIo.Test.TestHelpers;

namespace Vion.Dale.Sdk.AnalogIo.Test.Input
{
    /// <summary>
    ///     The face a block holds to read a analog input: what it raises, when, and what it does with a
    ///     message meant for another face.
    /// </summary>
    [TestClass]
    public class AnalogInputShould
    {
        private readonly ContractHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.7")]
        public void RaiseInputChangedForEveryDeliveryIncludingRepeatedValue()
        {
            // Arrange
            var input = _harness.Input();
            var observed = new List<double>();
            input.InputChanged += (_, value) => observed.Add(value);

            // Act
            ContractHarness.Deliver(input, "ai0", new AnalogInputChanged(4.2));
            ContractHarness.Deliver(input, "ai0", new AnalogInputChanged(4.2));
            ContractHarness.Deliver(input, "ai0", new AnalogInputChanged(-12.5));

            // Assert — no deadband and no de-duplication here, unlike a service property's emission: a block
            // that wants edges compares for itself.
            CollectionAssert.AreEqual(new[] { 4.2, 4.2, -12.5 }, observed);
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
            ContractHarness.Deliver(input, "ai0", new AnalogOutputChanged(4.2));

            // Assert
            Assert.AreEqual(0, raised);
        }
    }
}
