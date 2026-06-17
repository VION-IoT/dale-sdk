using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost.Scenarios
{
    /// <summary>Options for a scenario run.</summary>
    public sealed class ScenarioRunOptions
    {
        /// <summary>
        ///     Run despite a topology mismatch (the Player's "proceed anyway"). Default false: a mismatch
        ///     blocks before anything executes.
        /// </summary>
        public bool IgnoreTopologyMismatch { get; init; }

        /// <summary>Run identity surfaced in the report; generated when null.</summary>
        public string? RunId { get; init; }

        /// <summary>Git blob hash of the scenario file as run, when the caller has it (see <see cref="ScenarioStore.FileHash" />).</summary>
        public string? FileHash { get; init; }

        /// <summary>
        ///     Invoked after every report transition (step started / finished, run finished) — the web run
        ///     registry uses this to expose live progress. The same mutable report instance is passed each time.
        /// </summary>
        public Action<ScenarioRunReport>? OnProgress { get; init; }
    }

    /// <summary>
    ///     The one scenario interpreter (RFC 0006): a thin sequential executor over existing
    ///     <see cref="IDevHostControl" /> members — validate (topology match, every name path resolves) →
    ///     setup in file order (acked) → steps in order → report. There is no other evaluator: what the
    ///     Player shows, CI ran, byte for byte.
    /// </summary>
    public static class ScenarioRunner
    {
        // SetPropertyAsync acks on the write's own round-trip response, with a fixed 5 s safety timeout.
        // Applied writes — including no-op sets — ack promptly; an ack that consumed the whole window
        // means the block never replied: an actor-side rejection that was swallowed (the hollow-ack
        // gotcha) or a lost message.
        private const double AckCeilingMs = 4900;

        private const double DefaultWaitUntilTimeoutSeconds = 20;

        // Default virtual-time budget for a settle step when maxSeconds is not specified.
        private const double DefaultSettleMaxSeconds = 60;

        /// <summary>Run a scenario and return the structured report. Failures are recorded, not thrown.</summary>
        public static async Task<ScenarioRunReport> RunAsync(ScenarioFile scenario,
                                                             IDevHostControl control,
                                                             ScenarioRunOptions? options = null,
                                                             CancellationToken cancellationToken = default)
        {
            options ??= new ScenarioRunOptions();

            // Re-validate whatever we were handed: parsed files pass trivially; a malformed
            // hand-constructed instance must fail loudly, not NRE mid-run.
            scenario.EnsureStructurallyValid();
            var report = BuildReport(scenario, options);
            var progress = options.OnProgress ?? (_ => { });
            var stopwatch = Stopwatch.StartNew();
            progress(report);

            try
            {
                await ExecuteAsync(scenario,
                                   control,
                                   options,
                                   report,
                                   progress,
                                   cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                report.Status = ScenarioRunStatus.Canceled;
                SkipRemaining(report, "run canceled");
            }

            report.ElapsedSeconds = stopwatch.Elapsed.TotalSeconds;
            progress(report);
            return report;
        }

        /// <summary>Run a scenario by id from a scenarios directory (default: <c>{cwd}/scenarios</c>).</summary>
        public static Task<ScenarioRunReport> RunAsync(string id,
                                                       IDevHostControl control,
                                                       string? scenariosDir = null,
                                                       ScenarioRunOptions? options = null,
                                                       CancellationToken cancellationToken = default)
        {
            var store = new ScenarioStore(scenariosDir);
            return RunAsync(store.LoadFile(id), control, WithFileHash(options, store, id), cancellationToken);
        }

        /// <summary>
        ///     The composition entry point (RFC 0006 "Composition rule"): validate + setup + steps, throwing
        ///     <see cref="ScenarioRunException" /> on any failure — a C# test uses this as its
        ///     arrange/stimulate phase and adds arbitrary assertions on top.
        /// </summary>
        public static async Task ApplyAsync(ScenarioFile scenario, IDevHostControl control, ScenarioRunOptions? options = null, CancellationToken cancellationToken = default)
        {
            var report = await RunAsync(scenario, control, options, cancellationToken).ConfigureAwait(false);
            if (report.Status != ScenarioRunStatus.Succeeded)
            {
                var failure = report.Setup.Concat(report.Steps).FirstOrDefault(s => s.Status == ScenarioStepStatus.Failed);
                var detail = failure is null ? string.Join("; ", report.ValidationErrors) : $"step {failure.Index} ({failure.Label ?? failure.Target}): {failure.Detail}";
                throw new ScenarioRunException(report, $"Scenario '{scenario.Id}' did not apply ({report.Status}): {detail}");
            }
        }

        /// <summary>Apply a scenario by id from a scenarios directory (default: <c>{cwd}/scenarios</c>).</summary>
        public static Task ApplyAsync(string id,
                                      IDevHostControl control,
                                      string? scenariosDir = null,
                                      ScenarioRunOptions? options = null,
                                      CancellationToken cancellationToken = default)
        {
            var store = new ScenarioStore(scenariosDir);
            return ApplyAsync(store.LoadFile(id), control, WithFileHash(options, store, id), cancellationToken);
        }

        // Human-readable waitUntil condition for reports ("> 0 · 30 s timeout") — what the step waits
        // FOR, alongside the target that says where.
        private static string DescribeCondition(ScenarioStep step)
        {
            var condition = step.WaitUntil!;
            var timeout = step.TimeoutSeconds ?? DefaultWaitUntilTimeoutSeconds;
            var comparator = DescribeComparator(condition.Above,
                                                condition.Below,
                                                condition.EqualTo,
                                                condition.NotEquals,
                                                condition.OneOf,
                                                condition.Tolerance);
            return $"{comparator} · {timeout.ToString(CultureInfo.InvariantCulture)} s timeout";
        }

        // Human-readable expect assertion for reports ("> 0", "one of [a, b]", "== {RefBlock.Limit}").
        private static string DescribeExpect(ScenarioExpect expect)
        {
            return DescribeComparator(expect.Above,
                                      expect.Below,
                                      expect.EqualTo,
                                      expect.NotEquals,
                                      expect.OneOf,
                                      expect.Tolerance);
        }

        // The comparator description shared by waitUntil and expect, including oneOf and {path} comparands.
        private static string DescribeComparator(JsonElement above,
                                                 JsonElement below,
                                                 JsonElement equalTo,
                                                 JsonElement notEquals,
                                                 JsonElement oneOf,
                                                 double? tolerance)
        {
            if (above.ValueKind != JsonValueKind.Undefined)
            {
                return $"> {DescribeComparand(above)}";
            }

            if (below.ValueKind != JsonValueKind.Undefined)
            {
                return $"< {DescribeComparand(below)}";
            }

            if (equalTo.ValueKind != JsonValueKind.Undefined)
            {
                return $"== {DescribeComparand(equalTo)}" + (tolerance is { } t ? $" ±{t.ToString(CultureInfo.InvariantCulture)}" : "");
            }

            if (notEquals.ValueKind != JsonValueKind.Undefined)
            {
                return $"!= {DescribeComparand(notEquals)}";
            }

            return $"one of [{string.Join(", ", oneOf.EnumerateArray().Select(e => e.GetRawText()))}]";
        }

        // A comparand renders as its literal, or as {Block.Prop} for the relational {path} form.
        private static string DescribeComparand(JsonElement comparand)
        {
            return ScenarioComparators.IsPathComparand(comparand) ? $"{{{comparand.GetProperty("path").GetString()}}}" : comparand.GetRawText();
        }

        private static ScenarioRunOptions WithFileHash(ScenarioRunOptions? options, ScenarioStore store, string id)
        {
            if (options?.FileHash is not null)
            {
                return options;
            }

            return new ScenarioRunOptions
                   {
                       RunId = options?.RunId,
                       IgnoreTopologyMismatch = options?.IgnoreTopologyMismatch ?? false,
                       OnProgress = options?.OnProgress,
                       FileHash = store.FileHash(id),
                   };
        }

        private static ScenarioRunReport BuildReport(ScenarioFile scenario, ScenarioRunOptions options)
        {
            ScenarioStepResult Describe(ScenarioStep step, int index)
            {
                return new ScenarioStepResult
                       {
                           Index = index,
                           Kind = step.Kind,
                           Label = step.Label,
                           Spec = step.Spec,
                           Target = step.Kind switch
                           {
                               "set" => step.Set!,
                               "digitalInput" => $"{step.DigitalInput!.Block}.{step.DigitalInput.Contract}",
                               "analogInput" => $"{step.AnalogInput!.Block}.{step.AnalogInput.Contract}",
                               "waitUntil" => step.WaitUntil!.Property ?? string.Empty,
                               "expect" => step.Expect!.Property ?? string.Empty,
                               "advance" => string.Empty,
                               "settle" => "until stable",
                               _ => $"{step.Wait!.Seconds.ToString(CultureInfo.InvariantCulture)} s",
                           },
                           Argument = step.Kind switch
                           {
                               "set" or "digitalInput" or "analogInput" => step.Value.ValueKind == JsonValueKind.Undefined ? null : step.Value.GetRawText(),
                               "waitUntil" => DescribeCondition(step),
                               "expect" => DescribeExpect(step.Expect!),
                               "settle" => step.Settle!.MaxSeconds is { } max ? $"≤{max.ToString(CultureInfo.InvariantCulture)} s" :
                                               $"≤{DefaultSettleMaxSeconds.ToString(CultureInfo.InvariantCulture)} s",
                               "advance" => $"{step.Advance!.Seconds.ToString(CultureInfo.InvariantCulture)} s",
                               _ => null,
                           },
                       };
            }

            return new ScenarioRunReport
                   {
                       RunId = options.RunId ?? Guid.NewGuid().ToString("N"),
                       ScenarioId = scenario.Id ?? string.Empty,
                       Title = scenario.Title,
                       Topology = scenario.Topology,
                       FileHash = options.FileHash,
                       StartedAt = DateTimeOffset.UtcNow,
                       Setup = (scenario.Setup ?? Array.Empty<ScenarioStep>()).Select(Describe).ToList(),
                       Steps = (scenario.Steps ?? Array.Empty<ScenarioStep>()).Select(Describe).ToList(),
                       Judge = (scenario.Judge ?? Array.Empty<ScenarioJudgment>())
                               .Select(j => new ScenarioJudgmentResult { Text = j.Text ?? string.Empty, Spec = j.Spec })
                               .ToList(),
                   };
        }

        private static async Task ExecuteAsync(ScenarioFile scenario,
                                               IDevHostControl control,
                                               ScenarioRunOptions options,
                                               ScenarioRunReport report,
                                               Action<ScenarioRunReport> progress,
                                               CancellationToken cancellationToken)
        {
            var configuration = control.GetConfiguration();
            report.HostTopology = configuration.TopologyName;

            // Topology guard: blocked before anything runs (Player interstitial / CI skip semantics live
            // with the callers; the runner just refuses).
            if (!options.IgnoreTopologyMismatch && !string.Equals(scenario.Topology, configuration.TopologyName, StringComparison.Ordinal))
            {
                report.Status = ScenarioRunStatus.TopologyMismatch;
                report.ValidationErrors = new[]
                                          {
                                              $"scenario expects topology '{scenario.Topology}' but the host declares " +
                                              (configuration.TopologyName is null ? "none (no WithTopologyName)" : $"'{configuration.TopologyName}'"),
                                          };
                SkipRemaining(report, "topology mismatch");
                return;
            }

            // Up-front validation: every name path in setup/steps/watch resolves (watch-only scenarios
            // smoke renames this way), set targets are writable, waitUntil comparators fit the schema.
            var resolver = new ScenarioResolver(configuration);
            var errors = new List<string>();
            var setupSteps = scenario.Setup ?? Array.Empty<ScenarioStep>();
            var runSteps = scenario.Steps ?? Array.Empty<ScenarioStep>();
            var resolvedSetup = setupSteps.Select((s, i) => resolver.ResolveStep(s, $"setup[{i}]", errors)).ToList();
            var resolvedSteps = runSteps.Select((s, i) => resolver.ResolveStep(s, $"steps[{i}]", errors)).ToList();
            foreach (var (path, index) in (scenario.Watch ?? Array.Empty<string>()).Select((w, i) => (w, i)))
            {
                resolver.ResolveProperty(path, $"watch[{index}]", errors);
            }

            if (errors.Count > 0)
            {
                report.Status = ScenarioRunStatus.Failed;
                report.ValidationErrors = errors;
                SkipRemaining(report, "validation failed");
                return;
            }

            var watchPaths = scenario.Watch;

            // Setup in file order, then steps in order; any failure stops the run.
            for (var i = 0; i < resolvedSetup.Count; i++)
            {
                if (!await RunStepAsync(setupSteps[i],
                                        resolvedSetup[i],
                                        report.Setup[i],
                                        control,
                                        progress,
                                        report,
                                        cancellationToken)
                         .ConfigureAwait(false))
                {
                    report.Status = ScenarioRunStatus.Failed;
                    SkipRemaining(report, "an earlier step failed");
                    return;
                }
            }

            for (var i = 0; i < resolvedSteps.Count; i++)
            {
                if (!await RunStepAsync(runSteps[i],
                                        resolvedSteps[i],
                                        report.Steps[i],
                                        control,
                                        progress,
                                        report,
                                        cancellationToken,
                                        watchPaths)
                         .ConfigureAwait(false))
                {
                    report.Status = ScenarioRunStatus.Failed;
                    SkipRemaining(report, "an earlier step failed");
                    return;
                }
            }

            report.Status = ScenarioRunStatus.Succeeded;
        }

        private static async Task<bool> RunStepAsync(ScenarioStep step,
                                                     ResolvedStep resolved,
                                                     ScenarioStepResult result,
                                                     IDevHostControl control,
                                                     Action<ScenarioRunReport> progress,
                                                     ScenarioRunReport report,
                                                     CancellationToken cancellationToken,
                                                     IReadOnlyList<string>? watchPaths = null)
        {
            cancellationToken.ThrowIfCancellationRequested();
            result.Status = ScenarioStepStatus.Running;
            progress(report);
            var startedAt = DateTimeOffset.UtcNow;
            var stopwatch = Stopwatch.StartNew();

            try
            {
                switch (step.Kind)
                {
                    case "set":
                        await control.SetPropertyAsync(resolved.Property!.Block, resolved.Property.ServiceIdentifier, resolved.Property.PropertyName, step.Value)
                                     .ConfigureAwait(false);
                        if (stopwatch.Elapsed.TotalMilliseconds >= AckCeilingMs)
                        {
                            // The block never acknowledged the write — applied writes, no-ops included,
                            // ack promptly on their round-trip response. A swallowed exception on the
                            // SET-VALUE message is the observable cause — surface it as the RFC's
                            // "rejected write" failure. The middleware logs the message type, so
                            // requiring it in the match keeps unrelated block exceptions out.
                            var rejection = control.RecentLogs()
                                                   .LastOrDefault(l => l.Timestamp >= startedAt && l.Message.Contains("[EXCEPTION CAUGHT]", StringComparison.Ordinal) &&
                                                                       l.Message.Contains("SetServicePropertyValue", StringComparison.Ordinal));
                            if (rejection is not null)
                            {
                                result.Detail = $"write appears rejected — a block exception was logged during this write: {rejection.Message}";
                                Fail(result, report, progress, stopwatch);
                                return false;
                            }

                            result.Detail = "the block never acknowledged this write — it may not have been applied";
                        }

                        break;

                    case "digitalInput":
                        await control.SetDigitalInputAsync(resolved.Contract!.ServiceProviderId, resolved.Contract.ServiceId, resolved.Contract.ContractId, step.Value.GetBoolean())
                                     .ConfigureAwait(false);
                        result.Detail = "injected (inputs are fire-and-forget; pair with waitUntil to observe the effect)";
                        break;

                    case "analogInput":
                        await control.SetAnalogInputAsync(resolved.Contract!.ServiceProviderId, resolved.Contract.ServiceId, resolved.Contract.ContractId, step.Value.GetDouble())
                                     .ConfigureAwait(false);
                        result.Detail = "injected (inputs are fire-and-forget; pair with waitUntil to observe the effect)";
                        break;

                    case "waitUntil":
                        if (!await WaitUntilAsync(step, resolved.Property!, control, result, cancellationToken).ConfigureAwait(false))
                        {
                            Fail(result, report, progress, stopwatch);
                            return false;
                        }

                        break;

                    case "expect":
                        if (!ExpectStep(step.Expect!, resolved, control, result))
                        {
                            Fail(result, report, progress, stopwatch);
                            return false;
                        }

                        break;

                    case "advance":
                        await control.AdvanceAsync(TimeSpan.FromSeconds(step.Advance!.Seconds), cancellationToken).ConfigureAwait(false);
                        break;

                    case "settle":
                        await SettleAsync(step, watchPaths, control, result, cancellationToken).ConfigureAwait(false);
                        break;

                    default: // wait
                        result.Detail = "wall-clock delay (non-deterministic) — prefer advance for stepped hosts";
                        await Task.Delay(TimeSpan.FromSeconds(step.Wait!.Seconds), cancellationToken).ConfigureAwait(false);
                        break;
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                result.Detail = e.Message;
                Fail(result, report, progress, stopwatch);
                return false;
            }

            result.Status = ScenarioStepStatus.Ok;
            result.ElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            progress(report);
            return true;
        }

        // The waitUntil protocol (RFC 0006 "Execution model"): two branches depending on the host clock.
        //
        // Stepped host (FakeTimeProvider — control.IsStepped): advance virtual time hop-by-hop until the
        // condition holds or the virtual-time budget is exhausted. No real wall-clock wait occurs; the test
        // completes near-instantly even for multi-second virtual waits. A stall (AdvanceToNextEventAsync
        // returns without moving the clock, meaning nothing is scheduled) fails promptly rather than spinning.
        //
        // Real-clock host (Player free-run, !IsStepped): the original WaitForAsync event-subscription path —
        // observes only events that occur after the call, with the set-before-subscribe race closed by a
        // re-check immediately after subscribing.
        private static async Task<bool> WaitUntilAsync(ScenarioStep step,
                                                       ResolvedProperty target,
                                                       IDevHostControl control,
                                                       ScenarioStepResult result,
                                                       CancellationToken cancellationToken)
        {
            var condition = step.WaitUntil!;
            var timeoutSeconds = step.TimeoutSeconds ?? DefaultWaitUntilTimeoutSeconds;
            var timeout = TimeSpan.FromSeconds(timeoutSeconds);

            bool Satisfied(object? live)
            {
                // For a struct field-path target, the comparison is against the scalar field leaf — extract
                // it from the boxed struct before handing it to the (unchanged) comparison logic.
                return ScenarioConditions.IsSatisfied(condition, ExtractField(live, target.FieldPath));
            }

            object? Current()
            {
                return control.GetProperty(target.Block, target.ServiceIdentifier, target.PropertyName);
            }

            // The leaf value shown in failure details — the extracted field for a field-path target.
            string LastDisplay()
            {
                var leaf = ExtractField(Current(), target.FieldPath);
                return leaf is null ? "null" : Convert.ToString(leaf, CultureInfo.InvariantCulture) ?? "null";
            }

            // Already-satisfied fast-path: identical in both modes.
            if (Satisfied(Current()))
            {
                result.Detail = "already satisfied";
                return true;
            }

            // ── Stepped path ─────────────────────────────────────────────────────────────────────────────
            if (control.IsStepped)
            {
                var virtualStart = control.VirtualTimeUtc;
                var budget = timeout;
                var hops = 0;

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var beforeHop = control.VirtualTimeUtc;
                    await control.AdvanceToNextEventAsync(cancellationToken).ConfigureAwait(false);
                    hops++;

                    if (Satisfied(Current()))
                    {
                        var elapsed = (control.VirtualTimeUtc - virtualStart).TotalSeconds;
                        result.Detail = $"satisfied after {hops} hop{(hops == 1 ? "" : "s")} / {elapsed.ToString("0.###", CultureInfo.InvariantCulture)} virtual s";
                        return true;
                    }

                    // No progress: nothing was scheduled so the clock didn't move.
                    if (control.VirtualTimeUtc == beforeHop)
                    {
                        result.Detail = $"condition not met — no further scheduled events (last value: {LastDisplay()})";
                        return false;
                    }

                    // Virtual budget exhausted.
                    if (control.VirtualTimeUtc - virtualStart >= budget)
                    {
                        result.Detail = $"condition not met within {timeoutSeconds.ToString(CultureInfo.InvariantCulture)} virtual s (last value: {LastDisplay()})";
                        return false;
                    }
                }
            }

            // ── Real-clock path ──────────────────────────────────────────────────────────────────────────
            // WaitForAsync observes only events occurring after the call. Subscribe, then re-evaluate once
            // more to close the set-between-check-and-subscribe race. A timeout converts to a step failure.

            DevHostEvent? Selector(DevHostEvent e)
            {
                return e switch
                {
                    ServicePropertyChanged sp when sp.ServiceId == target.ServiceConfigId && sp.Property == target.PropertyName && Satisfied(sp.Value) => e,
                    ServiceMeasuringPointChanged mp when mp.ServiceId == target.ServiceConfigId && mp.MeasuringPoint == target.PropertyName && Satisfied(mp.Value) => e,
                    _ => null,
                };
            }

            // The early-exit token releases the registered waiter promptly when the re-check already
            // satisfies the condition (no abandoned waiter living out the full timeout) and propagates
            // run cancellation into the wait itself.
            using var earlyExit = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            var wait = control.WaitForAsync(Selector, timeout, earlyExit.Token);

            if (Satisfied(Current()))
            {
                earlyExit.Cancel();
                await wait.ConfigureAwait(false);
                return true;
            }

            var match = await wait.ConfigureAwait(false);
            cancellationToken.ThrowIfCancellationRequested();
            if (match is not null)
            {
                return true;
            }

            result.Detail = $"condition not met within {timeoutSeconds.ToString(CultureInfo.InvariantCulture)} s (last value: {LastDisplay()})";
            return false;
        }

        // The expect assertion (RFC 0006 "Assert tier"): a single point-in-time check of the target's CURRENT
        // value. No awaiting — read once, evaluate, and FAIL the step (so the run fails) when the comparator
        // does not hold. For a relational {path} comparand, the comparand path's current value is read too and
        // supplied as the live comparand. Both target and comparand honor struct field paths via ExtractField.
        private static bool ExpectStep(ScenarioExpect expect, ResolvedStep resolved, IDevHostControl control, ScenarioStepResult result)
        {
            var target = resolved.Property!;
            var live = ExtractField(control.GetProperty(target.Block, target.ServiceIdentifier, target.PropertyName), target.FieldPath);

            var hasComparand = resolved.Comparand is not null;
            object? comparandLive = null;
            if (resolved.Comparand is { } comparand)
            {
                comparandLive = ExtractField(control.GetProperty(comparand.Block, comparand.ServiceIdentifier, comparand.PropertyName), comparand.FieldPath);
            }

            if (ScenarioConditions.IsSatisfied(expect, live, hasComparand, comparandLive))
            {
                result.Detail = $"expectation held: {target.PropertyName} {DescribeExpect(expect)} (value {Display(live)})";
                return true;
            }

            result.Detail = ExpectFailureDetail(expect, resolved, live, comparandLive);
            return false;
        }

        // The failing-expect detail — "expected X above Y, but was Z" / "value W is not one of […]".
        private static string ExpectFailureDetail(ScenarioExpect expect, ResolvedStep resolved, object? live, object? comparandLive)
        {
            var target = resolved.Property!.PropertyName;
            var actual = Display(live);

            if (expect.OneOf.ValueKind == JsonValueKind.Array)
            {
                var options = string.Join(", ", expect.OneOf.EnumerateArray().Select(e => e.GetRawText()));
                return $"expected {target} to be one of [{options}], but was {actual}";
            }

            string Bound(JsonElement literal)
            {
                return resolved.Comparand is not null ? $"{Display(comparandLive)} (from {resolved.Comparand.PropertyName})" : literal.GetRawText();
            }

            if (expect.Above.ValueKind != JsonValueKind.Undefined)
            {
                return $"expected {target} above {Bound(expect.Above)}, but was {actual}";
            }

            if (expect.Below.ValueKind != JsonValueKind.Undefined)
            {
                return $"expected {target} below {Bound(expect.Below)}, but was {actual}";
            }

            if (expect.EqualTo.ValueKind != JsonValueKind.Undefined)
            {
                var tolerance = expect.Tolerance is { } t ? $" (±{t.ToString(CultureInfo.InvariantCulture)})" : "";
                return $"expected {target} to equal {Bound(expect.EqualTo)}{tolerance}, but was {actual}";
            }

            return $"expected {target} to not equal {Bound(expect.NotEquals)}, but was {actual}";
        }

        private static string Display(object? value)
        {
            return value is null ? "null" : Convert.ToString(value, CultureInfo.InvariantCulture) ?? "null";
        }

        // Settle protocol: advance hop-by-hop until the watched values are stable across one full hop, or
        // until the virtual-time budget is exhausted. An empty watch list converges immediately — nothing to
        // stabilize. Virtual time elapsed is tracked via control.VirtualTimeUtc so the budget is in virtual
        // seconds, not wall seconds.
        private static async Task SettleAsync(ScenarioStep step,
                                              IReadOnlyList<string>? watchPaths,
                                              IDevHostControl control,
                                              ScenarioStepResult result,
                                              CancellationToken cancellationToken)
        {
            var maxSeconds = step.Settle!.MaxSeconds ?? DefaultSettleMaxSeconds;
            var budget = TimeSpan.FromSeconds(maxSeconds);

            // Empty watch list: nothing to observe → converged immediately (stable by definition).
            if (watchPaths is null || watchPaths.Count == 0)
            {
                result.Detail = "converged immediately (no watch paths to observe)";
                return;
            }

            // Resolve the watch paths once (they passed up-front validation) so the snapshot reads the
            // correct service-qualified member AND extracts the scalar leaf for any struct field path.
            var resolver = new ScenarioResolver(control.GetConfiguration());
            var resolved = watchPaths.Select(p => resolver.ResolveProperty(p, "settle.watch", new List<string>())).ToList();

            object?[] Snapshot()
            {
                var values = new object?[resolved.Count];
                for (var i = 0; i < resolved.Count; i++)
                {
                    var target = resolved[i];
                    values[i] = target is null ? null : ExtractField(control.GetProperty(target.Block, target.ServiceIdentifier, target.PropertyName), target.FieldPath);
                }

                return values;
            }

            bool ValuesEqual(object?[] a, object?[] b)
            {
                for (var i = 0; i < a.Length; i++)
                {
                    if (!Equals(a[i], b[i]))
                    {
                        return false;
                    }
                }

                return true;
            }

            var virtualStart = control.VirtualTimeUtc;
            var deadline = virtualStart + budget;
            var hops = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var before = Snapshot();
                await control.AdvanceToNextEventAsync(cancellationToken).ConfigureAwait(false);
                hops++;
                var after = Snapshot();

                if (ValuesEqual(before, after))
                {
                    var elapsed = (control.VirtualTimeUtc - virtualStart).TotalSeconds;
                    result.Detail = $"converged after {hops} hop{(hops == 1 ? "" : "s")} / {elapsed.ToString("0.###", CultureInfo.InvariantCulture)} virtual s";
                    return;
                }

                if (control.VirtualTimeUtc >= deadline)
                {
                    var elapsed = (control.VirtualTimeUtc - virtualStart).TotalSeconds;
                    result.Detail =
                        $"did not converge within {maxSeconds.ToString(CultureInfo.InvariantCulture)} virtual s ({hops} hop{(hops == 1 ? "" : "s")} / {elapsed.ToString("0.###", CultureInfo.InvariantCulture)} virtual s elapsed)";
                    return;
                }
            }
        }

        // Extract a struct field leaf from a (boxed) live value by walking the PascalCase field path via
        // reflection. Each segment reads a public property (record-struct positional fields surface as
        // properties) or, failing that, a field of the same name. A null intermediate short-circuits to a
        // null leaf. With no field path the value passes through unchanged (the common scalar case).
        private static object? ExtractField(object? value, IReadOnlyList<string>? fieldPath)
        {
            if (fieldPath is null || fieldPath.Count == 0)
            {
                return value;
            }

            foreach (var segment in fieldPath)
            {
                if (value is null)
                {
                    return null;
                }

                var type = value.GetType();
                value = type.GetProperty(segment)?.GetValue(value) ?? type.GetField(segment)?.GetValue(value);
            }

            return value;
        }

        private static void Fail(ScenarioStepResult result, ScenarioRunReport report, Action<ScenarioRunReport> progress, Stopwatch stopwatch)
        {
            result.Status = ScenarioStepStatus.Failed;
            result.ElapsedMs = stopwatch.Elapsed.TotalMilliseconds;
            progress(report);
        }

        private static void SkipRemaining(ScenarioRunReport report, string reason)
        {
            foreach (var step in report.Setup.Concat(report.Steps))
            {
                if (step.Status == ScenarioStepStatus.Pending || step.Status == ScenarioStepStatus.Running)
                {
                    step.Status = step.Status == ScenarioStepStatus.Running ? ScenarioStepStatus.Failed : ScenarioStepStatus.Skipped;
                    step.Detail ??= reason;
                }
            }
        }
    }
}