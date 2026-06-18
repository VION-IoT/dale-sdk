using System;
using System.Collections.Generic;
using System.Linq;

namespace Vion.Dale.DevHost.Scenarios
{
    /// <summary>
    ///     The structured result of a scenario run — what the Player renders, what
    ///     <c>GET /api/scenarios/{id}/run</c> serves, and what the copy-verification-report block is built
    ///     from (RFC 0006). Mutated in place by the runner as the run progresses (step lists are created
    ///     up front and fixed-size; only fields transition), so a registry can serve live snapshots.
    /// </summary>
    public sealed class ScenarioRunReport
    {
        public string RunId { get; set; } = string.Empty;

        public string ScenarioId { get; set; } = string.Empty;

        public string? Title { get; set; }

        /// <summary>The topology id the scenario declared.</summary>
        public string? Topology { get; set; }

        /// <summary>The topology name the host declared (null when the preset declares none).</summary>
        public string? HostTopology { get; set; }

        /// <summary>
        ///     Git blob hash of the scenario file as run (when the caller had the file bytes) — pins the
        ///     verification report to an exact file version (RFC 0006 "Copy verification report").
        /// </summary>
        public string? FileHash { get; set; }

        /// <summary>One of <see cref="ScenarioRunStatus" />.</summary>
        public string Status { get; set; } = ScenarioRunStatus.Running;

        public DateTimeOffset StartedAt { get; set; }

        public double? ElapsedSeconds { get; set; }

        /// <summary>Validation failures (unresolvable name paths, topology mismatch detail) — empty on a clean run.</summary>
        public IReadOnlyList<string> ValidationErrors { get; set; } = Array.Empty<string>();

        public IReadOnlyList<ScenarioStepResult> Setup { get; set; } = Array.Empty<ScenarioStepResult>();

        public IReadOnlyList<ScenarioStepResult> Steps { get; set; } = Array.Empty<ScenarioStepResult>();

        /// <summary>Judgment items, always reported <c>requiresHuman</c> — v1 never auto-fails a judgment.</summary>
        public IReadOnlyList<ScenarioJudgmentResult> Judge { get; set; } = Array.Empty<ScenarioJudgmentResult>();

        /// <summary>
        ///     A timeseries of the <c>watch</c> name paths' values, sampled at each step boundary (and once at
        ///     the start, after setup) — the forensic trail of what the watched signals did across the run, for
        ///     report-diffing and judge-assist diagnosis. Empty when the scenario declares no <c>watch</c>.
        ///     Deterministic on a stepped host (the values and <see cref="WatchSample.VirtualElapsedMs" /> are
        ///     reproducible run-to-run); it is observability, not an assertion target (RFC 0008 §11.7).
        /// </summary>
        public IReadOnlyList<WatchSample> WatchTrace { get; set; } = Array.Empty<WatchSample>();

        /// <summary>
        ///     A consistent deep copy — the runner mutates its live instance on its own thread, so anything
        ///     that serializes a report concurrently (the web run registry) stores snapshots instead.
        /// </summary>
        public ScenarioRunReport Snapshot()
        {
            ScenarioStepResult Copy(ScenarioStepResult s)
            {
                return new ScenarioStepResult
                       {
                           Index = s.Index,
                           Kind = s.Kind,
                           Label = s.Label,
                           Spec = s.Spec,
                           Target = s.Target,
                           Argument = s.Argument,
                           Status = s.Status,
                           ElapsedMs = s.ElapsedMs,
                           VirtualElapsedMs = s.VirtualElapsedMs,
                           Detail = s.Detail,
                       };
            }

            return new ScenarioRunReport
                   {
                       RunId = RunId,
                       ScenarioId = ScenarioId,
                       Title = Title,
                       Topology = Topology,
                       HostTopology = HostTopology,
                       FileHash = FileHash,
                       Status = Status,
                       StartedAt = StartedAt,
                       ElapsedSeconds = ElapsedSeconds,
                       ValidationErrors = ValidationErrors.ToList(),
                       Setup = Setup.Select(Copy).ToList(),
                       Steps = Steps.Select(Copy).ToList(),
                       Judge = Judge.Select(j => new ScenarioJudgmentResult { Text = j.Text, Spec = j.Spec, Status = j.Status }).ToList(),
                       WatchTrace = WatchTrace.Select(s => new WatchSample
                                                           {
                                                               Phase = s.Phase,
                                                               StepIndex = s.StepIndex,
                                                               VirtualElapsedMs = s.VirtualElapsedMs,
                                                               Values = new Dictionary<string, object?>(s.Values),
                                                           })
                                              .ToList(),
                   };
        }
    }

