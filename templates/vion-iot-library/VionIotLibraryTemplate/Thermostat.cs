using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace VionIotLibraryTemplate
{
    /// <summary>
    ///     A self-contained room-thermostat simulation — a "hello world" for Dale logic blocks. It reads a
    ///     setpoint, drives a (simulated) heater/cooler, and reports live state, exercising the core SDK
    ///     surface end to end with NO hardware and NO other blocks:
    ///     <list type="bullet">
    ///         <item>writable, persisted configuration — <c>[ServiceProperty]</c> (the TargetTemperature slider, Mode)</item>
    ///         <item>
    ///             a colour-coded status pill — an enum with <c>[Severity]</c> + <c>[EnumLabel]</c> + <c>StatusIndicator</c>
    ///         </item>
    ///         <item>live, unit-bearing read-outs — <c>[ServiceMeasuringPoint]</c> + <c>[Presentation]</c></item>
    ///         <item>
    ///             members that are BOTH a property and a metric — <see cref="CurrentTemperature" /> and
    ///             <see cref="State" /> carry both attributes; the metadata is declared once on the
    ///             <c>[ServiceProperty]</c> and the bare <c>[ServiceMeasuringPoint]</c> inherits it (the SDK's cross-fill
    ///             rule)
    ///         </item>
    ///         <item>a billing-style accumulator — <c>MeasuringPointKind.TotalIncreasing</c> (<see cref="EnergyUsedKwh" />)</item>
    ///         <item>periodic logic — <c>[Timer]</c></item>
    ///     </list>
    ///     Run <c>dale dev</c> and watch <see cref="CurrentTemperature" /> track <see cref="TargetTemperature" /> while the
    ///     <see cref="State" /> pill changes colour — then drive it with <c>scenarios/thermostat.scenario.json</c>.
    /// </summary>
    [LogicBlock(Name = "Thermostat", Icon = "temp-hot-line", Groups = new[] { PropertyGroup.Status, PropertyGroup.Configuration, PropertyGroup.Metric })]
    public class Thermostat : LogicBlockBase
    {
        private readonly ILogger _logger;

        // The "physical" room temperature the simulation integrates; CurrentTemperature is its rounded view.
        private double _roomCelsius = 19.0;

        // ── Configuration (writable; public-setter properties persist across restarts by default) ──

        [ServiceProperty(Unit = "°C", Minimum = 5, Maximum = 30, Description = "The temperature the thermostat aims for.")]
        [Presentation(Group = PropertyGroup.Configuration, Importance = Importance.Primary, UiHint = UiHints.Slider, Decimals = 1)]
        public double TargetTemperature { get; set; } = 21.0;

        [ServiceProperty(Description = "How the thermostat is allowed to act.")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public ThermostatMode Mode { get; set; } = ThermostatMode.Auto;

        // ── Status — each member is BOTH a writable property AND a read-only measuring point ────────
        // Description/Unit live on [ServiceProperty]; the bare [ServiceMeasuringPoint] inherits them
        // (the SDK cross-fill rule), so the same member surfaces as a control AND a live read-out.

        [ServiceProperty(Description = "What the thermostat is doing right now.")]
        [ServiceMeasuringPoint]
        [Presentation(DisplayName = "State", Group = PropertyGroup.Status, StatusIndicator = true, Importance = Importance.Primary)]
        public ThermostatState State { get; set; } = ThermostatState.Idle;

        [ServiceProperty(Unit = "°C", Description = "Live room temperature — writable so you can inject a reading.")]
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

            State = Mode switch
            {
                ThermostatMode.Off => ThermostatState.Idle,
                _ when error > 0.2 && Mode != ThermostatMode.Cooling => ThermostatState.Heating,
                _ when error < -0.2 && Mode != ThermostatMode.Heating => ThermostatState.Cooling,
                _ => ThermostatState.Idle,
            };

            // The room reacts to what we're doing (and drifts gently toward ambient when idle).
            _roomCelsius += State switch
            {
                ThermostatState.Heating => 0.1,
                ThermostatState.Cooling => -0.1,
                _ => -0.01,
            };

            // Meter energy only while actively heating or cooling.
            if (State != ThermostatState.Idle)
            {
                EnergyUsedKwh += 0.002;
            }
        }

        protected override void Ready()
        {
            _logger.LogInformation("Thermostat ready — aiming for {Target} °C.", TargetTemperature);
        }
    }

    /// <summary>How the thermostat is allowed to act. A writable enum renders as a dropdown in the DevHost UI.</summary>
    public enum ThermostatMode
    {
        Off,

        Heating,

        Cooling,

        Auto,
    }

    /// <summary>
    ///     What the thermostat is doing. The per-member <c>[Severity]</c> drives the status-pill colour and
    ///     <c>[EnumLabel]</c> the text, so the enum reads as a coloured pill in the DevHost UI. (Colours are
    ///     chosen for variety here, not literal meaning.)
    /// </summary>
    public enum ThermostatState
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