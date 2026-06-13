using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace VionIotLibraryTemplate
{
    /// <summary>
    ///     A self-contained room-thermostat simulation — a "hello world" for Dale logic blocks. It reads a
    ///     setpoint, drives a (simulated) heater/cooler, and reports live state, exercising the core SDK
    ///     surface end to end with NO hardware and NO other blocks. The clean split is "what you set" vs
    ///     "what it does":
    ///     <list type="bullet">
    ///         <item>
    ///             writable, persisted configuration — <see cref="TargetTemperature" /> (a slider) and <see cref="Mode" />
    ///             (an enum dropdown)
    ///         </item>
    ///         <item>
    ///             a colour-coded status pill — <see cref="Status" />, a read-only enum with <c>[Severity]</c> +
    ///             <c>[EnumLabel]</c> + <c>StatusIndicator</c>
    ///         </item>
    ///         <item>
    ///             live, unit-bearing read-outs — <c>[ServiceMeasuringPoint]</c> + <c>[Presentation]</c> (
    ///             <see cref="CurrentTemperature" />, <see cref="EnergyUsedKwh" />)
    ///         </item>
    ///         <item>
    ///             ONE member that is BOTH a property and a metric — <see cref="CurrentTemperature" /> carries both
    ///             attributes (the SDK's cross-fill rule): you watch the live value, and can also inject a reading. The
    ///             metadata is declared once on the <c>[ServiceProperty]</c> and the bare <c>[ServiceMeasuringPoint]</c>
    ///             inherits it
    ///         </item>
    ///         <item>a billing-style accumulator — <c>MeasuringPointKind.TotalIncreasing</c> (<see cref="EnergyUsedKwh" />)</item>
    ///         <item>periodic logic — <c>[Timer]</c></item>
    ///     </list>
    ///     Run <c>dale dev</c> and watch <see cref="CurrentTemperature" /> track <see cref="TargetTemperature" /> while the
    ///     <see cref="Status" /> pill changes colour — then drive it with <c>scenarios/thermostat.scenario.json</c>.
    /// </summary>
    [LogicBlock(Name = "Thermostat", Icon = "temp-hot-line", Groups = new[] { PropertyGroup.Status, PropertyGroup.Configuration, PropertyGroup.Metric })]
    public class Thermostat : LogicBlockBase
    {
        private readonly ILogger _logger;

        // The "physical" room temperature the simulation integrates; CurrentTemperature is its rounded view.
        private double _roomCelsius = 19.0;

        // ── Configuration — what you set (writable; public-setter properties persist across restarts) ──

        [ServiceProperty(Unit = "°C", Minimum = 5, Maximum = 30, Description = "The temperature the thermostat aims for.")]
        [Presentation(Group = PropertyGroup.Configuration, Importance = Importance.Primary, UiHint = UiHints.Slider, Decimals = 1)]
        public double TargetTemperature { get; set; } = 21.0;

        [ServiceProperty(Description = "Which way the thermostat is allowed to act.")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public ThermostatMode Mode { get; set; } = ThermostatMode.Auto;

        // ── Status — what it's doing (read-only) and the live room temperature ─────────────────────

        [ServiceMeasuringPoint(Description = "What the thermostat is doing right now.")]
        [Presentation(DisplayName = "Status", Group = PropertyGroup.Status, StatusIndicator = true, Importance = Importance.Primary)]
        public ThermostatStatus Status { get; private set; } = ThermostatStatus.Idle;

        // The ONE member that is BOTH a writable [ServiceProperty] and a read-only [ServiceMeasuringPoint]
        // (the SDK's cross-fill rule): you watch the live value, and can also inject a reading for testing.
        // Unit/Description are declared once on the property; the bare measuring point inherits them, and the
        // DevHost renders it as a single row.
        [ServiceProperty(Unit = "°C", Description = "Live room temperature — also writable, so you can inject a reading for testing.")]
        [ServiceMeasuringPoint]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary, Decimals = 1)]
        public double CurrentTemperature
        {
            get => Math.Round(_roomCelsius, 2);

            set => _roomCelsius = value;
        }

        // ── Metric ──────────────────────────────────────────────────────────────────────────────────

        [ServiceMeasuringPoint(Unit = "kWh", Kind = MeasuringPointKind.TotalIncreasing, Description = "Energy used since start — never resets.")]
        [Presentation(Group = PropertyGroup.Metric, Decimals = 3)]
        public double EnergyUsedKwh { get; private set; }

        public Thermostat(ILogger logger) : base(logger)
        {
            _logger = logger;
        }

        /// <summary>The control loop plus a tiny room-physics simulation — runs once per second.</summary>
        [Timer(1)]
        public void Tick()
        {
            var error = TargetTemperature - _roomCelsius;

            Status = Mode switch
            {
                ThermostatMode.Off => ThermostatStatus.Idle,
                _ when error > 0.2 && Mode != ThermostatMode.Cool => ThermostatStatus.Heating,
                _ when error < -0.2 && Mode != ThermostatMode.Heat => ThermostatStatus.Cooling,
                _ => ThermostatStatus.Idle,
            };

            // The room reacts to what we're doing (and drifts gently toward ambient when idle).
            _roomCelsius += Status switch
            {
                ThermostatStatus.Heating => 0.1,
                ThermostatStatus.Cooling => -0.1,
                _ => -0.01,
            };

            // Meter energy only while actively heating or cooling.
            if (Status != ThermostatStatus.Idle)
            {
                EnergyUsedKwh += 0.002;
            }
        }

        protected override void Ready()
        {
            _logger.LogInformation("Thermostat ready — aiming for {Target} °C.", TargetTemperature);
        }
    }

    /// <summary>Which way the thermostat is allowed to act. A writable enum renders as a dropdown in the DevHost UI.</summary>
    public enum ThermostatMode
    {
        Off,

        Heat,

        Cool,

        Auto,
    }

    /// <summary>
    ///     What the thermostat is doing right now. The per-member <c>[Severity]</c> drives the status-pill
    ///     colour and <c>[EnumLabel]</c> the text, so the enum reads as a coloured pill in the DevHost UI.
    /// </summary>
    public enum ThermostatStatus
    {
        [Severity(StatusSeverity.Success)]
        [EnumLabel("Idle")]
        Idle,

        [Severity(StatusSeverity.Warning)]
        [EnumLabel("Heating")]
        Heating,

        [Severity(StatusSeverity.Info)]
        [EnumLabel("Cooling")]
        Cooling,
    }
}