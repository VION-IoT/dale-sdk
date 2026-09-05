using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Mqtt;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.DigitalIo.Test.TestHelpers;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.Sdk.DigitalIo.Test
{
    /// <summary>
    ///     The four round-trips of the digital family, each driven end to end across the pair of faces that
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
        [DataRow(true)]
        [DataRow(false)]
        public void DeliverCommandToOutputProvider(bool commanded)
        {
            // Arrange
            var output = _harness.Output();
            var provider = _harness.OutputProvider();
            bool? received = null;
            provider.SetReceived += (_, value) => received = value;

            // Act
            output.Set(commanded);
            ContractHarness.Deliver(provider, "dop0", _harness.Sent.OfType<ContractMessage<SetDigitalOutput>>().Single().Data);

            // Assert
            Assert.AreEqual(commanded, received);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.2")]
        [DataRow(true)]
        [DataRow(false)]
        public void DeliverConfirmationToOutput(bool applied)
        {
            // Arrange
            var provider = _harness.OutputProvider();
            var output = _harness.Output();
            bool? observed = null;
            output.OutputChanged += (_, value) => observed = value;

            // Act
            provider.Confirm(applied);
            ContractHarness.Deliver(output, "do0", _harness.Sent.OfType<ContractMessage<DigitalOutputChanged>>().Single().Data);

            // Assert
            Assert.AreEqual(applied, observed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.3")]
        [DataRow(true)]
        [DataRow(false)]
        public void DeliverDrivenValueToInput(bool driven)
        {
            // Arrange
            var provider = _harness.InputProvider();
            var input = _harness.Input();
            bool? observed = null;
            input.InputChanged += (_, value) => observed = value;

            // Act
            provider.Drive(driven);
            ContractHarness.Deliver(input, "di0", _harness.Sent.OfType<ContractMessage<DigitalInputChanged>>().Single().Data);

            // Assert
            Assert.AreEqual(driven, observed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-IO-002.4")]
        [DataRow(true)]
        [DataRow(false)]
        public void DeliverStateFromWireToInput(bool state)
        {
            // Arrange — the production half of the same trip: the value arrives on the wire rather than from a peer face.
            var handlerHarness = new HandlerHarness();
            var handler = new DigitalInputHandler(NullLogger<DigitalInputHandler>.Instance);
            handlerHarness.Link(handler);
            var input = _harness.Input();
            bool? observed = null;
            input.InputChanged += (_, value) => observed = value;

            // Act
            handlerHarness.Send(handler, HandlerHarness.MqttMessage(HandlerHarness.StateTopic(Topics.DiState), HandlerHarness.DigitalStatePayload(state)));
            ContractHarness.Deliver(input, "di0", handlerHarness.Forwarded<DigitalInputChanged>().Single().Data);

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
            provider.SetReceived += (_, value) => provider.Confirm(!value);
            bool? observed = null;
            output.OutputChanged += (_, value) => observed = value;

            // Act
            output.Set(true);
            ContractHarness.Deliver(provider, "dop0", _harness.Sent.OfType<ContractMessage<SetDigitalOutput>>().Single().Data);
            ContractHarness.Deliver(output, "do0", _harness.Sent.OfType<ContractMessage<DigitalOutputChanged>>().Single().Data);

            // Assert
            Assert.IsFalse(observed);
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
            output.Set(true);
            ContractHarness.Deliver(provider, "dop0", _harness.Sent.OfType<ContractMessage<SetDigitalOutput>>().Single().Data);

            // Assert — no timeout and no optimistic echo: an unanswered command leaves the event silent for good.
            Assert.AreEqual(0, raised);
        }
    }
}