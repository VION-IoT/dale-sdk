using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     A catalog fixture with an <c>[InstantiationParameter]</c> and a <c>[IncludedWhen]</c>-gated contract
    ///     binding (over the SmokeHost's <c>IGridDemand</c>), so the RFC 0016 catalog projection — parameter
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

        public GatedCatalogFixture(ILogger logger) : base(logger)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
}