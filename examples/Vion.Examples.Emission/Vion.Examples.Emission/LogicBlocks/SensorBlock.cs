using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Emission.LogicBlocks
{
    /// <summary>
    ///     Showcases the RFC 0004 emission policy — throttle (<c>MinInterval</c>), deadband (<c>MinChange</c>),
    ///     <c>Immediate</c> bypass, the always-on value-equality dedup floor, independent per-stream throttling
    ///     on a dual-annotated member, a custom <see cref="ThreePhase" /> deadband, and the invisible 250 ms
    ///     default.
    ///     <para />
    ///     Emission policy governs the <b>outbound</b> direction — how the block re-publishes its OWN measured
    ///     state — so every throttled/deadbanded member here is a <b>read-only</b> reading. A write to the
    ///     writable <see cref="Setpoint" /> is always forwarded; it is a plain operator input with no policy.
    ///     <see cref="Reading" /> and <see cref="PhaseCurrents" /> track the setpoint, so driving
    ///     <see cref="Setpoint" /> (which updates instantly) and watching them hold until the change clears the
    ///     deadband shows both halves: the write is forwarded, the re-emission is gated.
    ///     <para />
    ///     The policy is active only on a real clock — in DevHost that means FREE-RUN mode. Under the
    ///     deterministic / stepped clock it is force-disabled (so scenarios stay exact), and the TestKit re-enables
    ///     it explicitly via <c>WithEmissionPolicy(FromAttributes)</c>. See the README.
    /// </summary>
    [LogicBlock(Name = "Emission Policy Demo", Icon = "device-line", Groups = new[] { PropertyGroup.Configuration, PropertyGroup.Metric, PropertyGroup.Diagnostics })]
    public class SensorBlock : LogicBlockBase
    {
        private int _ticks;

        // ── Operator input (writable, PLAIN — always forwarded) ─────────────────────

        /// <summary>
        ///     The operator target — a plain writable input with NO emission policy. A write is always forwarded
        ///     to the block immediately; the emission policy governs only how the block re-publishes its measured
        ///     readings (below), not inbound writes. Drive this and watch <see cref="Reading" /> /
        ///     <see cref="PhaseCurrents" /> react under their deadbands.
        /// </summary>
        [ServiceProperty(Title = "Setpoint",
                         Unit = "kW",
                         Minimum = 0,
                         Maximum = 100,
                         Description =
                             "Operator target — a plain writable input. Writes are always forwarded; the deadbanded readings below are what the emission policy governs.")]
        [Presentation(Group = PropertyGroup.Configuration, Decimals = 2)]
        public double Setpoint { get; set; } = 25.0;

        // ── Measured readings that track the setpoint (read-only — deadband on emission) ──

        /// <summary>
        ///     DEADBAND. A measured reading that tracks the setpoint; the block re-emits it only when it moves by
        ///     at least Δ0.5. Drive <see cref="Setpoint" /> by less than 0.5 and this read-only chip holds — the
        ///     sub-threshold change is suppressed on the wire — then jumps once a change clears Δ0.5. Badge:
        ///     <c>deadband Δ0.5</c>.
        /// </summary>
        [ServiceMeasuringPoint(Title = "Reading",
                               Unit = "kW",
                               MinInterval = "0",
                               MinChange = "0.5",
                               Description = "Measured reading tracking the setpoint; re-emitted only when it moves by at least Δ0.5. Drive Setpoint by < 0.5 and this holds.")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double Reading { get; private set; }

        /// <summary>
        ///     CUSTOM-TYPE DEADBAND. Per-phase currents derived from the setpoint, whose Δ0.25 deadband resolves
        ///     a custom <see cref="ThreePhaseChangeThreshold" /> (<c>IChangeThreshold&lt;ThreePhase&gt;</c>)
        ///     discovered in this assembly (DF-34). Badge: <c>deadband Δ0.25</c>.
        /// </summary>
        [ServiceMeasuringPoint(Title = "Phase currents",
                               MinInterval = "0",
                               MinChange = "0.25",
                               Description =
                                   "Per-phase currents derived from the setpoint; the Δ0.25 deadband uses a custom IChangeThreshold<ThreePhase> discovered in this assembly.")]
        [Presentation(Group = PropertyGroup.Metric)]
        public ThreePhase PhaseCurrents { get; private set; }

        // ── Independently sensed signals (read-only, timer-driven — the auto throttle demo) ──

        /// <summary>
        ///     THROTTLE + DEADBAND. A sensed temperature that wobbles on its own; emitted at most every 2 s and only on
        ///     Δ0.5-or-greater moves. Badge: <c>throttle 2s · Δ0.5</c>.
        /// </summary>
        [ServiceMeasuringPoint(Title = "Temperature",
                               Unit = "°C",
                               MinInterval = "2s",
                               MinChange = "0.5",
                               Description = "Sensed temperature; throttled to 2 s and deadbanded at Δ0.5. Watch it hold and coalesce in free-run.")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double Temperature { get; private set; }

        /// <summary>
        ///     DUAL-ANNOTATED. One sensed power value feeds two independently-throttled streams: the property
        ///     stream (2 s) and the measuring-point stream (500 ms + Δ1) — the #104 fix. Two badges.
        /// </summary>
        [ServiceProperty(Title = "Power",
                         Unit = "W",
                         MinInterval = "2s",
                         Description = "Same sensed value, two independently-throttled streams: the property stream (2 s) and the measuring-point stream (500 ms + Δ1).")]
        [ServiceMeasuringPoint(Title = "Power", Unit = "W", MinInterval = "500ms", MinChange = "1")]
        [Presentation(Group = PropertyGroup.Metric)]
        public double Power { get; private set; }

        /// <summary>
        ///     IMMEDIATE bypass. Emits on every change — the visual contrast to the throttled members. Badge:
        ///     <c>immediate</c>.
        /// </summary>
        [ServiceMeasuringPoint(Title = "Live tick",
                               Kind = MeasuringPointKind.TotalIncreasing,
                               Immediate = true,
                               Description =
                                   "Immediate=true bypasses throttle and deadband: flashes every tick. (Immediate is also the right knob for a bool safety flag, which has no magnitude to deadband.)")]
        [Presentation(Group = PropertyGroup.Metric)]
        public long LiveTick { get; private set; }

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

        /// <summary>
        ///     Recomputes the readings every virtual second. The setpoint-tracking readings (<see cref="Reading" />,
        ///     <see cref="PhaseCurrents" />) follow the operator input; the sensed signals
        ///     (<see cref="Temperature" />, <see cref="Power" />) wobble on their own so the throttle is visible
        ///     without driving anything.
        /// </summary>
        [Timer(1)]
        public void OnTick()
        {
            _ticks++;
            var t = _ticks * 0.1;

            // Readings that track the operator setpoint — re-published under their deadband policy.
            Reading = Setpoint;
            PhaseCurrents = new ThreePhase(Setpoint, Setpoint, Setpoint);

            // Independently sensed signals that move on their own — the auto throttle demo.
            Temperature = 22.0 + Math.Sin(t) * 3.0 + Math.Sin(t * 7.0) * 0.4;
            Power = 1000.0 + Math.Sin(t) * 400.0 + Math.Sin(t * 5.0) * 30.0;

            LiveTick++;
            SampleCount++;
        }

        protected override void Ready()
        {
        }
    }
}