    /// <summary>Run-level status values (string-typed on the wire).</summary>
    public static class ScenarioRunStatus
    {
        public const string Running = "running";

        public const string Succeeded = "succeeded";

        public const string Failed = "failed";

        public const string TopologyMismatch = "topologyMismatch";

        public const string Canceled = "canceled";
    }

    /// <summary>One setup entry's or step's progress within a run.</summary>
    public sealed class ScenarioStepResult
    {
        public int Index { get; set; }

        /// <summary>set / digitalInput / analogInput / waitUntil / wait.</summary>
        public string Kind { get; set; } = string.Empty;

        public string? Label { get; set; }

        public string? Spec { get; set; }

        /// <summary>What the step addresses — a name path, a contract reference, or a duration.</summary>
        public string Target { get; set; } = string.Empty;

        /// <summary>
        ///     The step's payload, straight from the file: the JSON value a <c>set</c>/input writes, or a
        ///     <c>waitUntil</c>'s condition ("&gt; 0 · 30 s timeout") — so reports and the Player show WHAT
        ///     ran, not just where.
        /// </summary>
        public string? Argument { get; set; }

        /// <summary>One of <see cref="ScenarioStepStatus" />.</summary>
        public string Status { get; set; } = ScenarioStepStatus.Pending;

        /// <summary>
        ///     Wall-clock duration of the step (real <see cref="System.Diagnostics.Stopwatch" />) — instrumentation; varies
        ///     with thread jitter, not reproducible.
        /// </summary>
        public double? ElapsedMs { get; set; }

        /// <summary>
        ///     Virtual-time duration of the step on a stepped host (<c>control.VirtualTimeUtc</c> before/after),
        ///     or <c>null</c> on a real-clock host. Unlike <see cref="ElapsedMs" /> this is deterministic, so a
        ///     stepped run report is bit-reproducible across runs — the prerequisite for trace-diff regression
        ///     and judge-assist forensics (RFC 0008 / DF-20).
        /// </summary>
        public double? VirtualElapsedMs { get; set; }

        public string? Detail { get; set; }
    }

    /// <summary>
    ///     One sample in the <see cref="ScenarioRunReport.WatchTrace" /> timeseries — the values of the
    ///     scenario's <c>watch</c> name paths at a step boundary.
    /// </summary>
    public sealed class WatchSample
    {
        /// <summary>Where in the run this was sampled: <c>start</c> (after setup, before steps) or <c>steps</c>.</summary>
        public string Phase { get; set; } = string.Empty;

        /// <summary>The <c>steps</c> index this sample follows, or <c>-1</c> for the start-of-run sample.</summary>
        public int StepIndex { get; set; }

        /// <summary>Virtual time elapsed since the run started, in ms — deterministic on a stepped host, null on a real clock.</summary>
        public double? VirtualElapsedMs { get; set; }

        /// <summary>The watched name path → its value at this sample (struct field paths resolved to their scalar leaf).</summary>
        public Dictionary<string, object?> Values { get; set; } = new();
    }

    /// <summary>Step-level status values (string-typed on the wire).</summary>
    public static class ScenarioStepStatus
    {
        public const string Pending = "pending";

        public const string Running = "running";

        public const string Ok = "ok";

        public const string Failed = "failed";

        public const string Skipped = "skipped";
    }

    /// <summary>A judgment item in the report — carries its spec id; the verdict belongs to the human.</summary>
    public sealed class ScenarioJudgmentResult
    {
        public string Text { get; set; } = string.Empty;

        public string? Spec { get; set; }

        public string Status { get; set; } = "requiresHuman";
    }

    /// <summary>
    ///     A scenario run failed — thrown by
    ///     <see
    ///         cref="ScenarioRunner.ApplyAsync(ScenarioFile, Control.IDevHostControl, ScenarioRunOptions?, System.Threading.CancellationToken)" />
    ///     so C# tests fail loudly; the report carries the detail.
    /// </summary>
    public sealed class ScenarioRunException : Exception
    {
        public ScenarioRunReport Report { get; }

        public ScenarioRunException(ScenarioRunReport report, string message) : base(message)
        {
            Report = report;
        }
    }
}