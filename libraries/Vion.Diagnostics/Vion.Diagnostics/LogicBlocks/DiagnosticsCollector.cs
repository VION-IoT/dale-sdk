using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Diagnostics.LogicBlocks
{
    /// <summary>
    ///     RFC 0005 Sink 2 — the runtime-diagnostics block. On its <c>[Timer]</c> it snapshots the SDK
    ///     vitals core (<see cref="IRuntimeDiagnostics" />), projects it (<see cref="DiagnosticsProjection" />)
    ///     into a per-logic-block table + a runtime-health rollup + a status pill, and republishes on
    ///     Plane B. One array-valued property per gateway (one retained message) — no Mimir cardinality cost.
    /// </summary>
    [LogicBlock(Name = "Runtime Diagnostics",
                Icon = "pulse-line",
                Groups = new[] { PropertyGroup.Status, PropertyGroup.Diagnostics, PropertyGroup.Configuration })]
    public class DiagnosticsCollector : LogicBlockBase
    {
        private readonly IRuntimeDiagnostics _diagnostics;
        private readonly TimeProvider _timeProvider;

        private IReadOnlyList<ActorVitals> _priorSnapshot = Array.Empty<ActorVitals>();
        private long _lastTimestamp;

        public DiagnosticsCollector(IRuntimeDiagnostics diagnostics, TimeProvider timeProvider, ILogger logger)
            : base(logger)
        {
            _diagnostics = diagnostics;
            _timeProvider = timeProvider;
        }

        // ── Status ────────────────────────────────────────────────────────────────
        [ServiceProperty(Description = "Overall runtime-diagnostics status across all matched logic blocks.")]
        [Presentation(DisplayName = "Status", Group = PropertyGroup.Status, StatusIndicator = true, Importance = Importance.Primary)]
        public DiagnosticsStatus Status { get; private set; }

        // ── Diagnostics ─────────────────────────────────────────────────────────────
        [ServiceProperty(Description = "Per-logic-block vitals — one row per matched block; renders as a table.")]
        [Presentation(DisplayName = "Logic blocks", Group = PropertyGroup.Diagnostics, Importance = Importance.Primary)]
        public ImmutableArray<LogicBlockVitals> LogicBlocks { get; private set; } = ImmutableArray<LogicBlockVitals>.Empty;

        [ServiceProperty(Description = "Runtime ingress/egress choke-points (the shared MQTT client + publisher actors).")]
        [Presentation(DisplayName = "Runtime health", Group = PropertyGroup.Diagnostics)]
        public RuntimeHealth RuntimeHealth { get; private set; }

        [ServiceMeasuringPoint(Description = "Max handler duration across matched logic blocks (trend).")]
        [Presentation(DisplayName = "Max handler", Group = PropertyGroup.Diagnostics)]
        public TimeSpan MaxHandlerDuration { get; private set; }

        [ServiceMeasuringPoint(Description = "Max mailbox depth across matched logic blocks (trend).")]
        [Presentation(DisplayName = "Max mailbox", Group = PropertyGroup.Diagnostics)]
        public int MaxMailboxDepth { get; private set; }

        // ── Configuration (runtime-tunable) ─────────────────────────────────────────
        [ServiceProperty(Description = "Regex matched against logic-block names; empty = watch all.")]
        [Presentation(DisplayName = "Filter", Group = PropertyGroup.Configuration)]
        public string Filter { get; set; } = string.Empty;

        [ServiceProperty(Description = "Mailbox depth at which a block is marked Warning.")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public int WarnMailboxDepth { get; set; } = 50;

        [ServiceProperty(Description = "Mailbox depth at which a block is marked Critical.")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public int CriticalMailboxDepth { get; set; } = 500;

        [ServiceProperty(Description = "Handler duration at which a block is marked Warning.")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public TimeSpan WarnHandlerDuration { get; set; } = TimeSpan.FromMilliseconds(100);

        [ServiceProperty(Description = "Handler duration at which a block is marked Critical.")]
        [Presentation(Group = PropertyGroup.Configuration)]
        public TimeSpan CriticalHandlerDuration { get; set; } = TimeSpan.FromSeconds(1);

        /// <summary>Samples + republishes once a minute (RFC 0005: Plane B cadence, not per-change).</summary>
        [Timer(60)]
        public void Collect()
        {
            var now = _timeProvider.GetTimestamp();
            var elapsed = _lastTimestamp == 0 ? TimeSpan.Zero : _timeProvider.GetElapsedTime(_lastTimestamp, now);
            _lastTimestamp = now;

            var current = _diagnostics.Snapshot();
            var thresholds = new DiagnosticsThresholds(WarnMailboxDepth, CriticalMailboxDepth, WarnHandlerDuration, CriticalHandlerDuration);
            var result = DiagnosticsProjection.Project(
                _priorSnapshot,
                current,
                elapsed,
                string.IsNullOrWhiteSpace(Filter) ? null : Filter,
                thresholds);
            _priorSnapshot = current;

            LogicBlocks = result.LogicBlocks;
            RuntimeHealth = result.RuntimeHealth;
            Status = result.Status;
            MaxHandlerDuration = result.LogicBlocks.IsDefaultOrEmpty ? TimeSpan.Zero : result.LogicBlocks.Max(b => b.HandlerDurationMax);
            MaxMailboxDepth = result.LogicBlocks.IsDefaultOrEmpty ? 0 : result.LogicBlocks.Max(b => b.MailboxDepth);
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }
}
