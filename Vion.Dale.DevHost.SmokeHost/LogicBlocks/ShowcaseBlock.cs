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

        [ServiceProperty(Title = "Verbindung",
                         Description = "Read-only struct whose fields carry [StructField] titles, help, a duration, a timestamp, an enum and an ipv4 string.")]
        [Presentation(DisplayName = "Verbindung", Group = PropertyGroup.Status, Order = 5)]
        public LinkProfile CurrentLink { get; private set; } = new(Mood.Calm, "127.0.0.1", TimeSpan.FromMilliseconds(1200), DateTime.UtcNow, null);

        [ServiceMeasuringPoint(Unit = "kW", Description = "Last 16 samples — rendered as a sparkline.")]
        [Presentation(DisplayName = "Trend", Group = PropertyGroup.Status, UiHint = UiHints.Sparkline, Order = 10)]
        public ImmutableArray<double> Trend { get; private set; } = ImmutableArray<double>.Empty;

        [ServiceMeasuringPoint(Description = "Wall-clock time of the last tick — auto-refreshing relative rendering.")]
        [Presentation(DisplayName = "Zuletzt aktualisiert", Group = PropertyGroup.Status, Format = Formats.Relative, Order = 20)]
        public DateTime LastTickAt { get; private set; } = DateTime.UtcNow;

        // ── Metric (read-only counters) ─────────────────────────────────────────────

        [ServiceMeasuringPoint(Description = "Lifetime tick count — never resets. Emitted on every change (Immediate).",
                               Kind = MeasuringPointKind.TotalIncreasing,
                               Immediate = true)]
        [Presentation(Group = PropertyGroup.Metric)]
        public long Cycles { get; private set; }

        [ServiceMeasuringPoint(Description = "Uptime since start (Duration).")]
        [Presentation(DisplayName = "Laufzeit", Group = PropertyGroup.Metric, Order = 10)]
        public TimeSpan Uptime { get; private set; }

        // Deadband only: the interval is the disabling sentinel written as "0s" rather than "0" or "0ms".
        // The badge must read `deadband Δ0.5` with no throttle part — the sentinel is any duration that
        // resolves to zero, not two particular spellings.
        [ServiceMeasuringPoint(Unit = "kW",
                               MinInterval = "0s",
                               MinChange = "0.5",
                               Description = "Deadband only — throttling disabled by a zero interval, so this emits on change magnitude alone.")]
        [Presentation(DisplayName = "Abweichung", Group = PropertyGroup.Metric, Order = 15)]
        public double Drift { get; private set; }

        // ── Configuration (writable) ────────────────────────────────────────────────

        [ServiceProperty(Title = "Sollwert",
                         Unit = "kW",
                         Minimum = 0,
                         Maximum = 100,
                         MinInterval = "1s",
                         MinChange = "0.1",
                         Description =
                             "Operator setpoint — a bounded numeric input (Min/Max). Carries an advisory uiHint=slider chip; the current dashboard renders it as a number field, not a range slider. Throttled (1s) + deadband (Δ0.1), and persisted across restarts.")]
        [Presentation(Group = PropertyGroup.Configuration, UiHint = UiHints.Slider, Decimals = 1)]
        [Persistent]
        public double Setpoint { get; set; } = 25.0;

        [ServiceProperty(Title = "Abtastintervall", Description = "Sampling interval (Duration; ISO-8601 on the wire).")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 10)]
        public TimeSpan SampleInterval { get; set; } = TimeSpan.FromSeconds(2);

        [ServiceProperty(Title = "Heimatposition", Description = "Editable struct — one input per field.")]
        [Presentation(DisplayName = "Heimatposition", Group = PropertyGroup.Configuration, Order = 20)]
        public Position HomePosition { get; set; } = new(47.3769, 8.5417);

        [ServiceProperty(Title = "Verbindungsprofil", Description = "Editable struct — titled fields, a duration box that takes \"3s\", an ipv4 hint and a nullable field.")]
        [Presentation(DisplayName = "Verbindungsprofil", Group = PropertyGroup.Configuration, Order = 25)]
        public LinkProfile DesiredLink { get; set; } = new(Mood.Calm, "127.0.0.1", TimeSpan.FromSeconds(3), DateTime.UtcNow, null);

        [ServiceProperty(Title = "Bevorzugte Stimmung", Description = "Nullable enum — null means 'auto'.")]
        [Presentation(DisplayName = "Bevorzugte Stimmung", Group = PropertyGroup.Configuration, Order = 30)]
        public Mood? PreferredMood { get; set; }

        [ServiceProperty(Title = "Optionales Limit", Unit = "kW", Description = "Nullable number.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 40)]
        public double? OptionalLimit { get; set; }

        // ── Conditional visibility (VisibleWhen) ────────────────────────────────────
        // The DirectMeasurement pattern: when direct measurement is on, the two CT-ratio commissioning
        // inputs become internal no-ops, so hiding them is a pure display decision — they keep existing
        // and functioning. Toggle DirectMeasurement to watch the two inputs hide/show live.
        [ServiceProperty(Title = "Direkte Messung (ohne Stromwandler)", Description = "When on, the CT-ratio inputs below are internal no-ops and hide.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 50)]
        public bool DirectMeasurement { get; set; }

        [ServiceProperty(Title = "Primärstrom (schreiben)",
                         Unit = "A",
                         Minimum = 1,
                         Maximum = 5000,
                         Description = "CT primary current — only relevant when DirectMeasurement is off; hidden otherwise.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 51, VisibleWhen = "DirectMeasurement == false")]
        public double PrimaryCurrentToWriteA { get; set; } = 100;

        [ServiceProperty(Title = "Sekundärstrom (schreiben)",
                         Unit = "A",
                         Minimum = 1,
                         Maximum = 5,
                         Description = "CT secondary current — hidden together with the primary when DirectMeasurement is on.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 52, VisibleWhen = "DirectMeasurement == false")]
        public double SecondaryCurrentToWriteA { get; set; } = 5;

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
            Drift = Cycles * 0.3;
            Uptime = TimeSpan.FromSeconds(_ticks);
            LastTickAt = DateTime.UtcNow;
            CurrentPosition = new Position(47.3769 + Math.Sin(t) * 0.001, 8.5417 + Math.Cos(t) * 0.001);

            for (var i = 0; i < _trendBuffer.Length; i++)
            {
                _trendBuffer[i] = Setpoint + Math.Sin(t + i * 0.4) * 5.0;
            }

            Trend = ImmutableArray.Create(_trendBuffer);

            CurrentMood = (Mood)(_ticks / 5 % 3);
            CurrentLink = new LinkProfile(CurrentMood,
                                          DesiredLink.Address,
                                          TimeSpan.FromMilliseconds(900 + _ticks % 600),
                                          LastTickAt,
                                          _ticks % 10 == 0 ? null : TimeSpan.FromMilliseconds(_ticks % 10 * 50));
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

    /// <summary>
    ///     The struct-field presentation fixture: an enum field (whose schema title is its type name, so its
    ///     authored title, value labels and severities ride presentation.fields instead — VION-105), an ipv4
    ///     string, a duration, a timestamp and a nullable duration.
    ///     Rendered read-only by the StructViewer and writable by the flat-struct form.
    /// </summary>
    public readonly record struct LinkProfile(
        [StructField(Title = "State", Description = "Enum field — its title has no inline slot, so it rides presentation.fields.state.displayName.")]
        Mood State,
        [StructField(Title = "Server address", StringFormat = StringFormats.Ipv4, Description = "Where the client connects. The format is a hint, not validation.")]
        string Address,
        [StructField(Title = "Round trip (last)", Description = "Duration field — must read as a clock time, never as PT1.2S.")]
        TimeSpan RoundTrip,
        [StructField(Title = "Last contact", Description = "Timestamp field — must read as a date, never as an ISO string.")]
        DateTime LastContactAt,
        [StructField(Title = "Queued wait (last)", Description = "Nullable duration — the ∅ toggle applies here.")]
        TimeSpan? QueuedWait);

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