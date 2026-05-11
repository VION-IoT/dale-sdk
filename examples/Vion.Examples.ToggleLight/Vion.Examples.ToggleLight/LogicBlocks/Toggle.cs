using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Microsoft.Extensions.Logging;
using Vion.Examples.ToggleLight.Contracts;

namespace Vion.Examples.ToggleLight.LogicBlocks
{
    [LogicBlockInfo("Taster", "toggle-line")]
    public class Toggle : LogicBlockBase, IToggler
    {
        public enum SignalMode
        {
            [EnumValueInfo("Normal")]
            Normal,

            [EnumValueInfo("Invertiert")]
            Inverted,
        }

        private readonly ILogger _logger;

        private bool _lastValue;

        [ServiceProperty(Title = "Signalmodus")]
        [Category(PropertyCategory.Configuration)]
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