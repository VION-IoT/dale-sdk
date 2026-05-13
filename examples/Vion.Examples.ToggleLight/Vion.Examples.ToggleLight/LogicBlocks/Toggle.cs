using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Microsoft.Extensions.Logging;
using Vion.Examples.ToggleLight.Contracts;

namespace Vion.Examples.ToggleLight.LogicBlocks
{
    [LogicBlock(Name = "Taster", Icon = "toggle-line")]
    public class Toggle : LogicBlockBase, IToggler
    {
        public enum SignalMode
        {
            [EnumLabel("Normal")]
            Normal,

            [EnumLabel("Invertiert")]
            Inverted,
        }

        private readonly ILogger _logger;

        private bool _lastValue;

        [ServiceProperty(Title = "Signalmodus")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public SignalMode Mode { get; set; } = SignalMode.Normal;

        public IDigitalInput DigitalInput { get; set; }

        /// <inheritdoc />
        public Toggle(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            DigitalInput.InputChanged += DigitalInput_InputChanged;
        }

        private void DigitalInput_InputChanged(object sender, bool value)
        {
            if (value == _lastValue)
            {
                _logger.LogInformation("[{Id}] Toggle unchanged {Value}", Id, value);
                return;
            }

            _lastValue = value;

            if ((value && Mode == SignalMode.Normal) || (!value && Mode == SignalMode.Inverted))
            {
                _logger.LogInformation("[{Id}] Toggle pressed", Id);
                this.SendStateUpdate(new Toggling.TogglePressed());
            }
            else
            {
                _logger.LogInformation("[{Id}] Toggle released", Id);
                this.SendStateUpdate(new Toggling.ToggleReleased());
            }
        }
    }
}