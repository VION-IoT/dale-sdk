using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
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
            DigitalOutput.OutputChanged += DigitalOutput_OutputChanged;
        }

        private void DigitalOutput_OutputChanged(object sender, bool e)
        {
            _logger.LogInformation("Digital output changed to {Value}", e);
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