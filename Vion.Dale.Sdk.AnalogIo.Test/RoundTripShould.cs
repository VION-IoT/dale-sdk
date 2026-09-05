using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.AnalogIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.AnalogIo.Test
{
    /// <summary>
    ///     The four round-trips of the analog family, each driven end to end across the pair of faces that
    ///     carries it: what one side writes is delivered to the other as the message its handler declares, and
    ///     the other side raises its own event with the value that arrived. The delivery hop the DevHost or the
    ///     runtime performs is the test's Act — nothing here pairs the faces for real.
    ///     <para>
    ///         Cross-tier: the same four trips are proven at Tier 1 by the SmokeHost's
    ///         <c>provider-faces.scenario.json</c> and <c>paired-loop.scenario.json</c> over a live host; this
    ///         suite owns the in-process half — the message each operation sends and the event each delivery raises.
    ///     </para>
    /// </summary>
    [TestClass]
    public class RoundTripShould
    {
        private readonly ContractHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.1")]
        [DataRow(0.0)]
        [DataRow(4.2)]
        [DataRow(-12.5)]
        public void DeliverCommandToOutputProvider(double commanded)
        {
            // Arrange
            var output = _harness.Output();
            var provider = _harness.OutputProvider();
            double? received = null;
            provider.SetReceived += (_, value) => received = value;

            // Act
            output.Set(commanded);
            ContractHarness.Deliver(provider, "aop0", _harness.Sent.OfType<ContractMessage<SetAnalogOutput>>().Single().Data);

            // Assert
            Assert.AreEqual(commanded, received);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.2")]
        [DataRow(0.0)]
        [DataRow(4.2)]
        [DataRow(-12.5)]
        public void DeliverConfirmationToOutput(double applied)
        {
            // Arrange
            var provider = _harness.OutputProvider();
            var output = _harness.Output();
            double? observed = null;
            output.OutputChanged += (_, value) => observed = value;

            // Act
            provider.Confirm(applied);
            ContractHarness.Deliver(output, "ao0", _harness.Sent.OfType<ContractMessage<AnalogOutputChanged>>().Single().Data);

            // Assert
            Assert.AreEqual(applied, observed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.3")]
        [DataRow(0.0)]
        [DataRow(4.2)]
        [DataRow(-12.5)]
        public void DeliverDrivenValueToInput(double driven)
        {
            // Arrange
            var provider = _harness.InputProvider();
            var input = _harness.Input();
            double? observed = null;
            input.InputChanged += (_, value) => observed = value;

            // Act
            provider.Drive(driven);
            ContractHarness.Deliver(input, "ai0", _harness.Sent.OfType<ContractMessage<AnalogInputChanged>>().Single().Data);

            // Assert
            Assert.AreEqual(driven, observed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.4")]
        [DataRow(0.0)]
        [DataRow(4.2)]
        [DataRow(-12.5)]
        public void DeliverStateFromWireToInput(double state)
        {
            // Arrange — the production half of the same trip: the value arrives on the wire rather than from a peer face.
            var handlerHarness = new HandlerHarness();
            var handler = new AnalogInputHandler(NullLogger<AnalogInputHandler>.Instance);
            handlerHarness.Link(handler);
            var input = _harness.Input();
            double? observed = null;
            input.InputChanged += (_, value) => observed = value;

            // Act
            handlerHarness.Send(handler, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.AiState), HandlerHarness.AnalogStatePayload(state)));
            ContractHarness.Deliver(input, "ai0", handlerHarness.Forwarded<AnalogInputChanged>().Single().Data);

            // Assert
            Assert.AreEqual(state, observed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.5")]
        public void DeliverConfirmationCarryingValueProviderApplied()
        {
            // Arrange — an output provider that models equipment reporting the inverse of what it was told, which is
            // the fault a consumer's self-test looks for; the confirmation must carry that, not the command.
            var output = _harness.Output();
            var provider = _harness.OutputProvider();
            provider.SetReceived += (_, value) => provider.Confirm(value / 2);
            double? observed = null;
            output.OutputChanged += (_, value) => observed = value;

            // Act
            output.Set(4.2);
            ContractHarness.Deliver(provider, "aop0", _harness.Sent.OfType<ContractMessage<SetAnalogOutput>>().Single().Data);
            ContractHarness.Deliver(output, "ao0", _harness.Sent.OfType<ContractMessage<AnalogOutputChanged>>().Single().Data);

            // Assert
            Assert.AreEqual(2.1, observed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.6")]
        public void LeaveOutputChangedSilentUntilProviderConfirms()
        {
            // Arrange
            var output = _harness.Output();
            var provider = _harness.OutputProvider();
            provider.SetReceived += (_, _) => { }; // equipment that took the command and answers nothing — a legitimate model.
            var raised = 0;
            output.OutputChanged += (_, _) => raised++;

            // Act
            output.Set(4.2);
            ContractHarness.Deliver(provider, "aop0", _harness.Sent.OfType<ContractMessage<SetAnalogOutput>>().Single().Data);

            // Assert — no timeout and no optimistic echo: an unanswered command leaves the event silent for good.
            Assert.AreEqual(0, raised);
        }
    }
}
