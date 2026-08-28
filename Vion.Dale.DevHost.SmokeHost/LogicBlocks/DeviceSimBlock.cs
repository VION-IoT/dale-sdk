using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Dale.DevHost.SmokeHost.LogicBlocks
{
    /// <summary>
    ///     The fixture's <b>provider-face</b> consumer (VION-131): a simulator standing in for a contactor and
    ///     its auxiliary feedback contact. It binds the inverted faces — an <see cref="IDigitalOutputProvider" />
    ///     that receives what a digital output commanded and confirms back what the equipment applied, and an
    ///     <see cref="IDigitalInputProvider" /> that drives the value a digital input would read.
    ///     <para>
    ///         Nothing is wired to it. Both faces are auto-mocked like any other contract, so a scenario drives
    ///         the command in with <c>serviceProviderSet</c> and asserts the confirmation and the driven contact
    ///         with <c>serviceProviderExpect</c> — the trios proven end to end before anything pairs them to a
    ///         real consumer. <see cref="TakesUpCommands" /> is the behaviour knob: switched off, the simulator
    ///         receives the command and deliberately ignores it, which is how hardware that did not take up a
    ///         command is modelled — as block behaviour, not as host magic.
    ///     </para>
    /// </summary>
    [LogicBlock(Name = "Device Sim", Icon = "cpu-line")]
    public class DeviceSimBlock : LogicBlockBase
    {
        [ServiceProviderContractBinding(DefaultName = "Schütz")]
        public IDigitalOutputProvider ContactorProvider { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Rückmeldung")]
        public IDigitalInputProvider FeedbackProvider { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Stellwert")]
        public IAnalogOutputProvider SetpointProvider { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Istwert")]
        public IAnalogInputProvider ActualValueProvider { get; private set; }

        /// <summary>Whether the simulated equipment acts on a command — switch it off to model non-take-up.</summary>
        [ServiceProperty(Title = "Nimmt Befehle an")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public bool TakesUpCommands { get; set; } = true;

        [ServiceProperty(Title = "Zuletzt befohlen")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public bool LastCommand { get; private set; }

        [ServiceProperty(Title = "Angewendet")]
        [Presentation(Group = PropertyGroup.Status)]
        public bool Applied { get; private set; }

        [ServiceProperty(Title = "Stellwert angewendet", Unit = "V")]
        [Presentation(Group = PropertyGroup.Status)]
        public double AppliedSetpoint { get; private set; }

        [ServiceProperty(Title = "Bestätigungen")]
        [Presentation(Group = PropertyGroup.Metric)]
        public long ConfirmationCount { get; private set; }

        public DeviceSimBlock(ILogger logger) : base(logger)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            // The whole simulator: receive the command, decide what the equipment does, say so on both faces.
            ContactorProvider.SetReceived += (_, value) =>
                                             {
                                                 LastCommand = value;
                                                 if (!TakesUpCommands)
                                                 {
                                                     return;
                                                 }

                                                 Applied = value;
                                                 ConfirmationCount++;
                                                 ContactorProvider.Confirm(value);
                                                 FeedbackProvider.Drive(value);
                                             };

            // The analog pair, same shape — the four provider faces are one family, not two special cases.
            SetpointProvider.SetReceived += (_, value) =>
                                            {
                                                if (!TakesUpCommands)
                                                {
                                                    return;
                                                }

                                                AppliedSetpoint = value;
                                                SetpointProvider.Confirm(value);
                                                ActualValueProvider.Drive(value);
                                            };
        }
    }
}