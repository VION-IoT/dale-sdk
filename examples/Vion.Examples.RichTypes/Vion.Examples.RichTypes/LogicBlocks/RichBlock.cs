using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.RichTypes.LogicBlocks
{
    /// <summary>
    ///     Demonstrates every rich-types service-property / measuring-point shape introduced in PR 2,
    ///     wired through the <see cref="PresentationAttribute" /> surface — focus on how structs and
    ///     arrays render: per-StructField annotations, struct-array tables, numeric-array sparklines,
    ///     enum-array EnumLabel propagation, and the DisplayName override required when schema.title
    ///     is identity-bearing (struct / enum types).
    ///     <para />
    ///     Used by Vion.Examples.RichTypes.DevHost to visually inspect the emitted introspection JSON
    ///     and exercise the schema / presentation / runtime three-doc shape.
    /// </summary>
    [LogicBlock(Name = "Rich Types Demo", Icon = "device-line",
                Groups = new[]
                         {
                             PropertyGroup.Alarm,
                             PropertyGroup.Status,
                             PropertyGroup.Metric,
                             PropertyGroup.Configuration,
                             PropertyGroup.Diagnostics,
                             PropertyGroup.Identity,
                         })]
    public class RichBlock : LogicBlockBase
    {
        private readonly AlarmState[] _historyBuffer = new AlarmState[8];

        private int _historyHead;

        private int _tickCount;

        // ── Alarm ─────────────────────────────────────────────────────────────────

        /// <summary>
        ///     Enum status indicator. <see cref="PresentationAttribute.DisplayName" /> overrides the
        ///     identity-bearing schema.title (the CLR enum type name "AlarmState") so the UI can
        ///     show a human label.
        /// </summary>
        [ServiceMeasuringPoint(Description = "Active alarm state with per-member severity colouring.")]
        [Presentation(DisplayName = "Aktueller Alarm", Group = PropertyGroup.Alarm,
                      StatusIndicator = true, Importance = Importance.Primary)]
        public AlarmState CurrentAlarm { get; private set; }

        /// <summary>
        ///     Enum-typed array. The dashboard renders each cell with the per-enum-member
        ///     <see cref="EnumLabelAttribute" /> label and the <see cref="SeverityAttribute" />
        ///     colour, identical to a single-value enum render.
        /// </summary>
        [ServiceMeasuringPoint(Description = "Rolling history of the last eight alarm states (oldest → newest).")]
        [Presentation(DisplayName = "Alarm-Verlauf", Group = PropertyGroup.Alarm, Order = 10)]
        public ImmutableArray<AlarmState> AlarmHistory { get; private set; } = ImmutableArray<AlarmState>.Empty;

        // ── Status ────────────────────────────────────────────────────────────────

        /// <summary>
        ///     Struct measuring point. DisplayName overrides "Coordinates" (the CLR struct name)
        ///     because schema.title carries the type identity. Per-field labels / units / ranges
        ///     come from the [StructField] attributes on the record-struct constructor parameters.
        /// </summary>
        [ServiceMeasuringPoint(Description = "Live GPS fix as a flat (Lat, Lon) record struct.")]
        [Presentation(DisplayName = "Aktuelle Position", Group = PropertyGroup.Status,
                      Importance = Importance.Primary)]
        public Coordinates CurrentLocation { get; private set; }

        /// <summary>
        ///     Sentinel for "last successful sample" — auto-updating relative date rendering
        ///     ("3 Sekunden", "vor einer Minute") via <see cref="Formats.Relative" />.
        /// </summary>
        [ServiceMeasuringPoint(Description = "Wall-clock time of the last successful sample.")]
        [Presentation(DisplayName = "Zuletzt aktualisiert", Group = PropertyGroup.Status,
                      Format = Formats.Relative, Order = 20)]
        public DateTime LastSampleAt { get; private set; } = DateTime.UtcNow;

        // ── Metric ────────────────────────────────────────────────────────────────

        /// <summary>
        ///     Numeric array rendered as an inline sparkline. <see cref="PresentationAttribute.Decimals" />
        ///     applies to the optional tooltip / hover value, not the visual line.
        /// </summary>
        [ServiceMeasuringPoint(Unit = "A",
                               Description = "Last 16 current samples — visualised as a sparkline rather than a numeric list.")]
        [Presentation(DisplayName = "Strom-Histogramm", Group = PropertyGroup.Metric,
                      UiHint = UiHints.Sparkline, Importance = Importance.Primary)]
        public ImmutableArray<double> HistogramBuckets { get; private set; } = ImmutableArray<double>.Empty;

        /// <summary>
        ///     Monotonic counter. <see cref="MeasuringPointKind.TotalIncreasing" /> hints at billing-/
        ///     odometer-style time-series semantics (never resets — diffs across two samples are
        ///     guaranteed non-negative).
        /// </summary>
        [ServiceMeasuringPoint(Description = "Lifetime sample count — never resets.",
                               Kind = MeasuringPointKind.TotalIncreasing)]
        [Presentation(Group = PropertyGroup.Metric, Order = 10)]
        public long LongCounter { get; private set; }

        /// <summary>
        ///     Total with periodic reset — e.g. counters that roll over daily.
        /// </summary>
        [ServiceMeasuringPoint(Description = "Tagesweise zurückgesetzter Zähler.",
                               Kind = MeasuringPointKind.Total)]
        [Presentation(Group = PropertyGroup.Metric, Order = 20)]
        public uint UIntCounter { get; private set; }

        // ── Configuration ─────────────────────────────────────────────────────────

        /// <summary>
        ///     Writable struct property. Renders an editable form per field; the [StructField]
        ///     ranges drive validation, the per-field <see cref="StructFieldAttribute.Description" />
        ///     drives the input tooltips.
        /// </summary>
        [ServiceProperty(Description = "Operator-supplied target setpoint applied on the next control cycle.")]
        [Presentation(DisplayName = "Geplanter Sollwert", Group = PropertyGroup.Configuration,
                      Order = 10, Importance = Importance.Primary)]
        public ScheduledSetpoint? PreferredSetpoint { get; set; }

        /// <summary>
        ///     Writable struct array. Renders as a flat editable table with one column per
        ///     [StructField]. Demonstrates how the per-field annotations propagate to array
        ///     elements without further configuration.
        /// </summary>
        [ServiceProperty(Description = "Time-of-day schedule — each row is a (DateTime, kW, V) tuple applied at the given time.")]
        [Presentation(DisplayName = "Sollwert-Plan", Group = PropertyGroup.Configuration, Order = 20)]
        public ImmutableArray<ScheduledSetpoint> Schedule { get; set; } = ImmutableArray<ScheduledSetpoint>.Empty;

        [ServiceProperty(Title = "Spannungs-Sollwert", Unit = "V", Minimum = 0, Maximum = 400,
                         Description = "Statischer Spannungssollwert (Slider, da Min/Max gesetzt sind).")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 30,
                      UiHint = UiHints.Slider, Decimals = 1)]
        public double VoltageSetpoint { get; set; } = 230.0;

        [ServiceProperty(Title = "Gerätename")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 40)]
        public string DeviceName { get; set; } = "demo";

        [ServiceProperty(Title = "Bevorzugte Position",
                         Description = "Manuelle Override-Position; bei Null wird CurrentLocation verwendet.")]
        [Presentation(DisplayName = "Bevorzugte Position", Group = PropertyGroup.Configuration, Order = 50)]
        public Coordinates? PreferredLocation { get; set; }

        [ServiceProperty(Title = "Bevorzugter Modus")]
        [Presentation(DisplayName = "Bevorzugter Alarmmodus", Group = PropertyGroup.Configuration, Order = 60)]
        public AlarmState? PreferredAlarm { get; set; }

        [ServiceProperty(Title = "Optionaler Sollwert", Unit = "kW")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 70)]
        public double? OptionalTarget { get; set; }

        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Configuration, Order = 80)]
        public int? OptionalCount { get; set; }

        [ServiceProperty(Description = "Stunden des Tages, zu denen das Gerät aktiv sein soll.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 90)]
        public ImmutableArray<int> ScheduleHours { get; set; } = ImmutableArray<int>.Empty;

        // ── Diagnostics ───────────────────────────────────────────────────────────

        /// <summary>
        ///     Nullable-element array — demonstrates how schemas with optional members render
        ///     in tabular form (null cells appear as em-dash placeholders).
        /// </summary>
        [ServiceProperty(Title = "Messreihe mit Lücken", Unit = "kW",
                         Description = "Numerische Messreihe mit fehlenden Werten (null als Platzhalter).")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 10)]
        public ImmutableArray<double?> SamplesWithGaps { get; set; } = ImmutableArray<double?>.Empty;

        [ServiceProperty(Description = "Integer-Messreihe mit Lücken.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 20)]
        public ImmutableArray<int?> CountsWithGaps { get; set; } = ImmutableArray<int?>.Empty;

        /// <summary>
        ///     Nullable struct measuring point — null when the device has no last-known fix.
        /// </summary>
        [ServiceMeasuringPoint(Title = "Letzte bekannte Position")]
        [Presentation(DisplayName = "Letzte bekannte Position", Group = PropertyGroup.Diagnostics, Order = 30)]
        public Coordinates? LastKnownLocation { get; private set; }

        [ServiceMeasuringPoint(Title = "Letzter Fehler")]
        [Presentation(Group = PropertyGroup.Diagnostics, Order = 40)]
        public string? LastError { get; private set; }

        // ── Identity ──────────────────────────────────────────────────────────────

        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Identity, Order = 10)]
        public bool Enabled { get; set; } = true;

        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Identity, Order = 20)]
        public byte ByteCounter { get; set; }

        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Identity, Order = 30)]
        public ushort UShortRegister { get; set; }

        public RichBlock(ILogger logger) : base(logger)
        {
        }

        /// <summary>
        ///     Tick simulated values so the DevHost UI shows movement — without it the demo is
        ///     visually static and the Format = Relative auto-refresh isn't observable.
        /// </summary>
        [Timer(2)]
        public void OnTick()
        {
            _tickCount++;
            var now = DateTime.UtcNow;

            // Drifting GPS fix
            var t = _tickCount * 0.1;
            CurrentLocation = new Coordinates(47.3769 + Math.Sin(t) * 0.001, 8.5417 + Math.Cos(t) * 0.001);
            LastKnownLocation = CurrentLocation;
            LastSampleAt = now;

            // Histogram: rolling 16-sample buffer of a sine-modulated signal
            var samples = new double[16];

            for (var i = 0; i < 16; i++)
            {
                samples[i] = 10.0 + Math.Sin((t + i * 0.4)) * 5.0;
            }

            HistogramBuckets = samples.ToImmutableArray();

            // Counters tick monotonically
            LongCounter++;
            UIntCounter = (uint)(_tickCount % 86_400); // resets daily-ish

            // Cycle alarm state every ~20 ticks and roll the history buffer every tick
            var nextAlarm = (AlarmState)((_tickCount / 10) % 3);
            CurrentAlarm = nextAlarm;
            _historyBuffer[_historyHead] = nextAlarm;
            _historyHead = (_historyHead + 1) % _historyBuffer.Length;
            AlarmHistory = ImmutableArray.Create(_historyBuffer);

            // Surface a fault string once in a while
            LastError = nextAlarm == AlarmState.Critical ? $"E{_tickCount:D4}: simulated fault" : null;
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    /// <summary>
    ///     Flat record struct used as both a measuring-point and a service-property value.
    ///     The per-parameter <see cref="StructFieldAttribute" /> annotations propagate to the
    ///     emitted JSON-schema's <c>properties</c> map and drive per-field UI rendering
    ///     (label, unit, range, tooltip).
    /// </summary>
    public readonly record struct Coordinates(
        [StructField(Unit = "deg", Minimum = -90, Maximum = 90,
                     Description = "Latitude in WGS-84 decimal degrees.")]
        double Lat,
        [StructField(Unit = "deg", Minimum = -180, Maximum = 180,
                     Description = "Longitude in WGS-84 decimal degrees.")]
        double Lon);

    /// <summary>
    ///     Struct used inside an array — verifies per-StructField annotations propagate
    ///     through ImmutableArray&lt;T&gt; element schemas the same way they do for a
    ///     single-value struct property.
    /// </summary>
    public readonly record struct ScheduledSetpoint(
        [StructField(Description = "Application time (UTC).")]
        DateTime At,
        [StructField(Unit = "kW", Description = "Aktive Wirkleistung.")]
        double PowerSetpoint,
        [StructField(Unit = "V", Description = "Spannungs-Sollwert.")]
        double VoltageSetpoint);

    /// <summary>
    ///     Per-member <see cref="SeverityAttribute" /> drives the status-pill colour;
    ///     <see cref="EnumLabelAttribute" /> overrides the display label (the CLR name is the
    ///     wire identity; the label is purely cosmetic). Both work identically when the enum
    ///     appears as a single value, inside a nullable, or inside an array.
    /// </summary>
    public enum AlarmState
    {
        [Severity(StatusSeverity.Success)]
        [EnumLabel("Alles in Ordnung")]
        Ok,

        [Severity(StatusSeverity.Warning)]
        [EnumLabel("Warnung")]
        Warning,

        [Severity(StatusSeverity.Error)]
        [EnumLabel("Kritisch")]
        Critical,
    }
}
