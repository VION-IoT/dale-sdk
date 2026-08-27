using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.SmokeHost.Contracts;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.SmokeHost.LogicBlocks
{
    /// <summary>
    ///     Consumes the synthetic struct contract <see cref="IGridDemand" /> — a third-party-shaped value
    ///     contract whose wire payload is a multi-field struct with a 1-level nested struct + an enum. The
    ///     <c>grid-demand</c> scenario drives it with <c>serviceProviderSet</c>; the block surfaces the fields
    ///     (including the nested ones) as service properties, asserted with <c>expect</c>. This is the
    ///     committed end-to-end proof of the RFC 0010 / DF-27 struct unblock through the real DevHost.
    ///     <para>
    ///         It also writes the <b>outbound</b> half, <see cref="IGridSetpoint" />: a timer mirrors the
    ///         received demand onto a multi-field setpoint command carrying a publish-time stamp. That is the
    ///         shape the four HAL outputs never had — their commands are single-field and round-trip as bare
    ///         scalars — so it is what <c>serviceProviderExpect</c>'s <c>field</c> is asserted against.
    ///     </para>
    /// </summary>
    [LogicBlock(Name = "Grid Demand", Icon = "flashlight-line")]
    public class GridBlock : LogicBlockBase
    {
        // The received scope as its enum, for the outbound command. Scope above is the string projection the
        // scenario asserts as a service property; the wire carries the enum and serializes it by name.
        private DemandScope _scope;

        [ServiceProviderContractBinding(DefaultName = "Demand", Multiplicity = LinkMultiplicity.ZeroOrOne)]
        public IGridDemand Demand { get; private set; }

        [ServiceProviderContractBinding(DefaultName = "Setpoint")]
        public IGridSetpoint Setpoint { get; private set; }

        [ServiceProperty(Title = "Demand valid")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public bool DemandValid { get; private set; }

        [ServiceProperty(Title = "Scope")]
        [Presentation(Group = PropertyGroup.Status)]
        public string Scope { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Active power", Unit = "W")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ActivePowerW { get; private set; }

        [ServiceProperty(Title = "Reactive power", Unit = "var")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ReactivePowerVar { get; private set; }

        public GridBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            // Mirror the received demand onto the outbound setpoint command, so the multi-field output is live
            // and assertable after a single `advance` — the same shape IoBlock uses for its HAL outputs.
            Setpoint.Set(DemandValid, _scope, new SetpointLimits(ActivePowerW, ReactivePowerVar));
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            Demand.DemandReceived += (_, demand) =>
                                     {
                                         DemandValid = demand.Valid;
                                         _scope = demand.Scope;
                                         Scope = demand.Scope.ToString();
                                         ActivePowerW = demand.Limits.ActivePowerW;
                                         ReactivePowerVar = demand.Limits.ReactivePowerVar;
                                     };
        }
    }
}