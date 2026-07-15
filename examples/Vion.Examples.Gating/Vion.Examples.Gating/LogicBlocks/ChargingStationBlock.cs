using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Examples.Gating.Contracts;

namespace Vion.Examples.Gating.LogicBlocks
{
    /// <summary>The station tier — an <c>[InstantiationParameter]</c> enum that gates optional features.</summary>
    public enum StationModel
    {
        Basic,

        Plus,

        Pro,
    }

    /// <summary>
    ///     One charge point. It is BOTH a service-bearing component (<c>[ServiceProperty]</c> +
    ///     <c>[ServiceMeasuringPoint]</c>) AND the <c>IChargePoint</c> interface endpoint a
    ///     <see cref="ChargeController" /> talks to — so gating the property that holds it removes the whole
    ///     component service <b>and</b> its inter-block interface binding together (RFC 0016).
    /// </summary>
    public class ChargePoint : IChargePoint
    {
        // The rated output a point draws while charging, in this demo.
        private const double RatedPowerKw = 11.0;

        [ServiceProperty(Title = "Charging")]
        [Presentation(Group = PropertyGroup.Status)]
        public bool Active { get; set; }

        [ServiceMeasuringPoint(Title = "Power", Unit = "kW")]
        public double Power { get; private set; }

        [ServiceProperty(Title = "Current limit", Unit = "A")]
        [Presentation(Group = PropertyGroup.Status)]
        public double CurrentLimit { get; private set; }

        // IChargePoint (receiver): a controller sets this point's current limit; we answer with our state.
        public ChargeLink.ChargeState HandleRequest(ChargeLink.SetCurrentLimit request)
        {
            CurrentLimit = request.Amps;
            return new ChargeLink.ChargeState(Active, request.Amps);
        }

        // Driven by the station's timer each tick: a charging point draws its rated power, an idle one draws none.
        internal void Sample()
        {
            Power = Active ? RatedPowerKw : 0.0;
        }
    }

    /// <summary>
    ///     Dynamic load management — a service-bearing component present only on richer station models.
    ///     Gated by an <c>[IncludedWhen]</c> <b>membership</b> predicate over the <c>Model</c> enum.
    /// </summary>
    public class LoadManager
    {
        [ServiceProperty(Title = "Max current", Unit = "A", Minimum = 6, Maximum = 32)]
        public double MaxCurrent { get; set; } = 16.0;
    }

    /// <summary>
    ///     RFC 0016 config-time structural gating showcase — a dashboard-UI test fixture that exercises
    ///     <b>every</b> gateable member kind, driven by a <b>number</b>, an <b>enum</b>, and a <b>string</b>
    ///     <c>[InstantiationParameter]</c>:
    ///     <list type="bullet">
    ///         <item>
    ///             <description>
    ///                 <c>ChargePointCount</c> (int) gates charge-point <b>components</b> (<c>Point2</c>/
    ///                 <c>Point3</c>), each also an <c>IChargePoint</c> <b>interface binding</b>, plus the bay-2 contactor
    ///                 <b>IO output</b>.
    ///             </description>
    ///         </item>
    ///         <item>
    ///             <description><c>Model</c> (enum) gates a <b>component</b> — load management — via a membership predicate.</description>
    ///         </item>
    ///         <item>
    ///             <description>
    ///                 <c>Region</c> (string) gates an <b>IO input</b> — the grid-frequency guard — via a string
    ///                 membership predicate.
    ///             </description>
    ///         </item>
    ///     </list>
    ///     A gated-out member does not exist: no service, no interface endpoint, no IO binding, no MQTT topic,
    ///     no persistence. Gated-out <c>[ServiceProviderContractBinding]</c> properties are <b>null</b> (the
    ///     binder constructs them), so fan-out code null-guards them.
    /// </summary>
    [LogicBlock(Name = "Charging Station", Icon = "charging-pile-line")]
    public class ChargingStationBlock : LogicBlockBase
    {
        private bool _gridFrequencyOk = true;

        // ── Instantiation parameters (chosen in the topology, applied before Configure, wire-read-only) ──

        [ServiceProperty(Title = "Charge points", Minimum = 1, Maximum = 3)]
        [InstantiationParameter]
        public int ChargePointCount { get; init; } = 1;

        [ServiceProperty(Title = "Model")]
        [InstantiationParameter]
        public StationModel Model { get; init; } = StationModel.Basic;

        // A non-null default is required: a gate over a null string parameter fails closed at bind.
        [ServiceProperty(Title = "Region")]
        [InstantiationParameter]
        public string Region { get; init; } = "EU";

        // ── Number-gated COMPONENTS that are ALSO IChargePoint interface endpoints ──
        // Point1 is ungated (always mappable); Point2/Point3 are gated, so a controller's mapping to them is
        // present only when the count includes them.

        [LogicBlockInterfaceBinding(typeof(IChargePoint), Multiplicity = LinkMultiplicity.ZeroOrOne)]
        public ChargePoint Point1 { get; } = new();

        [LogicBlockInterfaceBinding(typeof(IChargePoint), Multiplicity = LinkMultiplicity.ZeroOrOne)]
        [IncludedWhen("ChargePointCount >= 2")]
        public ChargePoint Point2 { get; } = new();

        [LogicBlockInterfaceBinding(typeof(IChargePoint), Multiplicity = LinkMultiplicity.ZeroOrOne)]
        [IncludedWhen("ChargePointCount >= 3")]
        public ChargePoint Point3 { get; } = new();

        // ── IO bindings ──
        // Always-included IO output (the station's main contactor) — the "included" IO baseline.
        [ServiceProviderContractBinding(DefaultName = "Main contactor")]
        public IDigitalOutput MainContactor { get; set; } = null!;

        // Number-gated IO output: bay 2's contactor. Null when excluded.
        [ServiceProviderContractBinding(DefaultName = "Bay 2 contactor")]
        [IncludedWhen("ChargePointCount >= 2")]
        public IDigitalOutput? Bay2Contactor { get; private set; }

        // String-gated IO input: a grid-frequency guard fitted only on EU/UK grids. Null when excluded.
        [ServiceProviderContractBinding(DefaultName = "Grid frequency guard")]
        [IncludedWhen("Region in ['EU', 'UK']")]
        public IDigitalInput? GridFrequencyGuard { get; private set; }

        // ── Enum-gated COMPONENT ──
        [IncludedWhen("Model in ['Plus', 'Pro']")]
        public LoadManager LoadManagement { get; } = new();

        public ChargingStationBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            Point1.Sample();
            Point2.Sample();
            Point3.Sample();

            // The main contactor closes when any bay is charging; the bay-2 contactor mirrors bay 2. Driving a
            // gated-out (null) contract would throw — the null-conditional is what keeps that safe.
            MainContactor.Set(_gridFrequencyOk && (Point1.Active || Point2.Active || Point3.Active));
            Bay2Contactor?.Set(Point2.Active);
        }

        /// <inheritdoc />
        protected override void Ready()
        {
            // The grid-frequency guard input is only present in EU/UK; null-guard the gated-out case.
            if (GridFrequencyGuard != null)
            {
                GridFrequencyGuard.InputChanged += (_, ok) => _gridFrequencyOk = ok;
            }
        }
    }
}