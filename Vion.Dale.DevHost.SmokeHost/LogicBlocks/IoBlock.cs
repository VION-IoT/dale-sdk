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

        /// <inheritdoc />
        protected override void Stopping()
        {
            // The fixture's coverage of the stop hook: DevHost teardown (and therefore host recycle) sends the
            // domain stop, so this runs and the de-energised state is what the UI shows last before the
            // generation is replaced — the drained final values, not the ones from before the shutdown.
            // Deliberately service properties rather than a contract write: a best-effort HAL write from
            // Stopping() needs the grace period VION-65 is about, and would be an unreliable smoke assertion.
            IsEnabled = false;
            CurrentLevel = 0;
        }
    }
}