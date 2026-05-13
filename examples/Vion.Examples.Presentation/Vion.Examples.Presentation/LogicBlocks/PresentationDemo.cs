using System;
using System.Collections.Immutable;
using Microsoft.Extensions.Logging;
using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.Core;
using Vion.Examples.Presentation.Conventions;

namespace Vion.Examples.Presentation.LogicBlocks
{
    /// <summary>
    ///     Exercises every variant of the declarative-presentation attribute surface in one block:
    ///     all <see cref="PropertyGroup" /> values, all <see cref="Importance" /> levels,
    ///     multiple <c>StatusIndicator = true</c> properties, all three
    ///     <see cref="MeasuringPointKind" /> values, a <c>WriteOnly</c> secret, a
    ///     <c>UiHint = UiHints.Trigger</c> action button, explicit <c>Decimals</c>, an
    ///     <c>Order</c> override, and a couple of preset-attribute usages from the
    ///     example's own <c>Conventions/</c> folder (integrator-specific — each SDK user
    ///     ships their own equivalents under their own namespace).
    /// </summary>
    [LogicBlock(Name = "Präsentations-Demo",
                Icon = "dashboard-line",
                Groups = new[]
                         {
                             PropertyGroup.Alarm,
                             PropertyGroup.Status,
                             PropertyGroup.Metric,
                             PropertyGroup.Configuration,
                             PropertyGroup.Diagnostics,
                             PropertyGroup.Identity,
                         })]
    public class PresentationDemo : LogicBlockBase
    {
        // ── Identity group ────────────────────────────────────────────────────────
        [ServiceProperty(Title = "Modell",
                         Description = "Manufacturer's model identifier; immutable after commissioning.")]
        [Presentation(Group = PropertyGroup.Identity)]
        public string Model { get; set; } = "VDP-1000";

        [ServiceProperty(Title = "Seriennummer")]
        [Presentation(Group = PropertyGroup.Identity)]
        public string SerialNumber { get; set; } = "VDP-0000001";

        // Positive-Order example: this should render AFTER the unset-order Identity entries.
        [ServiceProperty(Title = "Firmware-Version")]
        [Presentation(Group = PropertyGroup.Identity, Order = 99)]
        public string FirmwareVersion { get; set; } = "1.4.2";

        // ── Alarm group (two status indicators — demonstrates multi-status support) ──
        [ServiceProperty(Title = "Anlagenzustand",
                         Description = "High-level operating state; drives the section banner colour.")]
        [Presentation(Group = PropertyGroup.Alarm, StatusIndicator = true, Importance = Importance.Primary)]
        public PlantState Plant { get; private set; } = PlantState.Running;

        [ServiceProperty(Title = "Kommunikation",
                         Description = "Link health between gateway and cloud.")]
        [Presentation(Group = PropertyGroup.Alarm, StatusIndicator = true)]
        public LinkState Link { get; private set; } = LinkState.Connected;

        // ── Status group ──────────────────────────────────────────────────────────
        // Preset attribute pulls Unit + Group/Importance/Decimals from the example's
        // own Conventions/ folder. (Integrators ship their own equivalents.)
        [Kilowatts]
        [StateMetric]
        public double ActivePower { get; private set; } = 12.345;

        // Explicit Order = -5 forces this to render BEFORE the unset-Order Status entries.
        [Volts]
        [Presentation(Group = PropertyGroup.Status, Order = -5, Decimals = 1)]
        public double Voltage { get; private set; } = 230.4;

        // ── Metric group (each MeasuringPointKind once) ───────────────────────────
        [KilowattsMeter] // Kind = Measurement
        [Presentation(Group = PropertyGroup.Metric)]
        public double Power { get; private set; } = 11.7;

        [ServiceMeasuringPoint(Title = "Speicherinhalt", Unit = "kWh", Kind = MeasuringPointKind.Total)]
        [Presentation(Group = PropertyGroup.Metric)]
        public double StoredEnergy { get; private set; } = 5.2;

        [CumulativeKilowattHours] // Kind = TotalIncreasing
        [EnergyCounter]
        public double EnergyImported { get; private set; } = 1234.5;

        // ── Configuration group ───────────────────────────────────────────────────
        [Kilowatts]
        [ConfigSetting]
        public double MaximumPower { get; set; } = 50;

