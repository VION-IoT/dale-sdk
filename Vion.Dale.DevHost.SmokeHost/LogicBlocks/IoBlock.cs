using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Dale.DevHost.SmokeHost.LogicBlocks
{
    /// <summary>
    ///     Exercises the HAL surface: a digital + analog input and a digital + analog output, bound as
    ///     service-provider contracts. The DevHost auto-mocks unmapped contracts, so the inputs are
    ///     drivable from the UI / <c>POST /api/hal/...</c> and the outputs are observable. A 1 s timer
    ///     mirrors the mocked inputs onto the outputs so the whole HAL path is live and steppable.
    /// </summary>
    [LogicBlock(Name = "IO Device", Icon = "plug-line")]
    public class IoBlock : LogicBlockBase
    {
        [ServiceProviderContractBinding(DefaultName = "Freigabe", Multiplicity = LinkMultiplicity.ZeroOrOne)]
        public IDigitalInput EnableInput { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Pegel", Multiplicity = LinkMultiplicity.ZeroOrOne)]
        public IAnalogInput LevelInput { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Aktiv")]
        public IDigitalOutput ActiveOutput { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Echo")]
        public IAnalogOutput EchoOutput { get; private set; }

        [ServiceProperty(Title = "Freigegeben")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public bool IsEnabled { get; private set; }

        [ServiceProperty(Title = "Aktueller Pegel", Unit = "V")]
        [Presentation(Group = PropertyGroup.Status)]
        public double CurrentLevel { get; private set; }

        public IoBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            // Mirror the mocked inputs onto the outputs — the HAL path is live and observable end to end.
            ActiveOutput.Set(IsEnabled);
            EchoOutput.Set(CurrentLevel);
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            EnableInput.InputChanged += (_, value) => IsEnabled = value;
            LevelInput.InputChanged += (_, value) => CurrentLevel = value;
        }
    }
}