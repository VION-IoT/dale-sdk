using System;
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
    ///     <para>
    ///         It is also the fixture's <b>output-confirmation</b> consumer (VION-131): both outputs subscribe
    ///         <c>OutputChanged</c> — the confirmation a service provider sends back — and surface what arrived
    ///         as service properties, alongside a flag comparing it with what was last commanded. Off
    ///         production that event only fires when something drives the output contract's declared inbound,
    ///         so the mismatch flag is reachable exactly because a scenario can confirm a value the block never
    ///         asked for.
    ///     </para>
    /// </summary>
    [LogicBlock(Name = "IO Device", Icon = "plug-line")]
    public class IoBlock : LogicBlockBase
    {
        // What the block last asked the outputs for — the half a confirmation is compared against. A field,
        // not a service property: the commanded value is already observable as the captured output command.
        private bool _commandedActive;

        private double _commandedEcho;

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

        [ServiceProperty(Title = "Aktiv bestätigt")]
        [Presentation(Group = PropertyGroup.Status)]
        public bool ConfirmedActive { get; private set; }

        [ServiceProperty(Title = "Echo bestätigt", Unit = "V")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ConfirmedEcho { get; private set; }

        /// <summary>Whether the LAST confirmation to arrive disagreed with what that output was commanded.</summary>
        [ServiceProperty(Title = "Bestätigung weicht ab")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public bool ConfirmationMismatch { get; private set; }

        public IoBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            // Mirror the mocked inputs onto the outputs — the HAL path is live and observable end to end.
            _commandedActive = IsEnabled;
            _commandedEcho = CurrentLevel;
            ActiveOutput.Set(IsEnabled);
            EchoOutput.Set(CurrentLevel);
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            EnableInput.InputChanged += (_, value) => IsEnabled = value;
            LevelInput.InputChanged += (_, value) => CurrentLevel = value;

            // The confirmation half: what the service provider says it applied. Each confirmation is judged
            // against ITS OWN family's last command, so the flag reads "the last confirmation disagreed" and
            // never trips because the other family has not confirmed yet.
            ActiveOutput.OutputChanged += (_, value) =>
                                          {
                                              ConfirmedActive = value;
                                              ConfirmationMismatch = value != _commandedActive;
                                          };
            EchoOutput.OutputChanged += (_, value) =>
                                        {
                                            ConfirmedEcho = value;
                                            ConfirmationMismatch = Math.Abs(value - _commandedEcho) > 0.0001;
                                        };
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
            ConfirmedActive = false;
            ConfirmedEcho = 0;
            ConfirmationMismatch = false;
        }
    }
}