using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>An interface endpoint a catalog block holds in a get-only property.</summary>
    public sealed class GatedSignalSink : SmokeHost.LogicBlocks.ISignalSink
    {
        public SmokeHost.LogicBlocks.SignalLink.Ack HandleRequest(SmokeHost.LogicBlocks.SignalLink.Ping request)
        {
            return new SmokeHost.LogicBlocks.SignalLink.Ack(request.Sequence);
        }
    }

    /// <summary>
    ///     A catalog fixture with an <c>[InstantiationParameter]</c> and a <c>[IncludedWhen]</c>-gated contract
    ///     binding (over the SmokeHost's <c>IGridDemand</c>), so the catalog projection — parameter
    ///     schemas + per-member gate predicates on <see cref="Topologies.LogicBlockDefinition" /> — has something
    ///     to exercise by reflection alone.
    /// </summary>
    public sealed class GatedCatalogFixture : LogicBlockBase
    {
        [ServiceProperty(Title = "Count", Minimum = 1, Maximum = 2)]
        [InstantiationParameter]
        public int Count { get; init; } = 1;

        [ServiceProviderContractBinding(DefaultName = "Demand")]
        [IncludedWhen("Count >= 2")]
        public SmokeHost.Contracts.IGridDemand? Demand { get; private set; }

        // Get-only and initialized in place — the shape every in-repo block writes for a component that is
        // also an interface endpoint, and the one the catalog used to drop for having no setter.
        [LogicBlockInterfaceBinding(typeof(SmokeHost.LogicBlocks.ISignalSink))]
        [IncludedWhen("Count >= 2")]
        public GatedSignalSink Sink { get; } = new();

        public GatedCatalogFixture(ILogger logger) : base(logger)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
}