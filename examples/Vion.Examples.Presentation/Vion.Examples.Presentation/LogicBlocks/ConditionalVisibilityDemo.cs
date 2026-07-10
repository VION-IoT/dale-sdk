using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.LogicBlocks
{
    /// <summary>
    ///     Showcases conditional visibility (RFC 0017 — <c>[Presentation(VisibleWhen = "…")]</c>) well
    ///     beyond the trivial bool case. One dependent field per predicate shape:
    ///     <list type="bullet">
    ///         <item>
    ///             enum equality / inequality / membership (<c>Mode == 'Eco'</c>, <c>Mode != 'Off'</c>,
    ///             <c>Mode in ['Eco', 'Boost']</c>);
    ///         </item>
    ///         <item>integer relational and membership (<c>PhaseCount &gt;= 3</c>, <c>PhaseCount in [1, 3]</c>);</item>
    ///         <item>
    ///             boolean combinators with parentheses (<c>DirectMeasurement == false &amp;&amp; Mode != 'Off'</c>,
    ///             <c>!(ShowAdvanced || DirectMeasurement)</c>) and a bare bool ref (<c>ShowAdvanced</c>);
    ///         </item>
    ///         <item>a <b>measuring point</b> that hides (<c>Mode == 'Eco'</c>);</item>
    ///         <item>
    ///             both qualified addressing forms — a <b>sibling component service</b>
    ///             (<c>Downstream.Enabled</c>) and the <b>root service by the block's class name</b>
    ///             (<c>ConditionalVisibilityDemo.Mode != 'Off'</c>, authored inside the component).
    ///         </item>
    ///     </list>
    ///     The referenced fields keep existing and functioning; only their editor rows hide. Toggle the
    ///     drivers (Mode, DirectMeasurement, PhaseCount, ShowAdvanced, Downstream.Enabled) to watch the
    ///     dependent rows appear/disappear live. The predicates are validated at build time by the SDK
    ///     analyzers (DALE041/DALE042) — e.g. a typo like <c>Mode == 'Ecoo'</c> would fail the build.
    /// </summary>
    [LogicBlock(Name = "Bedingte Sichtbarkeit", Icon = "eye-line", Groups = new[] { PropertyGroup.Configuration, PropertyGroup.Metric })]
    public class ConditionalVisibilityDemo : LogicBlockBase
    {
        // ── Drivers — the sibling properties the predicates below reference ──────────
        [ServiceProperty(Title = "Betriebsmodus", Description = "Steers the mode-dependent fields.")]
        [Presentation(Group = PropertyGroup.Configuration, StatusIndicator = true, Order = 0)]
        public MeterMode Mode { get; set; } = MeterMode.Standard;

        [ServiceProperty(Title = "Direkte Messung (ohne Stromwandler)", Description = "When on, the CT-ratio input hides.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 1)]
        public bool DirectMeasurement { get; set; }

        [ServiceProperty(Title = "Phasenzahl", Description = "1 or 3 — steers the per-phase fields.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 2)]
        public int PhaseCount { get; set; } = 3;

        [ServiceProperty(Title = "Erweitert anzeigen", Description = "Reveals the advanced-only fields.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 3)]
        public bool ShowAdvanced { get; set; }

        // ── enum equality — only for the matching mode ──────────────────────────────
        [ServiceProperty(Title = "Eco-Zielleistung", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 10, VisibleWhen = "Mode == 'Eco'")]
        public double EcoTargetPower { get; set; } = 5;

        [ServiceProperty(Title = "Boost-Dauer", Unit = "s")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 11, VisibleWhen = "Mode == 'Boost'")]
        public int BoostDurationSeconds { get; set; } = 30;

        // ── enum inequality — hidden only when Off ──────────────────────────────────
        [ServiceProperty(Title = "Leistungsgrenze", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 12, VisibleWhen = "Mode != 'Off'")]
        public double PowerLimit { get; set; } = 11;

        // ── enum membership ─────────────────────────────────────────────────────────
        [ServiceProperty(Title = "Rampenrate", Unit = "kW/s")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 13, VisibleWhen = "Mode in ['Eco', 'Boost']")]
        public double RampRate { get; set; } = 0.5;

        // ── integer relational — 3-phase only ───────────────────────────────────────
        [ServiceProperty(Title = "L3-Spannung", Unit = "V")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 14, VisibleWhen = "PhaseCount >= 3")]
        public double L3Voltage { get; set; } = 230;

        // ── conjunction — CT ratio only when NOT direct AND the meter is on ─────────
        [ServiceProperty(Title = "Primärstrom (CT)", Unit = "A", Minimum = 1, Maximum = 5000)]
        [Presentation(Group = PropertyGroup.Configuration, Order = 15, VisibleWhen = "DirectMeasurement == false && Mode != 'Off'")]
        public double CtPrimaryCurrent { get; set; } = 100;

        // ── integer membership ──────────────────────────────────────────────────────
        [ServiceProperty(Title = "Neutralleiter-Kompensation")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 16, VisibleWhen = "PhaseCount in [1, 3]")]
        public bool NeutralCompensation { get; set; }

        // ── negation + disjunction ──────────────────────────────────────────────────
        [ServiceProperty(Title = "Einfache Anzeige")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 17, VisibleWhen = "!(ShowAdvanced || DirectMeasurement)")]
        public bool SimpleReadout { get; set; } = true;

        // ── bare bool ref (on a string-typed member — the annotated type is unrestricted) ──
        [ServiceProperty(Title = "Erweiterte Diagnose")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 18, VisibleWhen = "ShowAdvanced")]
        public string AdvancedDiagnostics { get; set; } = "n/a";

        // ── qualified ref to a SIBLING component service ────────────────────────────
        [ServiceProperty(Title = "Downstream-Sollwert", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 19, VisibleWhen = "Downstream.Enabled")]
        public double DownstreamSetpoint { get; set; } = 3;

        // ── a measuring point that hides (dual-doc emission handles both) ───────────
        [ServiceMeasuringPoint(Title = "Eco-Einsparung", Unit = "kWh", Kind = MeasuringPointKind.TotalIncreasing)]
        [Presentation(Group = PropertyGroup.Metric, VisibleWhen = "Mode == 'Eco'")]
        public double EcoEnergySaved { get; private set; }

        // ── the sibling component service itself (identifier = this property's name) ─
        public DownstreamMeter Downstream { get; } = new();

        public ConditionalVisibilityDemo(ILogger logger) : base(logger)
        {
        }

        // Accrues the Eco saving so the (conditionally-visible) measuring point is live in Eco mode.
        [Timer(2)]
        public void OnTimer()
        {
            if (Mode == MeterMode.Eco)
            {
                EcoEnergySaved += EcoTargetPower * 2 / 3600;
            }
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    /// <summary>
    ///     A sibling component service (its identifier is the holding property's name, <c>Downstream</c>).
    ///     Demonstrates qualified addressing in both directions: a bare ref against its own service, and a
    ///     ref to the root service by the block's class name.
    /// </summary>
    public class DownstreamMeter
    {
        [ServiceProperty(Title = "Aktiviert")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public bool Enabled { get; set; }

        // Bare ref — resolves against THIS (own) component service.
        [ServiceProperty(Title = "Downstream-Grenze", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration, VisibleWhen = "Enabled")]
        public double DownstreamLimit { get; set; } = 7;

        // Qualified ref to the ROOT service, addressed by the logic-block class name.
        [ServiceProperty(Title = "Synchronisierte Grenze", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration, VisibleWhen = "ConditionalVisibilityDemo.Mode != 'Off'")]
        public double SyncedLimit { get; set; } = 11;
    }

    public enum MeterMode
    {
        [EnumLabel("Aus")]
        [Severity(StatusSeverity.Neutral)]
        Off,

        [EnumLabel("Standard")]
        [Severity(StatusSeverity.Success)]
        Standard,

        [EnumLabel("Eco")]
        [Severity(StatusSeverity.Success)]
        Eco,

        [EnumLabel("Boost")]
        [Severity(StatusSeverity.Warning)]
        Boost,
    }
}