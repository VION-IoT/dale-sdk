using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.TestHelpers
{
    /// <summary>
    ///     Logic-block fixtures for the config-gating suite (<c>docs/specs/config-gating.md</c>). Shared
    ///     across the parameter, gate, binding, routing, persistence and metadata classes so one shape is
    ///     declared once; a fixture used by a single class stays private to it.
    /// </summary>
    public enum StationModel
    {
        Bricco,

        Moka,

        Ristretto,

        Cappuccino,
    }

    /// <summary>A service-bearing component: one service property, one measuring point, one persisted member.</summary>
    public sealed class GatedPoint
    {
        [ServiceProperty(Title = "Aktiv")]
        public bool Active { get; set; }

        [ServiceMeasuringPoint(Unit = "kW")]
        public double Power { get; private set; }

        [Persistent]
        public double Energy { get; set; }
    }

    /// <summary>An integer parameter gating two component services — the canonical station shape.</summary>
    public sealed class GatedCountBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 3)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        public GatedPoint Point1 { get; } = new();

        [IncludedWhen("PointCount >= 2")]
        public GatedPoint Point2 { get; } = new();

        [IncludedWhen("PointCount >= 3")]
        public GatedPoint Point3 { get; } = new();

        public GatedCountBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
}
