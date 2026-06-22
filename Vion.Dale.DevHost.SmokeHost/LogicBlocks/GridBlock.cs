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
    /// </summary>
    [LogicBlock(Name = "Grid Demand", Icon = "flashlight-line")]
    public class GridBlock : LogicBlockBase
    {
        [ServiceProviderContractBinding(DefaultName = "Demand", Multiplicity = LinkMultiplicity.ZeroOrOne)]
        public IGridDemand Demand { get; private set; }

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

        /// <inheritdoc />
        protected override void Ready()
        {
            Demand.DemandReceived += (_, demand) =>
                                     {
                                         DemandValid = demand.Valid;
                                         Scope = demand.Scope.ToString();
                                         ActivePowerW = demand.Limits.ActivePowerW;
                                         ReactivePowerVar = demand.Limits.ReactivePowerVar;
                                     };
        }
    }
}