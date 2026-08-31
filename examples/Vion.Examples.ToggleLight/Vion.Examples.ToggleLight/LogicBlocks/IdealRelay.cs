using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Examples.ToggleLight.LogicBlocks
{
    /// <summary>
    ///     The relay the <see cref="Light" /> switches — an <b>ideal</b> one, standing in for hardware so the
    ///     example runs a closed loop on a workstation.
    ///     <para>
    ///         In production a digital output ends at an I/O module, and it is that module — not the lamp —
    ///         that reports back what it applied. Off production there is nothing on the far side of the wire,
    ///         so <c>Light.DigitalOutput.OutputChanged</c> never fires and the light's own confirmation stays
    ///         dark. This block is the far side: it binds the <see cref="IDigitalOutputProvider" /> face of a
    ///         digital output, receives what the light commanded, and confirms exactly that value back. Ideal
    ///         means no delay, no drop, no disagreement.
    ///     </para>
    ///     <para>
    ///         Nothing wires this to the light in code. The topology declares the two contracts to be one wire
    ///         (its <c>contractPairings</c>), and the host re-delivers each side's message to the other without
    ///         transforming it — every behaviour lives here, in ordinary block code. A relay that hesitates,
    ///         sticks, or lies would be the same handler with a different body; see
    ///         <c>docs/simulator-authoring.md</c> for the recipe and for when to model the device instead.
    ///     </para>
    /// </summary>
    [LogicBlock(Name = "Ideales Relais", Icon = "device-line")]
    public class IdealRelay : LogicBlockBase
    {
        private readonly ILogger _logger;

        [ServiceProviderContractBinding(DefaultName = "Lampenausgang")]
        public IDigitalOutputProvider LightChannel { get; private set; }

        /// <summary>What the light last commanded — the proof the command reached the far side.</summary>
        [ServiceProperty(Title = "Zuletzt befohlen")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public bool LastCommand { get; private set; }

        [ServiceProperty(Title = "Empfangene Befehle")]
        [Presentation(Group = PropertyGroup.Metric)]
        public long CommandCount { get; private set; }

        public IdealRelay(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            // The ideal relay, in full: whatever was commanded is what was applied.
            LightChannel.SetReceived += LightChannel_SetReceived;
        }

        private void LightChannel_SetReceived(object sender, bool value)
        {
            _logger.LogInformation("Relay received command {Value}", value);
            LastCommand = value;
            CommandCount++;
            LightChannel.Confirm(value);
        }
    }
}