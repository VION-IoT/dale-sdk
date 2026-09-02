using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;

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

    /// <summary>The contract behind <see cref="GatedInterfaceOnlyProbe" />, so a gated interface binding has a wire.</summary>
    [LogicBlockContract(BetweenInterface = "IGatedProbeSource", AndInterface = "IGatedProbeSink", Direction = ContractDirection.Bidirectional)]
    public static class GatedProbeLink
    {
        [RequestResponse(From = "IGatedProbeSource", To = "IGatedProbeSink", ResponseType = typeof(Ack))]
        public readonly record struct Poll(int N);

        public readonly record struct Ack(int N);
    }

    /// <summary>
    ///     A component bound only through its interface: it declares no service member, so it is absent from
    ///     the service-binding keys and only the gate can decide whether its state belongs to the instance.
    /// </summary>
    public sealed class GatedInterfaceOnlyProbe : IGatedProbeSink
    {
        [Persistent]
        public double Energy { get; set; }

        public GatedProbeLink.Ack HandleRequest(GatedProbeLink.Poll request)
        {
            return new GatedProbeLink.Ack(request.N);
        }
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

    // Every gate below is one DALE043 rejects — an empty predicate does not parse. The declarations are
    // deliberate: they are how the suite reaches the three sites that record a member's gate, which must
    // agree with the binder about what counts as one whatever the predicate's text.
#pragma warning disable DALE043

    /// <summary>A gate declared as the empty string on each of the three member kinds that record one.</summary>
    public sealed class EmptyGateBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [IncludedWhen("")]
        public GatedPoint Point2 { get; } = new();

        [ServiceProviderContractBinding(DefaultName = "Ladepunkt 2 aktiv")]
        [IncludedWhen("")]
        public IDigitalOutput? Point2Output { get; private set; }

        [LogicBlockInterfaceBinding(typeof(IGatedProbeSink))]
        [IncludedWhen("")]
        public GatedInterfaceOnlyProbe Probe { get; } = new();

        public EmptyGateBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
#pragma warning restore DALE043
}
