using System;
using System.Collections.Immutable;
using Vion.Dale.Sdk.Core;

namespace Vion.Diagnostics
{
    /// <summary>
    ///     One row of the diagnostics table — a logic block's vitals. Exposed as an
    ///     <c>ImmutableArray&lt;LogicBlockVitals&gt;</c> service property and dashboard-rendered as a
    ///     <c>&lt;StructArray&gt;</c> table (RFC 0005 Sink 2). <c>LastActivityUtc</c> is null when the block
    ///     has never handled a message. <c>Restarts</c> is intentionally omitted — Proto lifecycle counters
    ///     are an edge-gateway-health concern, not surfaced here.
    /// </summary>
    public readonly record struct LogicBlockVitals(
        [StructField(Description = "Logic-block instance name.")]
        string LogicBlockName,
        [StructField(Unit = "/s", Description = "Messages handled per second (windowed rate).")]
        double MessageRatePerSec,
        [StructField(Description = "Maximum handler duration over the recent window.")]
        TimeSpan HandlerDurationMax,
        [StructField(Description = "Current mailbox depth (messages posted minus received).")]
        int MailboxDepth,
        [StructField(Description = "Handler exceptions since start.")]
        int Errors,
        [StructField(Description = "Time of the last handled message; null if the block has never been active.")]
        DateTime? LastActivityUtc,
        [StructField(Description = "Per-block health (OK / Warning / Critical).")]
        LogicBlockHealth Health);

    /// <summary>
    ///     The runtime ingress/egress choke-points — the shared <c>MqttClient</c> and the single
    ///     property / measuring-point publisher actors (N blocks -> 1). <c>MqttConnected</c> is
    ///     intentionally omitted (edge-gateway-health concern).
    /// </summary>
    public readonly record struct RuntimeHealth(
        int MqttIngressBacklog,
        int PublisherBacklog,
        double PublishErrorsPerSec);

    /// <summary>Per-logic-block health; the <see cref="SeverityAttribute" /> drives the per-row status colour.</summary>
    public enum LogicBlockHealth
    {
        [Severity(StatusSeverity.Success)]
        [EnumLabel("OK")]
        Ok,

        [Severity(StatusSeverity.Warning)]
        [EnumLabel("Warning")]
        Warning,

        [Severity(StatusSeverity.Error)]
        [EnumLabel("Critical")]
        Critical,
    }

    /// <summary>The overall diagnostics pill; the <see cref="SeverityAttribute" /> drives its colour.</summary>
    public enum DiagnosticsStatus
    {
        [Severity(StatusSeverity.Success)]
        [EnumLabel("Healthy")]
        Healthy,

        [Severity(StatusSeverity.Warning)]
        [EnumLabel("Degraded")]
        Degraded,

        [Severity(StatusSeverity.Error)]
        [EnumLabel("Overloaded")]
        Overloaded,
    }

    /// <summary>
    ///     Severity thresholds. Runtime-tunable per gateway (the block exposes them as
    ///     <c>[ServiceProperty]</c>s); the defaults are starting points to be tuned with real data.
    /// </summary>
    public readonly record struct DiagnosticsThresholds(
        int WarnMailboxDepth,
        int CriticalMailboxDepth,
        TimeSpan WarnHandlerDuration,
        TimeSpan CriticalHandlerDuration)
    {
        public static DiagnosticsThresholds Default { get; } =
            new(WarnMailboxDepth: 50,
                CriticalMailboxDepth: 500,
                WarnHandlerDuration: TimeSpan.FromMilliseconds(100),
                CriticalHandlerDuration: TimeSpan.FromSeconds(1));
    }

    /// <summary>The full projection the diagnostics block republishes on each tick.</summary>
    public readonly record struct DiagnosticsResult(
        ImmutableArray<LogicBlockVitals> LogicBlocks,
        RuntimeHealth RuntimeHealth,
        DiagnosticsStatus Status);
}