        // UiHint = Slider on a bounded numeric — the dashboard renders a slider control.
        [Percent]
        [Presentation(Group = PropertyGroup.Configuration, UiHint = UiHints.Slider)]
        public double TargetStateOfCharge { get; set; } = 80;

        // Hidden importance: present in introspection but suppressed by the dashboard.
        [ServiceProperty(Title = "Interner Modus")]
        [Presentation(Group = PropertyGroup.Configuration, Importance = Importance.Hidden)]
        public int InternalMode { get; set; } = 0;

        // WriteOnly secret — dashboard renders the "set / hidden" placeholder.
        [Secret] // ServiceProperty + WriteOnly = true
        [Presentation(DisplayName = "API-Schlüssel", Group = PropertyGroup.Configuration)]
        public string? ApiKey { get; set; }

        // UiHint = Trigger on a writable bool — dashboard renders a button.
        // Trigger workaround: the getter always returns false (resting state). Click commits
        // true → setter runs the action → next read still reports false. Without this pattern
        // a plain { get; set; } would latch at true after the first click. Forbidden with
        // [Persistent] (no state to persist).
        [ServiceProperty(Title = "Kalibrierung starten",
                         Description = "Trigger workaround pattern: the getter returns false, the setter runs the action. Each click increments the CalibrationsTriggered counter in Diagnostics.")]
        [Presentation(Group = PropertyGroup.Configuration, UiHint = UiHints.Trigger)]
        public bool StartCalibration
        {
            get => false;
            set
            {
                if (value)
                {
                    CalibrationsTriggered++;
                }
            }
        }

        // Counter incremented by the StartCalibration trigger — makes each click visible
        // in the UI without needing the trigger property itself to hold state.
        [ServiceMeasuringPoint(Title = "Ausgelöste Kalibrierungen",
                               Description = "Counts how often the StartCalibration trigger has fired since startup.",
                               Kind = MeasuringPointKind.TotalIncreasing)]
        [Presentation(Group = PropertyGroup.Diagnostics, Importance = Importance.Secondary)]
        public int CalibrationsTriggered { get; private set; }

        // ── Ungrouped item ────────────────────────────────────────────────────────
        // No [Presentation(Group = ...)] → falls into the "Ungrouped" section, which the UI
        // renders after all platform-default groups (last in PLATFORM_DEFAULT_GROUP_ORDER).
        // Also exercises UiHint = "multiline" for a free-form text input.
        [ServiceProperty(Title = "Hinweise",
                         Description = "Free-form operator notes. No group → renders in the Ungrouped section.")]
        [Presentation(UiHint = UiHints.Multiline)]
        public string Notes { get; set; } = "";

        // ── Diagnostics group ─────────────────────────────────────────────────────
        [ServiceMeasuringPoint(Title = "Antwortzeit", Unit = "ms", Kind = MeasuringPointKind.Measurement)]
        [Diagnostic]
        public double ResponseTime { get; private set; } = 12;

        // Decimals = 2 on a numeric — fine. (Compare DALE021: same on a string would warn.)
        [ServiceProperty(Title = "Verzögerungs-Sollwert", Unit = "ms")]
        [Presentation(Group = PropertyGroup.Diagnostics, Decimals = 2)]
        public double DelaySetpoint { get; set; } = 0.50;

        // Secondary importance — secondary tile slot.
        [ServiceMeasuringPoint(Title = "Anzahl Neustarts")]
        [Presentation(Group = PropertyGroup.Diagnostics, Importance = Importance.Secondary)]
        public int RestartCount { get; private set; }

        // ── Date / duration formatting (Presentation.Format) ──────────────────────
        // The Format value is a moment.js / day.js compatible token string. The renderer
        // (dashboard / DevHost) interprets it. Two reserved sentinels: "relative" and "humanize".

