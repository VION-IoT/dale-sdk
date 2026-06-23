using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.SmokeHost.LogicBlocks
{
    /// <summary>
    ///     The value-shape + presentation champion: one block exposing a representative service property /
    ///     measuring point of every shape the DevHost UI renders (int, double-with-slider, bool, string,
    ///     enum-with-labels, nullable, TimeSpan/duration, struct, array), across every presentation group,
    ///     in both writable and read-only forms — and a 1 s timer that moves the live values so stepping,
    ///     <c>advance</c>, and the relative-time rendering are observable. The smoke test reads/writes these
    ///     over HTTP; the live-UI tier eyeballs that each shape renders.
    /// </summary>
    [LogicBlock(Name = "Showcase",
                Icon = "device-line",
                Groups = new[] { PropertyGroup.Status, PropertyGroup.Metric, PropertyGroup.Configuration, PropertyGroup.Diagnostics, PropertyGroup.Identity })]
    public class ShowcaseBlock : LogicBlockBase
    {
        private readonly double[] _trendBuffer = new double[16];

        private int _ticks;

        // ── Status (read-only, live) ────────────────────────────────────────────────

        [ServiceMeasuringPoint(Description = "Current operating mood — a status pill with per-member severity colour.")]
        [Presentation(DisplayName = "Stimmung", Group = PropertyGroup.Status, StatusIndicator = true, Importance = Importance.Primary)]
        public Mood CurrentMood { get; private set; }

        [ServiceMeasuringPoint(Description = "Live (x, y) position as a flat record struct — drifts each tick.")]
        [Presentation(DisplayName = "Position", Group = PropertyGroup.Status)]
        public Position CurrentPosition { get; private set; }

        [ServiceMeasuringPoint(Unit = "kW", Description = "Last 16 samples — rendered as a sparkline.")]
        [Presentation(DisplayName = "Trend", Group = PropertyGroup.Status, UiHint = UiHints.Sparkline, Order = 10)]
        public ImmutableArray<double> Trend { get; private set; } = ImmutableArray<double>.Empty;

        [ServiceMeasuringPoint(Description = "Wall-clock time of the last tick — auto-refreshing relative rendering.")]
        [Presentation(DisplayName = "Zuletzt aktualisiert", Group = PropertyGroup.Status, Format = Formats.Relative, Order = 20)]
        public DateTime LastTickAt { get; private set; } = DateTime.UtcNow;

        // ── Metric (read-only counters) ─────────────────────────────────────────────

        [ServiceMeasuringPoint(Description = "Lifetime tick count — never resets. RFC 0004: emitted on every change (Immediate).", Kind = MeasuringPointKind.TotalIncreasing, Immediate = true)]
        [Presentation(Group = PropertyGroup.Metric)]
        public long Cycles { get; private set; }

        [ServiceMeasuringPoint(Description = "Uptime since start (Duration).")]
        [Presentation(DisplayName = "Laufzeit", Group = PropertyGroup.Metric, Order = 10)]
        public TimeSpan Uptime { get; private set; }

        // ── Configuration (writable) ────────────────────────────────────────────────

        [ServiceProperty(Title = "Sollwert",
                         Unit = "kW",
                         Minimum = 0,
                         Maximum = 100,
                         MinInterval = "1s",
                         MinChange = "0.1",
                         Description =
                             "Operator setpoint — a bounded numeric input (Min/Max). Carries an advisory uiHint=slider chip; the current dashboard renders it as a number field, not a range slider. RFC 0004: throttled (1s) + deadband (Δ0.1), and persisted across restarts.")]
        [Presentation(Group = PropertyGroup.Configuration, UiHint = UiHints.Slider, Decimals = 1)]
        [Persistent]
        public double Setpoint { get; set; } = 25.0;

        [ServiceProperty(Title = "Abtastintervall", Description = "Sampling interval (Duration; ISO-8601 on the wire).")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 10)]
        public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(2);

        [ServiceProperty(Title = "Heimatposition", Description = "Editable struct — one input per field.")]
        [Presentation(DisplayName = "Heimatposition", Group = PropertyGroup.Configuration, Order = 20)]
        public Position HomePosition { get; set; } = new(47.3769, 8.5417);

        [ServiceProperty(Title = "Bevorzugte Stimmung", Description = "Nullable enum — null means 'auto'.")]
        [Presentation(DisplayName = "Bevorzugte Stimmung", Group = PropertyGroup.Configuration, Order = 30)]
        public Mood? PreferredMood { get; set; }

        [ServiceProperty(Title = "Optionales Limit", Unit = "kW", Description = "Nullable number.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 40)]
        public double? OptionalLimit { get; set; }

        // ── Diagnostics (read-only, private setter) ─────────────────────────────────

        [ServiceProperty(Title = "Letzte Notiz", Description = "Read-only status string (private setter) — writing it must be rejected.")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public string? LastNote { get; private set; }

        // ── Identity (writable) ──────────────────────────────────────────────────────

        [ServiceProperty(Title = "Gerätename")]
        [Presentation(Group = PropertyGroup.Identity)]
        public string DeviceName { get; set; } = "showcase";

        [ServiceProperty(Title = "Aktiviert")]
        [Presentation(Group = PropertyGroup.Identity, Order = 10)]
        public bool Enabled { get; set; } = true;

        public ShowcaseBlock(ILogger logger) : base(logger)
        {
        }

        /// <summary>Moves the live values each virtual second so stepping / advance / relative-time are observable.</summary>
        [Timer(1)]
        public void OnTick()
        {
            _ticks++;
            var t = _ticks * 0.1;

            Cycles++;
            Uptime = TimeSpan.FromSeconds(_ticks);
            LastTickAt = DateTime.UtcNow;
            CurrentPosition = new Position(47.3769 + Math.Sin(t) * 0.001, 8.5417 + Math.Cos(t) * 0.001);

            for (var i = 0; i < _trendBuffer.Length; i++)
            {
                _trendBuffer[i] = Setpoint + Math.Sin(t + i * 0.4) * 5.0;
            }

            Trend = ImmutableArray.Create(_trendBuffer);

            CurrentMood = (Mood)(_ticks / 5 % 3);
            LastNote = CurrentMood == Mood.Overloaded ? $"N{_ticks:D4}: load high" : null;
        }

        /// <inheritdoc />
        protected override void Ready()
        {
        }
    }

    /// <summary>A flat record struct used as both a writable property and a read-only measuring point.</summary>
    public readonly record struct Position(
        [StructField(Title = "X", Unit = "deg", Minimum = -90, Maximum = 90, Description = "Horizontal coordinate.")]
        double X,
        [StructField(Title = "Y", Unit = "deg", Minimum = -180, Maximum = 180, Description = "Vertical coordinate.")]
        double Y);

    /// <summary>Status enum with per-member label + severity — exercises the status-pill render across value / nullable.</summary>
    public enum Mood
    {
        [Severity(StatusSeverity.Success)]
        [EnumLabel("Ruhig")]
        Calm,

        [Severity(StatusSeverity.Warning)]
        [EnumLabel("Beschäftigt")]
        Busy,

        [Severity(StatusSeverity.Error)]
        [EnumLabel("Überlastet")]
        Overloaded,
    }
}