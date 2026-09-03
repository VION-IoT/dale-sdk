using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Dale.DevHost.SmokeHost.LogicBlocks
{
    /// <summary>
    ///     The fixture's <b>ideal I/O module</b> — the reference recipe for the bench
    ///     participant a paired topology needs, and the whole of it. It stands in for the hardware side of two
    ///     channels: an <see cref="IDigitalOutputProvider" /> that confirms back exactly what it was commanded
    ///     (an ideal contact: no delay, no drop, no disagreement), and an <see cref="IDigitalInputProvider" />
    ///     that reports whatever <see cref="InputClosed" /> says.
    ///     <para>
    ///         Pair those two faces to a consumer block's digital output and digital input and the loop closes
    ///         with no host magic: the command reaches this block, its confirmation lights up the consumer's
    ///         <c>OutputChanged</c>, and <see cref="InputClosed" /> is the bench's hand on the input wire. A
    ///         device model with behaviour of its own — take-up, delay, a wrong confirmation — is the sibling
    ///         <see cref="DeviceSimBlock" />; this block deliberately has none, because in production the
    ///         confirmation comes from the I/O module and not from the device.
    ///     </para>
    ///     <para>
    ///         The input is written <b>edge-only</b>, from a timer rather than from a property setter: a paired
    ///         loop converges on block cadence, so a simulator that re-drives an unchanged value on every tick
    ///         adds messages the quiescence barrier must chase for nothing.
    ///     </para>
    /// </summary>
    [LogicBlock(Name = "Ideal I/O", Icon = "device-line")]
    public class IdealIoBlock : LogicBlockBase
    {
        private bool? _lastDriven;

        [ServiceProviderContractBinding(DefaultName = "Ausgangskanal")]
        public IDigitalOutputProvider OutputChannel { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Eingangskanal")]
        public IDigitalInputProvider InputChannel { get; private set; }

        /// <summary>The knob: what the input channel reports — the bench's hand on the wire.</summary>
        [ServiceProperty(Title = "Eingang geschlossen")]
        [Presentation(Group = PropertyGroup.Configuration, Importance = Importance.Primary)]
        public bool InputClosed { get; set; }

        /// <summary>What the paired output last commanded — the proof the command arrived.</summary>
        [ServiceProperty(Title = "Zuletzt befohlen")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public bool LastCommand { get; private set; }

        [ServiceProperty(Title = "Empfangene Befehle")]
        [Presentation(Group = PropertyGroup.Metric)]
        public long CommandCount { get; private set; }

        public IdealIoBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            if (_lastDriven == InputClosed)
            {
                return;
            }

            _lastDriven = InputClosed;
            InputChannel.Drive(InputClosed);
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            // The ideal I/O module, in full: whatever was commanded is what was applied.
            OutputChannel.SetReceived += (_, value) =>
                                         {
                                             LastCommand = value;
                                             CommandCount++;
                                             OutputChannel.Confirm(value);
                                         };
        }
    }
}