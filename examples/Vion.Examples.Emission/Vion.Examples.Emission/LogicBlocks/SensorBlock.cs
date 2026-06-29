using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Emission.LogicBlocks
{
    /// <summary>
    ///     Showcases the RFC 0004 emission policy — one service property / measuring point per knob:
    ///     throttle (<c>MinInterval</c>), deadband (<c>MinChange</c>), <c>Immediate</c> bypass, the always-on
    ///     value-equality dedup floor, independent per-stream throttling on a dual-annotated member, a custom
    ///     <see cref="ThreePhase" /> deadband, and the invisible 250 ms default. Two members are writable so the
    ///     gate can be driven from the DevHost UI.
    ///     <para />
    ///     The policy is active only on a real clock — in DevHost that means FREE-RUN mode. Under the
    ///     deterministic / stepped clock it is force-disabled (so scenarios stay exact), and the TestKit re-enables
    ///     it explicitly via <c>WithEmissionPolicy(FromAttributes)</c>. See the README.
    /// </summary>
    [LogicBlock(Name = "Emission Policy Demo", Icon = "device-line", Groups = new[] { PropertyGroup.Configuration, PropertyGroup.Metric, PropertyGroup.Diagnostics })]
    public class SensorBlock : LogicBlockBase
    {
        private int _ticks;

        // ── Configuration (writable — drive these to see the gate) ──────────────────

        /// <summary>
        ///     Interactive DEADBAND. Deadband-only (<c>MinInterval="0"</c>): drive it in the UI — a change below
        ///     Δ0.5 is dropped (no flash), a change of Δ0.5 or more is emitted. Badge: <c>deadband Δ0.5</c>.
        /// </summary>
        [ServiceProperty(Title = "Setpoint",
                         Unit = "kW",
                         Minimum = 0,
                         Maximum = 100,
                         MinInterval = "0",
                         MinChange = "0.5",
                         Description = "Writable, deadband-only. Drive it: a sub-Δ0.5 change is dropped (no flash); a Δ0.5-or-greater change emits.")]
        [Presentation(Group = PropertyGroup.Configuration, Decimals = 2)]
        public double Setpoint { get; set; } = 25.0;

        /// <summary>
        ///     Interactive CUSTOM-TYPE DEADBAND. A writable <see cref="ThreePhase" /> struct whose Δ0.25 deadband
        ///     resolves <see cref="ThreePhaseChangeThreshold" /> (DF-34). Nudge a phase by less than 0.25 → dropped;
        ///     by 0.25 or more → emitted. Badge: <c>deadband Δ0.25</c>.
        /// </summary>
        [ServiceProperty(Title = "Current",
                         MinInterval = "0",
                         MinChange = "0.25",
                         Description = "Writable custom struct. The Δ0.25 deadband uses a custom IChangeThreshold<ThreePhase> discovered in this assembly.")]
        [Presentation(Group = PropertyGroup.Configuration, Order = 10)]
        public ThreePhase Current { get; set; }

        // ── Metric (read-only, timer-driven — auto demos) ───────────────────────────

        /// <summary>
        ///     THROTTLE + DEADBAND. Updated every tick; emitted at most every 2 s and only on Δ0.5-or-greater moves. Badge:
        ///     <c>throttle 2s · Δ0.5</c>.
        /// </summary>
        [ServiceMeasuringPoint(Title = "Temperature",
                               Unit = "°C",
                               MinInterval = "2s",
                               MinChange = "0.5",
                               Description = "Noisy signal; throttled to 2 s and deadbanded at Δ0.5. Watch it hold and coalesce in free-run.")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double Temperature { get; private set; }

        /// <summary>THROTTLE only. Echoes Setpoint + noise each tick; emitted at most every 3 s. Badge: <c>throttle 3s</c>.</summary>
        [ServiceMeasuringPoint(Title = "Throttled echo",
                               Unit = "kW",
                               MinInterval = "3s",
                               Description = "Echoes Setpoint plus noise each tick; throttled to 3 s. Drive Setpoint and watch the echo lag.")]
        [Presentation(Group = PropertyGroup.Metric, Order = 10)]
        public double ThrottledEcho { get; private set; }

        /// <summary>
        ///     IMMEDIATE bypass. Emits on every change — the visual contrast to the throttled members. Badge:
        ///     <c>immediate</c>.
        /// </summary>
        [ServiceMeasuringPoint(Title = "Live tick",
                               Kind = MeasuringPointKind.TotalIncreasing,
                               Immediate = true,
                               Description =
                                   "Immediate=true bypasses throttle and deadband: flashes every tick. (Immediate is also the right knob for a bool safety flag, which has no magnitude to deadband.)")]
        [Presentation(Group = PropertyGroup.Metric, Order = 20)]
        public long LiveTick { get; private set; }

        /// <summary>
        ///     DUAL-ANNOTATED. One value feeds two independently-throttled streams: property 2 s; measuring-point 500 ms + Δ1
        ///     (the #104 fix). Two badges.
        /// </summary>
        [ServiceProperty(Title = "Power",
                         Unit = "W",
                         MinInterval = "2s",
                         Description = "Same value, two independently-throttled streams: the property stream (2 s) and the measuring-point stream (500 ms + Δ1).")]
        [ServiceMeasuringPoint(Title = "Power", Unit = "W", MinInterval = "500ms", MinChange = "1")]
        [Presentation(Group = PropertyGroup.Metric, Order = 30)]
        public double Power { get; private set; }

        // ── Diagnostics (read-only) ─────────────────────────────────────────────────

        /// <summary>
        ///     DEFAULT policy. No knobs → the default 250 ms throttle applies but introspection omits the badge. The
        ///     "invisible default".
        /// </summary>
        [ServiceMeasuringPoint(Title = "Sample count",
                               Kind = MeasuringPointKind.TotalIncreasing,
                               Description = "No emission knobs: the default 250 ms throttle applies, but introspection omits the badge for the default policy.")]
        [Presentation(Group = PropertyGroup.Diagnostics)]
        public long SampleCount { get; private set; }

        public SensorBlock(ILogger logger) : base(logger)
        {
        }

        /// <summary>Drives all read-only signals every virtual second so the gate has fast-moving input to act on in free-run.</summary>
        [Timer(1)]
        public void OnTick()
        {
            _ticks++;
            var t = _ticks * 0.1;

            // Slow sine + fast jitter so the 2 s throttle + Δ0.5 deadband visibly act.
            Temperature = 22.0 + Math.Sin(t) * 3.0 + Math.Sin(t * 7.0) * 0.4;

            // Echo of the driveable Setpoint plus noise; 3 s throttle.
            ThrottledEcho = Setpoint + Math.Sin(t * 3.0) * 2.0;

            // Ungated counter — emits every tick.
            LiveTick++;

            // Default-policy counter.
            SampleCount++;

            // Dual-stream power — large swing so it clears the Δ1 deadband each tick.
            Power = 1000.0 + Math.Sin(t) * 400.0 + Math.Sin(t * 5.0) * 30.0;
        }

        protected override void Ready()
        {
        }
    }
}