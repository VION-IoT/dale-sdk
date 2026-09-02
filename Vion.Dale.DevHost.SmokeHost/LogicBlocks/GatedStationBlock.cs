using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.SmokeHost.LogicBlocks
{
    /// <summary>
    ///     One charge point — a service-bearing component (its own <c>[ServiceProperty]</c> + measuring
    ///     point) whose whole existence is gated by the station's <c>PointCount</c> parameter. Gating the
    ///     property that holds it gates the entire component service.
    /// </summary>
    public class ChargePoint
    {
        [ServiceProperty(Title = "Aktiv")]
        [Presentation(Group = PropertyGroup.Status)]
        public bool Active { get; set; }

        [ServiceMeasuringPoint(Title = "Leistung", Unit = "kW")]
        public double Power { get; private set; }
    }

    /// <summary>
    ///     A signal endpoint the station holds in a get-only property under an explicit binding identifier —
    ///     the two things the topology editor's catalog has to get right about a gated interface binding:
    ///     that it is listed at all (the binder never asks for a setter), and that it is listed under the
    ///     name the binder will answer to.
    /// </summary>
    public class GatedSignalPoint : ISignalSink
    {
        public SignalLink.Ack HandleRequest(SignalLink.Ping request)
        {
            return new SignalLink.Ack(request.Sequence);
        }
    }

    /// <summary>
    ///     Config-time structural gating showcase. The block declares a <b>static maximum</b> of
    ///     three charge points; the operator-chosen <c>[InstantiationParameter] PointCount</c> decides how
    ///     many are part of the configured instance. A topology sets <c>PointCount</c> (applied before
    ///     <c>Configure</c>), so the DevHost shows <b>exactly</b> that many point services — <c>Point1</c>
    ///     always, <c>Point2</c> only when <c>PointCount &gt;= 2</c>, <c>Point3</c> only when
    ///     <c>PointCount &gt;= 3</c> — plus the root service carrying the (read-only) parameter.
    /// </summary>
    [LogicBlock(Name = "Gated Charging Station", Icon = "charging-pile-line")]
    public class GatedStationBlock : LogicBlockBase
    {
        // Chosen by the operator in the topology; applied before Configure, wire-read-only, never set at
        // runtime. Its C# initializer default (1) is only seen by the constructor.
        [ServiceProperty(Title = "Ladepunkte", Minimum = 1, Maximum = 3)]
        [InstantiationParameter]
        public int PointCount { get; init; } = 1;

        // Nullable with no declared default: a gate over it can only resolve once the operator supplies a
        // value, which is what makes "the default is known, and it is null" worth telling an editor apart
        // from "the default was never read".
        [ServiceProperty(Title = "Reserveleistung")]
        [InstantiationParameter]
        public int? Reserve { get; init; }

        public ChargePoint Point1 { get; } = new();

        [IncludedWhen("PointCount >= 2")]
        public ChargePoint Point2 { get; } = new();

        [IncludedWhen("PointCount >= 3")]
        public ChargePoint Point3 { get; } = new();

        [LogicBlockInterfaceBinding(typeof(ISignalSink), Identifier = "Point2Signal", Multiplicity = LinkMultiplicity.ZeroOrOne)]
        [IncludedWhen("PointCount >= 2")]
        public GatedSignalPoint Point2Endpoint { get; } = new();

        // Gated on the nullable parameter: with no value chosen the gate cannot resolve and the block fails
        // to start, which is why the editor warns about a known-null default rather than failing open on it.
        [LogicBlockInterfaceBinding(typeof(ISignalSink), Identifier = "ReserveSignal", Multiplicity = LinkMultiplicity.ZeroOrOne)]
        [IncludedWhen("Reserve >= 1")]
        public GatedSignalPoint ReserveEndpoint { get; } = new();

        public GatedStationBlock(ILogger logger) : base(logger)
        {
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }
}