using System;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Utils;

// Logic-block fixtures for the config-gating suite (docs/specs/config-gating.md). Shared across the
// parameter, gate, binding, routing, persistence and metadata classes so one shape is declared once; a
// fixture used by a single class stays private to it.

namespace Vion.Dale.Sdk.Test.TestHelpers
{
    /// <summary>The station tier an enum parameter selects, and the gates compare against by member name.</summary>
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

        /// <summary>Records the last poll that reached this handler — how a test sees a message arrive at all.</summary>
        public int LastPoll { get; private set; }

        public GatedProbeLink.Ack HandleRequest(GatedProbeLink.Poll request)
        {
            LastPoll = request.N;
            return new GatedProbeLink.Ack(request.N);
        }
    }

    /// <summary>The sending half of the probe contract — the side the generator gives an extension class.</summary>
    public sealed class GatedProbeSource : IGatedProbeSource
    {
        public void HandleResponse(InterfaceId functionId, GatedProbeLink.Ack response)
        {
        }
    }

    /// <summary>A block whose own <c>Ready()</c> throws — configuration failing past the binders.</summary>
    public sealed class ReadyThrowsBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        public GatedPoint Point1 { get; } = new();

        public ReadyThrowsBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            throw new InvalidOperationException("the block refused to become ready");
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

    /// <summary>
    ///     An enum parameter gating a component by membership — the shape that mis-resolves when the
    ///     evaluation context is built with an int cast or a raw <c>ToString</c> instead of the shared codec.
    /// </summary>
    public sealed class GatedEnumBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Modell")]
        [InstantiationParameter]
        public StationModel Model { get; init; } = StationModel.Bricco;

        public GatedPoint Point1 { get; } = new();

        [IncludedWhen("Model in ['Moka', 'Ristretto', 'Cappuccino']")]
        public GatedPoint Point2 { get; } = new();

        public GatedEnumBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>One parameter of each type the declaration set allows, for the decode and default rows.</summary>
    public sealed class ParameterTypesBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 3)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [ServiceProperty(Title = "Modell")]
        [InstantiationParameter]
        public StationModel Model { get; init; } = StationModel.Bricco;

        [ServiceProperty(Title = "Region")]
        [InstantiationParameter]
        public string? Region { get; init; } = "EU";

        [ServiceProperty(Title = "Reserve")]
        [InstantiationParameter]
        public int? Reserve { get; init; }

        // A plain public setter, the accessor shape the analyzer accepts beside init.
        [ServiceProperty(Title = "Ausbaustufe")]
        [InstantiationParameter]
        public int Stage { get; set; } = 1;

        public ParameterTypesBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>A parameter and a gate declared once on a shared base, resolved on every leaf.</summary>
    public abstract class BaseStationBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [IncludedWhen("PointCount >= 2")]
        public GatedPoint Point2 { get; } = new();

        protected BaseStationBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    public sealed class LeafStationBlock : BaseStationBlock
    {
        public GatedPoint Point1 { get; } = new();
    }

    /// <summary>A gated contract binding: excluded means the binder never constructs it, so the property is null.</summary>
    public sealed class GatedContractBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 2)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [ServiceProviderContractBinding(DefaultName = "Ladepunkt 2 aktiv")]
        [IncludedWhen("PointCount >= 2")]
        public IDigitalOutput? Point2Output { get; private set; }

        public GatedContractBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     A gated interface binding beside a class-implemented one: the property binding follows the gate,
    ///     the class-level binding never does.
    /// </summary>
    public sealed class GatedInterfaceBlock : LogicBlockBase, IGatedProbeSink
    {
        [ServiceProperty(Title = "Anzahl", Minimum = 1, Maximum = 2)]
        [InstantiationParameter]
        public int Count { get; init; } = 1;

        [LogicBlockInterfaceBinding(typeof(IGatedProbeSink))]
        [IncludedWhen("Count >= 2")]
        public GatedInterfaceOnlyProbe Probe { get; } = new();

        [IncludedWhen("Count >= 2")]
        public GatedPoint Point2 { get; } = new();

        /// <summary>Records the last poll that reached the block's own class-implemented endpoint.</summary>
        public int LastPoll { get; private set; }

        public GatedInterfaceBlock() : base(NullLogger.Instance)
        {
        }

        public GatedProbeLink.Ack HandleRequest(GatedProbeLink.Poll request)
        {
            LastPoll = request.N;
            return new GatedProbeLink.Ack(request.N);
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>A gated component the author leaves null, so the definition view has nothing to describe.</summary>
    public sealed class NullComponentBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        public GatedPoint Point1 { get; } = new();

        [IncludedWhen("PointCount >= 2")]
        public GatedPoint? Point2 { get; set; }

        public NullComponentBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     A gated interface binding the author leaves null. Unlike a service-bearing component, an endpoint's
    ///     identity is type-level — the property name and the interface — so the definition view can describe
    ///     it with nothing behind it.
    /// </summary>
    public sealed class NullInterfaceComponentBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Anzahl")]
        [InstantiationParameter]
        public int Count { get; init; } = 1;

        // The SENDER side deliberately: the generator emits an extension class only for the side that sends,
        // and that class is what the interface factory hands the implementation to. A sink-side binding never
        // reaches it, so it cannot show what a null instance does.
        [LogicBlockInterfaceBinding(typeof(IGatedProbeSource))]
        [IncludedWhen("Count >= 2")]
        public GatedProbeSource? Probe { get; set; }

        public NullInterfaceComponentBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     A gate that is a bare boolean reference — the one predicate shape carrying no comparison, and so
    ///     the only one whose tree is a lone reference node.
    /// </summary>
    public sealed class GatedBoolParameterBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Freigabe")]
        [InstantiationParameter]
        public bool Enabled { get; init; }

        [IncludedWhen("Enabled")]
        public GatedPoint Point2 { get; } = new();

        public GatedBoolParameterBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>A gate over a parameter whose declared default is null — the fail-closed edge.</summary>
    public sealed class GatedNullParameterBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Region")]
        [InstantiationParameter]
        public string? Region { get; init; }

        [IncludedWhen("Region == 'EU'")]
        public GatedPoint Point2 { get; } = new();

        public GatedNullParameterBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    // Every gate below is one DALE043 rejects — an empty predicate does not parse, a malformed one does not
    // parse, and a reference that is not an [InstantiationParameter] does not resolve. The declarations are
    // deliberate: they are how the suite reaches what the binder and the definition view do with a gate the
    // author shipped anyway, past a suppressed or absent analyzer.
#pragma warning disable DALE043

    /// <summary>
    ///     A parameter an author also marked <c>[Persistent]</c> — the combination DALE044 refuses, declared
    ///     here so the suite can reach what persistence does with one that shipped anyway.
    /// </summary>
#pragma warning disable DALE044
    public sealed class PersistedParameterBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        [Persistent]
        public int PointCount { get; init; } = 1;

        public GatedPoint Point1 { get; } = new();

        [IncludedWhen("PointCount >= 2")]
        public GatedPoint Point2 { get; } = new();

        public PersistedParameterBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
#pragma warning restore DALE044

    // One empty gate per fixture, one member kind each: the binders run interface, then contract, then
    // service, so a block carrying all three would only ever show the first site's refusal.

    /// <summary>An empty gate on a component service — the site DeclarativeServiceBinder records.</summary>
    public sealed class EmptyGateComponentBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [IncludedWhen("")]
        public GatedPoint Point2 { get; } = new();

        public EmptyGateComponentBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>An empty gate on a contract binding — the site ContractMetaData records.</summary>
    public sealed class EmptyGateContractBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [ServiceProviderContractBinding(DefaultName = "Ladepunkt 2 aktiv")]
        [IncludedWhen("")]
        public IDigitalOutput? Point2Output { get; private set; }

        public EmptyGateContractBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>An empty gate on an interface binding — the site FunctionInterfaceMetaData records.</summary>
    public sealed class EmptyGateInterfaceBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [LogicBlockInterfaceBinding(typeof(IGatedProbeSink))]
        [IncludedWhen("")]
        public GatedInterfaceOnlyProbe Probe { get; } = new();

        public EmptyGateInterfaceBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>A gate whose predicate is outside the grammar.</summary>
    public sealed class UnparseableGateBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [IncludedWhen("PointCount >>> 2")]
        public GatedPoint Point2 { get; } = new();

        public UnparseableGateBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>A gate referencing a name no parameter carries.</summary>
    public sealed class UnknownReferenceGateBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [IncludedWhen("Missing >= 2")]
        public GatedPoint Point2 { get; } = new();

        public UnknownReferenceGateBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>A gate referencing an ordinary service property rather than an instantiation parameter.</summary>
    public sealed class NonParameterReferenceGateBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Ladepunkte")]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        [ServiceProperty(Title = "Sollwert")]
        public int Setting { get; set; } = 5;

        [IncludedWhen("Setting >= 2")]
        public GatedPoint Point2 { get; } = new();

        public NonParameterReferenceGateBlock() : base(NullLogger.Instance)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
#pragma warning restore DALE043
}