        // Relative date — auto-updating "3 minutes ago". Sentinel value short-circuits the
        // token interpreter.
        [ServiceMeasuringPoint(Title = "Letzte Probe",
                               Description = "Timestamp of the last sample taken. Rendered as auto-updating relative time.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Format = Formats.Relative)]
        public DateTime LastSampleAt { get; private set; } = DateTime.UtcNow.AddMinutes(-3);

        // Absolute date — explicit ISO with millis. Use when "when exactly" matters.
        [ServiceMeasuringPoint(Title = "Boot-Zeit",
                               Description = "When this LB instance started. Rendered with millisecond precision.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Format = Formats.IsoMillis)]
        public DateTime BootedAt { get; private set; } = DateTime.UtcNow;

        // Locale-aware long format — readable in the user's locale.
        [ServiceProperty(Title = "Geplanter Start",
                         Description = "Operator-set future timestamp. Rendered locale-aware.")]
        [Presentation(Group = PropertyGroup.Configuration, Format = Formats.LocaleLong)]
        public DateTime ScheduledAt { get; set; } = DateTime.UtcNow.AddHours(2);

        // Clock-style duration — typical for response times / SLA windows.
        [ServiceMeasuringPoint(Title = "Mittlere Antwortzeit",
                               Description = "Average response time as HH:mm:ss.SSS for sub-second visibility.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Format = Formats.ClockMillis)]
        public TimeSpan AvgResponseTime { get; private set; } = TimeSpan.FromMilliseconds(123);

        // Humanized duration — sentinel; rough natural-language output.
        [ServiceMeasuringPoint(Title = "Betriebszeit",
                               Description = "How long this LB has been running. Rendered as a humanized phrase.")]
        [Presentation(Group = PropertyGroup.Diagnostics, Importance = Importance.Secondary, Format = Formats.Humanize)]
        public TimeSpan Uptime { get; private set; } = TimeSpan.FromHours(3.5);

        // ── Sparkline + range data ────────────────────────────────────────────────
        [ServiceMeasuringPoint(Title = "Lastprofil")]
        [Presentation(Group = PropertyGroup.Metric, UiHint = UiHints.Sparkline)]
        public ImmutableArray<double> RecentLoad { get; private set; } = ImmutableArray.Create(10.0, 11.5, 9.8, 12.1);

        private readonly DateTime _startedAt = DateTime.UtcNow;

        private int _tickCount;

        public PresentationDemo(ILogger logger) : base(logger)
        {
        }

        // Ticks the volatile readonly values so the dashboard sees them change.
        // The Plant / Link status indicators also flap a bit so their severity colours move.
        [Timer(2)]
        public void OnTimer()
        {
            _tickCount++;
            var now = DateTime.UtcNow;

            LastSampleAt = now;
            Uptime = now - _startedAt;

            // ActivePower: gentle sine wave around 12 kW, swing ±2 kW.
            ActivePower = 12 + 2 * Math.Sin(_tickCount * 0.3);
            Power = ActivePower;
            Voltage = 230 + Math.Sin(_tickCount * 0.5);

            // EnergyImported: monotonically increasing counter.
            EnergyImported += ActivePower * 2 / 3600;

            // AvgResponseTime: vary between 50 ms and 200 ms.
            AvgResponseTime = TimeSpan.FromMilliseconds(125 + 75 * Math.Sin(_tickCount * 0.4));
            ResponseTime = AvgResponseTime.TotalMilliseconds;

            // RecentLoad: shift in a new sample (oldest drops off).
            if (RecentLoad.Length < 12)
            {
                RecentLoad = RecentLoad.Add(ActivePower);
            }
            else
            {
                RecentLoad = RecentLoad.RemoveAt(0).Add(ActivePower);
            }

            // Plant state cycles slowly through the four states (every ~20 s with 2-s ticks).
            Plant = (_tickCount / 10 % 4) switch
            {
                0 => PlantState.Running,
                1 => PlantState.Maintenance,
                2 => PlantState.Fault,
                _ => PlantState.Idle,
            };

            // Link flickers more often.
            Link = (_tickCount / 4 % 3) switch
            {
                0 => LinkState.Connected,
                1 => LinkState.Connecting,
                _ => LinkState.Disconnected,
            };
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    public enum PlantState
    {
        [EnumLabel("Bereit")]
        [Severity(StatusSeverity.Neutral)]
        Idle,

        [EnumLabel("In Betrieb")]
        [Severity(StatusSeverity.Success)]
        Running,

        [EnumLabel("Wartung")]
        [Severity(StatusSeverity.Warning)]
        Maintenance,

        [EnumLabel("Störung")]
        [Severity(StatusSeverity.Error)]
        Fault,
    }

    public enum LinkState
    {
        [EnumLabel("Verbunden")]
        [Severity(StatusSeverity.Success)]
        Connected,

        [EnumLabel("Verbinden...")]
        [Severity(StatusSeverity.Warning)]
        Connecting,

        [EnumLabel("Getrennt")]
        [Severity(StatusSeverity.Error)]
        Disconnected,
    }
}
