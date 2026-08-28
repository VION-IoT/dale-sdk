using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.SmokeHost.Contracts;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.SmokeHost.LogicBlocks
{
    /// <summary>
    ///     Consumes the <b>bidirectional</b> synthetic contract <see cref="IPlantControl" /> — one contract
    ///     identifier carrying both wire directions, the shape <see cref="GridBlock" /> splits across its two
    ///     contracts. A demand arrives on the contract (<c>serviceProviderSet</c>), the block surfaces its
    ///     fields as service properties, and a 1 s timer publishes the measurement back on the <b>same</b>
    ///     contract — a multi-field command a <c>serviceProviderExpect</c> asserts one scalar at a time with
    ///     <c>field</c>.
    ///     <para>
    ///         This is the fixture for VION-129: driving and asserting one contract in a single scenario. It
    ///         mirrors <c>IPowerPlantControlPv</c> in <c>logic-block-libraries</c> — the reporter's contract,
    ///         whose <c>serviceProviderExpect</c> the resolver refused as "an input" before any read.
    ///     </para>
    /// </summary>
    [LogicBlock(Name = "Plant Control", Icon = "sun-line")]
    public class PlantBlock : LogicBlockBase
    {
        // ZeroOrOne here is the CONSUMER side — this block binds at most one provider — and is unrelated to the
        // contract type's provider-side Consumers, which IPlantControl leaves at the default. Only the latter
        // reaches introspection as the `consumers` annotation the scenario direction gate reads, which is why
        // this contract still classifies as an input. Same split as GridBlock's Demand.
        [ServiceProviderContractBinding(DefaultName = "Control", Multiplicity = LinkMultiplicity.ZeroOrOne)]
        public IPlantControl Control { get; private set; }

        [ServiceProperty(Title = "Demand valid")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        public bool DemandValid { get; private set; }

        [ServiceProperty(Title = "Scope")]
        [Presentation(Group = PropertyGroup.Status)]
        public string Scope { get; private set; } = string.Empty;

        [ServiceProperty(Title = "Active power", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ActivePowerKw { get; private set; }

        [ServiceProperty(Title = "Reactive power", Unit = "kvar")]
        [Presentation(Group = PropertyGroup.Status)]
        public double ReactivePowerKvar { get; private set; }

        public PlantBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            // Publish the measurement back on the same contract the demand arrived on, so one `advance` makes
            // the outbound half live and assertable. With no valid demand the block reports zero rather than
            // withholding the write — the measurement is a heartbeat, unlike GridBlock's optional limits.
            Control.SetMeasurement(DemandValid, ActivePowerKw, ReactivePowerKvar);
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            Control.DemandReceived += (_, demand) =>
                                      {
                                          DemandValid = demand.Valid;
                                          Scope = demand.Scope.ToString();
                                          ActivePowerKw = demand.Supply.ActivePowerKw;
                                          ReactivePowerKvar = demand.Supply.ReactivePowerKvar;
                                      };
        }
    }
}