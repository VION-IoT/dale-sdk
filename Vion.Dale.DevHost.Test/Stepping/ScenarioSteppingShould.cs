using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     What a step means under each clock mode, end to end on a real host: the host-adaptive
    ///     <c>advance</c> / <c>waitUntil</c> / <c>settle</c> protocols, the watch trace they produce, and the
    ///     absence of any clock-mode refusal.
    ///     <para>
    ///         The structural rules these shapes also have — a non-positive budget, an empty
    ///         <c>settle.until</c>, the setup subset, the parse round-trip — are
    ///         <c>ScenarioFileShould</c>'s: they need no host, and proving them twice made one of them a
    ///         parse assertion wearing a report test's name.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ScenarioSteppingShould
    {
        private static readonly DateTimeOffset Epoch = new(2026,
                                                           1,
                                                           1,
                                                           0,
                                                           0,
                                                           0,
                                                           TimeSpan.Zero);

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.4")]
        public async Task JumpVirtualTimeExactlyOnSteppedHost()
        {
            // Arrange
            var ticks = new int[15];

            // Act — repeated because the value of this step is that it is the same every time.
            for (var run = 0; run < ticks.Length; run++)
            {
                await using var host = BuildTickerHost(NewClock());
                await host.StartAsync();
                var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                              {
                                                                                "version": 1, "id": "advance-ticker", "topology": "stepping-topology",
                                                                                "watch": ["Ticker.Ticks"],
                                                                                "steps": [
                                                                                  { "advance": { "seconds": 3 } },
                                                                                  { "waitUntil": { "property": "Ticker.Ticks", "above": 2 }, "timeoutSeconds": 1 }
                                                                                ]
                                                                              }
                                                                              """),
                                                           host.Control);
                Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, $"run {run}: {Join(report)}");
                ticks[run] = (int)host.Control.GetProperty("Ticker", "Ticks")!;
            }

            // Assert
            CollectionAssert.AreEqual(Enumerable.Repeat(3, ticks.Length).ToArray(), ticks);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.4")]
        public async Task WaitRealTimeOnRealClockHostAndSaySo()
        {
            // Arrange — no controllable clock, so TimeProvider.System stays in place.
            await using var host = BuildRealClockTickerHost();
            await host.StartAsync();
            Assert.IsFalse(host.Control.IsStepped, "precondition: a real-clock host");

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "advance-real-clock", "topology": "stepping-topology",
                                                                            "watch": ["Ticker.Ticks"],
                                                                            "steps": [ { "advance": { "seconds": 1.5 } } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — the world moved rather than freezing, and the detail says which clock was spent.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail, "real wall-clock wait");
            Assert.IsGreaterThanOrEqualTo(1, (int)host.Control.GetProperty("Ticker", "Ticks")!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.3")]
        public async Task RefuseNoStepKindForHostsClockMode()
        {
            // Arrange — the same three time-sensitive kinds against both clock modes. The criterion states an
            // absence, so what is asserted is that resolution reports nothing about the mode either way.
            await using var stepped = BuildTickerHost(NewClock());
            await using var realClock = BuildRealClockTickerHost();
            await stepped.StartAsync();
            await realClock.StartAsync();

            const string timeSensitive = """
                                         {
                                           "version": 1, "id": "modes", "topology": "stepping-topology",
                                           "watch": ["Ticker.Ticks"],
                                           "steps": [
                                             { "advance": { "seconds": 1 } },
                                             { "waitUntil": { "property": "Ticker.Ticks", "above": 0 }, "timeoutSeconds": 5 },
                                             { "settle": { "until": ["Ticker.Ticks"], "maxSeconds": 1 } }
                                           ]
                                         }
                                         """;

            // Act
            var onStepped = await ScenarioRunner.RunAsync(ScenarioFile.Parse(timeSensitive), stepped.Control);
            var onRealClock = await ScenarioRunner.RunAsync(ScenarioFile.Parse(timeSensitive), realClock.Control);

            // Assert — neither run carries a resolution error, and neither step detail refuses a kind for the
            // mode. (The settle over a free-running ticker fails to converge in both modes, which is the
            // budget rule and not a mode refusal.)
            Assert.IsEmpty(onStepped.ValidationErrors, Join(onStepped));
            Assert.IsEmpty(onRealClock.ValidationErrors, Join(onRealClock));
            foreach (var detail in onStepped.Steps.Concat(onRealClock.Steps).Select(s => s.Detail ?? string.Empty))
            {
                Assert.IsFalse(detail.Contains("not supported", StringComparison.OrdinalIgnoreCase), detail);
                Assert.IsFalse(detail.Contains("requires a stepped host", StringComparison.OrdinalIgnoreCase), detail);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.2")]
        public async Task StartVirtualClockAtFixedEpochAndStepIt()
        {
            // Arrange
            await using var host = BuildSteppedHost();
            await host.StartAsync();

            // Act
            var startedAt = host.Control.VirtualTimeUtc;
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "epoch", "topology": "stepping-topology",
                                                                            "steps": [
                                                                              { "advance": { "seconds": 3 } },
                                                                              { "expect": { "property": "Ticker.Ticks", "equals": 3 } }
                                                                            ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — a stepped run is reproducible only from a stable start, so the epoch is the contract.
            Assert.IsTrue(host.Control.IsStepped);
            Assert.AreEqual(Epoch, startedAt);
            Assert.AreEqual(Epoch.AddSeconds(3), host.Control.VirtualTimeUtc);
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.5")]
        public async Task SatisfyWaitUntilImmediatelyWhenConditionAlreadyHolds()
        {
            // Arrange — the ticker starts at zero, so "below 1" holds before any event occurs.
            await using var host = BuildSteppedHost();
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "already", "topology": "stepping-topology",
                                                                            "steps": [ { "waitUntil": { "property": "Ticker.Ticks", "below": 1 }, "timeoutSeconds": 5 } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — the fast path must not depend on an event arriving after the call.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("already satisfied", report.Steps[0].Detail);
            Assert.AreEqual(Epoch, host.Control.VirtualTimeUtc);
        }

        [TestMethod]
        [TestCategory("Smoke")]
        [TestProperty("spec", "AC-SCEN-011.6")]
        public async Task HopVirtualTimeUntilWaitUntilHolds()
        {
            // Arrange
            await using var host = BuildTickerHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "wait-until-stepped", "topology": "stepping-topology",
                                                                            "steps": [ { "waitUntil": { "property": "Ticker.Ticks", "above": 2 }, "timeoutSeconds": 5 } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — the wait was virtual. The virtual clock and the detail carry the whole claim: on a
            // real-clock host this step takes the polling branch, which moves no virtual time and writes no
            // "hop" — so no wall-clock bound is needed to tell the two apart, and any bound that was here
            // could only be a guess about how fast this machine runs.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsGreaterThanOrEqualTo(3, (int)host.Control.GetProperty("Ticker", "Ticks")!);
            StringAssert.Contains(report.Steps[0].Detail, "virtual s");
            StringAssert.Contains(report.Steps[0].Detail, "hop");
            Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(3), host.Control.VirtualTimeUtc - Epoch);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.6")]
        public async Task FailWaitUntilOnVirtualBudget()
        {
            // Arrange
            await using var host = BuildTickerHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "wait-until-budget", "topology": "stepping-topology",
                                                                            "steps": [ { "waitUntil": { "property": "Ticker.Ticks", "above": 998 }, "timeoutSeconds": 5 } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — the budget was spent in VIRTUAL seconds: five of them elapsed on the clock, in less
            // real time than that.
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail, "condition not met within 5 virtual s");
            Assert.IsGreaterThanOrEqualTo(TimeSpan.FromSeconds(5), host.Control.VirtualTimeUtc - Epoch);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.8")]
        public async Task ConvergeSettleWhenItsTargetsStabilise()
        {
            // Arrange — the latch fires once and then holds.
            await using var host = BuildSettleHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "settle-converge", "topology": "stepping-topology",
                                                                            "watch": ["Latch.Value"],
                                                                            "steps": [ { "settle": { "maxSeconds": 30 } } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail, "converged");
            Assert.AreEqual(1, (int)host.Control.GetProperty("Latch", "Value")!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.8")]
        [DataRow("""{ "settle": { "maxSeconds": 3 } }""", DisplayName = "over the watch list")]
        [DataRow("""{ "settle": { "until": ["Ticker.Ticks"], "maxSeconds": 3 } }""", DisplayName = "over a declared target")]
        public async Task FailSettleNamingTheStillChangingTarget(string step)
        {
            // Arrange — the ticker never stops, so the budget is exhausted whichever way the step is scoped.
            await using var host = BuildSettleHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse($$"""
                                                                            {
                                                                              "version": 1, "id": "settle-budget", "topology": "stepping-topology",
                                                                              "watch": ["Ticker.Ticks"],
                                                                              "steps": [{{step}}]
                                                                            }
                                                                            """),
                                                       host.Control);

            // Assert — a settle that never converges is a failure with a named cause, not a silent pass.
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.AreEqual(ScenarioStepStatus.Failed, report.Steps[0].Status);
            StringAssert.Contains(report.Steps[0].Detail, "did not converge");
            StringAssert.Contains(report.Steps[0].Detail, "Ticker.Ticks");
            StringAssert.Contains(report.Steps[0].Detail, "→");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.11")]
        public async Task ConvergeSettleOnFirstHopWhenNoTargetMoves()
        {
            // Arrange - a block declaring no timer, so nothing the settle targets is ever scheduled. The
            // schedule is NOT empty even so: a live logic block carries the framework's own periodic event,
            // which is what the single hop below lands on.
            await using var host = BuildQuietHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "settle-quiescent", "topology": "stepping-topology",
                                                                            "watch": ["Quiet.Value"],
                                                                            "steps": [ { "settle": { "maxSeconds": 30 } } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert - one hop, and nothing the settle watches moved across it, so the targets are stable and
            // the step converges having proved nothing. The detail is the only tell, asserted whole rather
            // than by substring: "60 virtual s" contains "0 virtual s", so a Contains here would hold for a
            // hop that jumped a minute as readily as for one that jumped nothing.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("converged after 1 hop / 60 virtual s", report.Steps[0].Detail);
            Assert.AreEqual(0, (int)host.Control.GetProperty("Quiet", "Value")!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.9")]
        public async Task ScopeSettleToItsDeclaredTargetsIgnoringVolatileWatch()
        {
            // Arrange — a large watch set is for observability and need not all settle.
            await using var host = BuildSettleHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "settle-until", "topology": "stepping-topology",
                                                                            "watch": ["Ticker.Ticks", "Latch.Value"],
                                                                            "steps": [ { "settle": { "until": ["Latch.Value"], "maxSeconds": 30 } } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — the never-settling watch tile is ignored because the step named its own target.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail, "converged");
            Assert.AreEqual(1, (int)host.Control.GetProperty("Latch", "Value")!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.9")]
        public async Task ConvergeSettleImmediatelyWhenTargetSetEmpty()
        {
            // Arrange — no watch list and no declared target, so there is nothing to stabilise.
            await using var host = BuildTickerHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "settle-empty-watch", "topology": "stepping-topology",
                                                                            "steps": [ { "settle": {} } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail, "no watch paths");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.10")]
        public async Task SpendSettleBudgetInActiveClocksSeconds()
        {
            // Arrange — a three-second budget over a target that never settles.
            await using var host = BuildSettleHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "settle-virtual-budget", "topology": "stepping-topology",
                                                                            "steps": [ { "settle": { "until": ["Ticker.Ticks"], "maxSeconds": 3 } } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — the clock moved three VIRTUAL seconds. The real-clock branch spends the same budget in
            // real seconds and leaves the virtual clock where it was, so this assertion is what separates
            // them; a wall-clock bound would add a machine-speed guess and no discrimination.
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail, "virtual s");
            Assert.IsGreaterThanOrEqualTo(Epoch.AddSeconds(3), host.Control.VirtualTimeUtc);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.2")]
        public async Task FailValidationForUnresolvableSettleTarget()
        {
            // Arrange
            await using var host = BuildTickerHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "settle-until-bad", "topology": "stepping-topology",
                                                                            "steps": [ { "settle": { "until": ["Ticker.Nonexistent"] } } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — a typo must fail up front, never "converge" on an unresolved target that cannot change.
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("settle.until")), Join(report));
            Assert.AreEqual(ScenarioStepStatus.Skipped, report.Steps[0].Status);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-010.1")]
        public async Task SampleWatchedValuesAfterSetupAndAfterEachStep()
        {
            // Arrange
            await using var host = BuildTickerHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "watch-trace", "topology": "stepping-topology",
                                                                            "watch": ["Ticker.Ticks"],
                                                                            "steps": [
                                                                              { "advance": { "seconds": 2 } },
                                                                              { "advance": { "seconds": 1 } }
                                                                            ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — one sample after setup, then one per step.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.HasCount(3, report.WatchTrace);
            CollectionAssert.AreEqual(new[] { "start", "steps", "steps" }, report.WatchTrace.Select(s => s.Phase).ToList());
            CollectionAssert.AreEqual(new[] { -1, 0, 1 }, report.WatchTrace.Select(s => s.StepIndex).ToList());
            CollectionAssert.AreEqual(new[] { 0, 2, 3 }, report.WatchTrace.Select(s => Convert.ToInt32(s.Values["Ticker.Ticks"])).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-010.2")]
        public async Task LeaveWatchTraceEmptyWhenNothingWatched()
        {
            // Arrange
            await using var host = BuildTickerHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "no-watch", "topology": "stepping-topology",
                                                                            "steps": [ { "advance": { "seconds": 2 } } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsEmpty(report.WatchTrace);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-010.3")]
        public async Task ProduceOneWatchTraceAcrossFreshSteppedHosts()
        {
            // Arrange
            var traces = new string[8];

            // Act
            for (var run = 0; run < traces.Length; run++)
            {
                await using var host = BuildTickerHost(NewClock());
                await host.StartAsync();
                var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                              {
                                                                                "version": 1, "id": "watch-repro", "topology": "stepping-topology",
                                                                                "watch": ["Ticker.Ticks"],
                                                                                "steps": [
                                                                                  { "advance": { "seconds": 2 } },
                                                                                  { "advance": { "seconds": 1 } }
                                                                                ]
                                                                              }
                                                                              """),
                                                           host.Control);
                Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, $"run {run}: {Join(report)}");
                traces[run] = string.Join(" | ", report.WatchTrace.Select(s => $"{s.Phase}:{s.StepIndex}:{s.Values["Ticker.Ticks"]}@{s.VirtualElapsedMs}"));
            }

            // Assert — values AND virtual timestamps, which is what makes a trace diffable run to run.
            StringAssert.Contains(traces[0], "@3000");
            CollectionAssert.AreEqual(Enumerable.Repeat(traces[0], traces.Length).ToArray(), traces);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.7")]
        [TestProperty("spec", "AC-SCEN-003.4")]
        public async Task DescribeEveryStepAndItsDefaultBudgetBeforeRun()
        {
            // Arrange — a topology the host is not on, so the report is built and nothing executes.
            await using var host = BuildTickerHost(NewClock());
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "described", "topology": "elsewhere",
                                                                            "setup": [ { "set": "Ticker.Ticks", "value": 1 } ],
                                                                            "steps": [
                                                                              { "advance": { "seconds": 2.5 } },
                                                                              { "settle": { "maxSeconds": 10 } },
                                                                              { "settle": {} },
                                                                              { "settle": { "until": ["Ticker.Ticks"] } },
                                                                              { "waitUntil": { "property": "Ticker.Ticks", "above": 1 } }
                                                                            ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — kind, target and argument are all present before a step runs, and the two omitted
            // budgets render as their documented defaults (20 s for a wait, 60 s for a settle).
            Assert.AreEqual(ScenarioRunStatus.TopologyMismatch, report.Status);
            CollectionAssert.AreEqual(new[] { "advance", "settle", "settle", "settle", "waitUntil" }, report.Steps.Select(s => s.Kind).ToList());
            CollectionAssert.AreEqual(new[] { "", "until stable", "until stable", "until Ticker.Ticks", "Ticker.Ticks" }, report.Steps.Select(s => s.Target).ToList());
            CollectionAssert.AreEqual(new[] { "2.5 s", "≤10 s", "≤60 s", "≤60 s", "> 1 · 20 s timeout" }, report.Steps.Select(s => s.Argument).ToList());
            Assert.AreEqual("set", report.Setup[0].Kind);
            Assert.AreEqual("Ticker.Ticks", report.Setup[0].Target);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.9")]
        public async Task RecordWallClockAlwaysAndVirtualOnlyWhenStepped()
        {
            // Arrange
            await using var stepped = BuildTickerHost(NewClock());
            await using var realClock = BuildRealClockTickerHost();
            await stepped.StartAsync();
            await realClock.StartAsync();

            const string body = """
                                {
                                  "version": 1, "id": "elapsed", "topology": "stepping-topology",
                                  "steps": [ { "advance": { "seconds": 2 } } ]
                                }
                                """;

            // Act
            var onStepped = await ScenarioRunner.RunAsync(ScenarioFile.Parse(body), stepped.Control);
            var onRealClock = await ScenarioRunner.RunAsync(ScenarioFile.Parse(body), realClock.Control);

            // Assert — the wall-clock figure is instrumentation and varies; the virtual one is the
            // reproducible number, and it exists only where there is a virtual clock to read.
            Assert.IsNotNull(onStepped.Steps[0].ElapsedMs);
            Assert.IsNotNull(onRealClock.Steps[0].ElapsedMs);
            Assert.AreEqual(2000d, onStepped.Steps[0].VirtualElapsedMs!.Value, 0.001);
            Assert.IsNull(onRealClock.Steps[0].VirtualElapsedMs);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.9")]
        public async Task AgreeOnEveryDeterministicReportFieldAcrossTwoRuns()
        {
            // Arrange — two runs of one scenario on one host, the second after a recycle-free re-run.
            await using var first = BuildTickerHost(NewClock());
            await using var second = BuildTickerHost(NewClock());
            await first.StartAsync();
            await second.StartAsync();

            const string body = """
                                {
                                  "version": 1, "id": "twice", "topology": "stepping-topology",
                                  "watch": ["Ticker.Ticks"],
                                  "steps": [
                                    { "advance": { "seconds": 2 } },
                                    { "expect": { "property": "Ticker.Ticks", "equals": 2 } }
                                  ]
                                }
                                """;

            // Act
            var one = await ScenarioRunner.RunAsync(ScenarioFile.Parse(body), first.Control);
            var two = await ScenarioRunner.RunAsync(ScenarioFile.Parse(body), second.Control);

            // Assert — every deterministic field agrees; the run id and the start instant are by construction
            // per-run, which is why "byte-identical report" is not the guarantee.
            Assert.AreEqual(Deterministic(one), Deterministic(two));
            Assert.AreNotEqual(one.RunId, two.RunId);
            Assert.AreNotEqual(one.StartedAt, two.StartedAt);

            static string Deterministic(ScenarioRunReport report)
            {
                var steps = report.Steps.Select(s => $"{s.Index}:{s.Kind}:{s.Target}:{s.Argument}:{s.Status}:{s.VirtualElapsedMs}:{s.Detail}");
                var trace = report.WatchTrace.Select(s => $"{s.Phase}:{s.StepIndex}:{s.Values["Ticker.Ticks"]}@{s.VirtualElapsedMs}");
                return $"{report.Status}|{report.ScenarioId}|{report.Topology}|{string.Join(";", steps)}|{string.Join(";", trace)}";
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.11")]
        public async Task ReportDriveAsFireAndForget()
        {
            // Arrange
            await using var host = BuildIoHost();
            await host.StartAsync();

            // Act
            var report = await ScenarioRunner.RunAsync(ScenarioFile.Parse("""
                                                                          {
                                                                            "version": 1, "id": "drive-detail", "topology": "io",
                                                                            "steps": [ { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true } ]
                                                                          }
                                                                          """),
                                                       host.Control);

            // Assert — an author who expects a drive to have landed needs to be told to pair it with a wait.
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail, "fire-and-forget");
            StringAssert.Contains(report.Steps[0].Detail, "waitUntil");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.7")]
        public async Task SettleStartupTrafficBeforeFirstEventHop()
        {
            // Arrange — the ticker publishes its initial state during startup, and the first hop must begin
            // from a settled system rather than racing that traffic.
            await using var host = BuildSteppedHost();
            await host.StartAsync();

            // Act — a zero budget does no stepping at all; the settle is the whole of its work.
            await host.Control.AdvanceAsync(TimeSpan.Zero);

            // Assert
            Assert.AreEqual(Epoch, host.Control.VirtualTimeUtc);
            Assert.AreEqual(0, (int)host.Control.GetProperty("Ticker", "Ticks")!);
            Assert.IsFalse(host.Control.HasAdvancedFromBaseline);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.9")]
        public async Task ReportWhetherSteppedGenerationMovedFromItsBaseline()
        {
            // Arrange
            await using var host = BuildSteppedHost();
            await host.StartAsync();

            // Act
            var whenFresh = host.Control.HasAdvancedFromBaseline;
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(5));

            // Assert — this is the clean-slate signal recycle-on-run reads.
            Assert.IsFalse(whenFresh);
            Assert.IsTrue(host.Control.HasAdvancedFromBaseline);
        }

        private static FakeTimeProvider NewClock()
        {
            return new FakeTimeProvider(Epoch);
        }

        private static IDevHost BuildTickerHost(FakeTimeProvider clock)
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<TickerBlock>("Ticker").Build();

            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).ConfigureServices(s => s.AddSingleton<TimeProvider>(clock)).Build();
        }

        private static IDevHost BuildSteppedHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<TickerBlock>("Ticker").Build();

            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        private static IDevHost BuildIoHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("io").AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io").Build();

            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        private static IDevHost BuildRealClockTickerHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<TickerBlock>("Ticker").Build();

            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
        }

        private static IDevHost BuildQuietHost(FakeTimeProvider clock)
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<QuietBlock>("Quiet").Build();

            return DevHostBuilder.Create().WithDi<SteppingDependencyInjection>().WithConfiguration(config).ConfigureServices(s => s.AddSingleton<TimeProvider>(clock)).Build();
        }

        private static IDevHost BuildSettleHost(FakeTimeProvider clock)
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("stepping-topology").AddLogicBlock<TickerBlock>("Ticker").AddLogicBlock<LatchBlock>("Latch").Build();

            return DevHostBuilder.Create().WithDi<SteppingDependencyInjection>().WithConfiguration(config).ConfigureServices(s => s.AddSingleton<TimeProvider>(clock)).Build();
        }

        private static string Join(ScenarioRunReport report)
        {
            var steps = report.Setup.Concat(report.Steps).Select(s => $"[{s.Index} {s.Kind} {s.Status}: {s.Detail}]");
            return string.Join("; ", report.ValidationErrors.Concat(steps));
        }
    }

    /// <summary>
    ///     Fires its <c>[Timer(1)]</c> exactly once, increments <see cref="Value" /> to 1 and then holds — the
    ///     fixture that gives <c>settle</c> a target which actually converges.
    /// </summary>
    [LogicBlock(Name = "Latch")]
    public class LatchBlock : LogicBlockBase
    {
        private bool _fired;

        [ServiceProperty(Title = "Value")]
        public int Value { get; private set; }

        public LatchBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            if (_fired)
            {
                return;
            }

            _fired = true;
            Value = 1;
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     Declares no timer, so a host built from it alone schedules nothing — the fixture that shows what a
    ///     <c>settle</c> does when there is no next event to advance to.
    /// </summary>
    [LogicBlock(Name = "Quiet")]
    public class QuietBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Value")]
        public int Value { get; private set; }

        public QuietBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>DI registration for the settle-host fixture.</summary>
    public class SteppingDependencyInjection : IConfigureServices
    {
        public void ConfigureServices(IServiceCollection serviceCollection)
        {
            serviceCollection.AddTransient<TickerBlock>();
            serviceCollection.AddTransient<LatchBlock>();
            serviceCollection.AddTransient<QuietBlock>();
        }
    }
}