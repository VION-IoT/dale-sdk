using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Utils;
using Vion.Examples.ToggleLight.Contracts;

namespace Vion.Examples.ToggleLight.LogicBlocks
{
    [LogicBlock(Name = "Licht", Icon = "lightbulb-line")]
    public class Light : LogicBlockBase, IToggleable
    {
        public enum Mode
        {
            [EnumLabel("Schalten bei Druck")]
            ToggleOnPressed,

            [EnumLabel("Schalten bei Loslassen")]
            ToggleOnReleased,
        }

        private readonly ILogger _logger;

        private bool _on;

        public IDigitalOutput DigitalOutput { get; set; }

        [ServiceProperty(Title = "Tastermodus")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public Mode ToggleMode { get; set; } = Mode.ToggleOnPressed;

        [ServiceProperty(Title = "Ein")]
        [ServiceMeasuringPoint]
        [Presentation(Importance = Importance.Primary)]
        public bool On
        {
            get => _on;

            set
            {
                if (value != _on)
                {
                    SetDigitalOutput(value);
                    _on = value;
                }
            }
        }

        /// <summary>
        ///     What the relay on the far side of the digital output reported back. <see cref="On" /> is what
        ///     this block asked for; this is what actually happened — the two agree only once the confirmation
        ///     has arrived, and a bench whose far side is silent leaves it dark.
        /// </summary>
        [ServiceProperty(Title = "Ein bestätigt")]
        [Presentation(Group = PropertyGroup.Status)]
        public bool ConfirmedOn { get; private set; }

        [ServiceProperty(Title = "Anzahl Einschaltungen")]
        [Presentation(Group = PropertyGroup.Metric, Importance = Importance.Secondary)]
        public int TimesSwitchedOn { get; private set; }

        [ServiceProperty(Title = "Nutzungsdauer Total")]
        [Presentation(Group = PropertyGroup.Metric)]
        public TimeSpan TotalTimeOn { get; private set; }

        public Light(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, Toggling.TogglePressed response)
        {
            if (ToggleMode == Mode.ToggleOnPressed)
            {
                On = !On;
            }
        }

        /// <inheritdoc />
        public void HandleStateUpdate(InterfaceId functionId, Toggling.ToggleReleased response)
        {
            if (ToggleMode == Mode.ToggleOnReleased)
            {
                On = !On;
            }
        }

        [Timer(1)]
        public void UpdateTotalTimeOn()
        {
            if (On)
            {
                TotalTimeOn += TimeSpan.FromSeconds(1);
            }
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            // The other half of a digital output: the far side reports what it applied. On a device that is
            // the I/O module; in this example it is the IdealRelay block, joined to this output by the
            // topology's contract pairing. Without such a far side the event simply never fires.
            DigitalOutput.OutputChanged += DigitalOutput_OutputChanged;
        }

        private void DigitalOutput_OutputChanged(object sender, bool e)
        {
            _logger.LogInformation("Digital output changed to {Value}", e);
            ConfirmedOn = e;
        }

        private void SetDigitalOutput(bool value)
        {
            _logger.LogInformation("Setting digital output to {Value}", value);
            DigitalOutput.Set(value);
            if (value)
            {
                TimesSwitchedOn++;
            }
        }
    }
}