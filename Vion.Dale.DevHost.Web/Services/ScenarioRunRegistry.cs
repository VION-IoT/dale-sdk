using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Web.Services
{
    /// <summary>
    ///     Server-side scenario run state (RFC 0006 "Run identity &amp; concurrency"): one active run per
    ///     host — two scenarios interleaving sets on one shared network is semantically incoherent, so a
    ///     second apply conflicts instead; <c>restart</c> cancels the in-flight run (including its pending
    ///     waits) and starts the new one. Latest reports are kept per scenario id so the Player is F5-safe
    ///     and agents can poll; <c>runId</c> changes let pollers detect restarts (no ABA).
    /// </summary>
    public sealed class ScenarioRunRegistry
    {
        private readonly object _gate = new();

        private readonly Dictionary<string, ScenarioRunReport> _latest = new(StringComparer.Ordinal);

        private ActiveRun? _active;

        /// <summary>
        ///     True while a scenario run is in flight. Manual stepping (RFC 0008 §Part 4) must not drive the
        ///     clock while a run does — the two would race on the shared virtual schedule.
        /// </summary>
        public bool HasActiveRun
        {
            get
            {
                lock (_gate)
                {
                    return _active is { } active && !active.Task.IsCompleted;
                }
            }
        }

        /// <summary>The latest run report for a scenario id, or null when it never ran this host generation.</summary>
        public ScenarioRunReport? Latest(string scenarioId)
        {
            lock (_gate)
            {
                return _latest.TryGetValue(scenarioId, out var report) ? report : null;
            }
        }

        /// <summary>
        ///     Start a run. Refuses (conflict) while another run is active unless <paramref name="restart" />,
        ///     which cancels the active run and awaits its teardown first.
        /// </summary>
        public async Task<ScenarioApplyResult> ApplyAsync(ScenarioFile scenario, IDevHostControl control, bool restart, bool force, string? fileHash = null)
        {
            ActiveRun? toCancel;
            lock (_gate)
            {
                toCancel = _active is { } active && !active.Task.IsCompleted ? _active : null;
                if (toCancel is not null && !restart)
                {
                    return ScenarioApplyResult.Conflict(toCancel.RunId, toCancel.ScenarioId);
                }
            }

            if (toCancel is not null)
            {
                toCancel.Cancellation.Cancel();
                try
                {
                    await toCancel.Task.ConfigureAwait(false);
                }
                catch
                {
                    // The canceled run's failure shape doesn't matter here; its report records it.
                }
            }

            var runId = Guid.NewGuid().ToString("N");
            var cancellation = new CancellationTokenSource();
            var options = new ScenarioRunOptions
                          {
                              RunId = runId,
                              IgnoreTopologyMismatch = force,
                              FileHash = fileHash,
                              OnProgress = report => Publish(scenario.Id!, report),
                          };

            lock (_gate)
            {
                if (_active is { } stillActive && !stillActive.Task.IsCompleted)
                {
                    // A concurrent apply won the race while the old run was torn down.
                    return ScenarioApplyResult.Conflict(stillActive.RunId, stillActive.ScenarioId);
                }

                // The CTS is deliberately NOT disposed from a completion continuation: Cancel() on a
                // disposed CTS throws, and a restart can race the run's natural completion (the
                // check-then-cancel above runs outside the lock). A timer-less CTS holds no unmanaged
                // state — GC handles it.
                var task = Task.Run(() => ScenarioRunner.RunAsync(scenario, control, options, cancellation.Token));
                _active = new ActiveRun(runId, scenario.Id!, cancellation, task);

                // Publish a pending report under the SAME lock, before the 202 leaves: a poller that
                // reads Latest() right after apply must see the new runId, never the previous run's
                // terminal report. The runner's first progress callback replaces it within moments.
                _latest[scenario.Id!] = new ScenarioRunReport
                                        {
                                            RunId = runId,
                                            ScenarioId = scenario.Id!,
                                            Title = scenario.Title,
                                            Topology = scenario.Topology,
                                            FileHash = fileHash,
                                            Status = ScenarioRunStatus.Running,
                                            StartedAt = DateTimeOffset.UtcNow,
                                        };
            }

            return ScenarioApplyResult.Started(runId);
        }

        /// <summary>
        ///     Cancel the active run, if any — called on host teardown so a run never keeps executing
        ///     against a disposed generation. Does not wait for the run to observe the cancellation.
        /// </summary>
        public void Shutdown()
        {
            lock (_gate)
            {
                if (_active is { } active && !active.Task.IsCompleted)
                {
                    active.Cancellation.Cancel();
                }
            }
        }

        private void Publish(string scenarioId, ScenarioRunReport report)
        {
            // Store a snapshot: the runner keeps mutating its live report instance on its own thread,
            // and the controller serializes whatever Latest() returns — a clone per transition keeps
            // every served JSON internally consistent.
            var snapshot = report.Snapshot();
            lock (_gate)
            {
                _latest[scenarioId] = snapshot;
            }
        }

        private sealed record ActiveRun(string RunId, string ScenarioId, CancellationTokenSource Cancellation, Task<ScenarioRunReport> Task);
    }

    /// <summary>Outcome of an apply request: started with a run id, or refused because a run is active.</summary>
    public sealed class ScenarioApplyResult
    {
        public bool IsConflict { get; private init; }

        public string RunId { get; private init; } = string.Empty;

        public string? ActiveScenarioId { get; private init; }

        public static ScenarioApplyResult Started(string runId)
        {
            return new ScenarioApplyResult { RunId = runId };
        }

        public static ScenarioApplyResult Conflict(string activeRunId, string activeScenarioId)
        {
            return new ScenarioApplyResult { IsConflict = true, RunId = activeRunId, ActiveScenarioId = activeScenarioId };
        }
    }
}