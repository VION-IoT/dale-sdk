using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The scenario interpreter end to end against a real host: name-path resolution against the wired
    ///     configuration, the topology guard, the real-clock waitUntil protocol, and the failure taxonomy. The
    ///     runner is the one evaluator the Player, CI and agents share, so these tests are the semantics
    ///     contract.
    ///     <para>
    ///         Structural parsing is <c>ScenarioFileShould</c>'s — it needs no host, and proving it here too
    ///         made the same rule answer to two suites.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ScenarioRunnerShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.2")]
        [TestProperty("spec", "AC-SCEN-005.8")]
        [TestProperty("spec", "AC-SCEN-005.4")]
        [TestProperty("spec", "AC-SCEN-005.2")]
        public async Task FailValidationForUnknownAndAmbiguousNamePaths()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "bad-paths", "topology": "scenario-topology",
                                                "steps": [
                                                  { "set": "NoSuchBlock.Counter", "value": 1 },
                                                  { "set": "DualPoint.Limit", "value": 1.0 },
                                                  { "set": "Counter.CounterDoubled", "value": 1 }
                                                ],
                                                "watch": [ "Counter.NoSuchProperty" ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status);
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("NoSuchBlock")), Join(report));

            // The revision 5 ambiguity rule: never silent last-wins — the error lists the qualified candidates.
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("ambiguous") && e.Contains("DualPoint.PointA.Limit") && e.Contains("DualPoint.PointB.Limit")), Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("measuring point")), Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("NoSuchProperty")), Join(report));
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Skipped), Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.1")]
        public async Task RefuseRunOnTopologyMismatch()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              { "version": 1, "id": "wrong-topo", "topology": "some-other-topology",
                                                "steps": [ { "set": "Counter.Counter", "value": 5 } ] }
                                              """);

            // A scenario only runs against the topology it declares — there is no "force" override. The web
            // caller brings the host to the right topology first (recycle-on-run); the in-process runner just
            // refuses a mismatch loudly.

            // Act
            var blocked = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.TopologyMismatch, blocked.Status);
            Assert.AreEqual("scenario-topology", blocked.HostTopology);
            Assert.IsTrue(blocked.Steps.All(s => s.Status == ScenarioStepStatus.Skipped), Join(blocked));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.8")]
        [TestProperty("spec", "AC-SCEN-009.6")]
        [TestProperty("spec", "AC-SCEN-009.3")]
        public async Task RunSetupThenStepsInFileOrder()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "smoke", "title": "Smoke", "topology": "scenario-topology",
                                                "setup": [ { "set": "Counter.Counter", "value": 2 } ],
                                                "steps": [
                                                  { "label": "raise", "set": "Counter.Counter", "value": 21 },
                                                  { "label": "doubled follows", "waitUntil": { "property": "Counter.CounterDoubled", "above": 40 }, "timeoutSeconds": 10 },
                                                  { "label": "pace", "advance": { "seconds": 0.1 } },
                                                  { "label": "exact", "waitUntil": { "property": "Counter.Counter", "equals": 21 }, "timeoutSeconds": 5 }
                                                ],
                                                "watch": [ "Counter.CounterDoubled" ],
                                                "judge": [ { "text": "Counter felt responsive", "spec": "AC-TEST-1" } ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual(ScenarioStepStatus.Ok, report.Setup[0].Status);
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Ok), Join(report));
            Assert.IsTrue(report.Steps.All(s => s.ElapsedMs is not null), Join(report));

            // The report carries WHAT ran, not just where: set payloads and waitUntil conditions.
            Assert.AreEqual("21", report.Steps[0].Argument);
            Assert.AreEqual("> 40 · 10 s timeout", report.Steps[1].Argument);
            Assert.AreEqual("2", report.Setup[0].Argument);
            Assert.AreEqual("requiresHuman", report.Judge[0].Status);
            Assert.AreEqual("AC-TEST-1", report.Judge[0].Spec);
            Assert.AreEqual(21, host.Control.GetProperty("Counter", "Counter"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-011.7")]
        public async Task ObserveFutureEventInWaitUntilOnRealClock()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // The ticker increments once a second; waiting for "more than now" can only be satisfied by a
            // FUTURE publish — exercising the subscribe path of the check-subscribe-recheck protocol.
            var current = host.Control.GetProperty("Ticker", "Ticks") as int? ?? 0;
            var scenario = ScenarioFile.Parse($$"""
                                                {
                                                  "version": 1, "id": "future", "topology": "scenario-topology",
                                                  "steps": [ { "waitUntil": { "property": "Ticker.Ticks", "above": {{current + 1}} }, "timeoutSeconds": 15 } ]
                                                }
                                                """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.3")]
        public async Task FailStepOnWaitUntilTimeoutAndSkipRemainder()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "timeout", "topology": "scenario-topology",
                                                "steps": [
                                                  { "label": "never", "waitUntil": { "property": "Counter.Counter", "above": 999999 }, "timeoutSeconds": 1 },
                                                  { "label": "unreached", "advance": { "seconds": 0.1 } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.AreEqual(ScenarioStepStatus.Failed, report.Steps[0].Status);
            StringAssert.Contains(report.Steps[0].Detail, "condition not met within 1 s");
            Assert.AreEqual(ScenarioStepStatus.Skipped, report.Steps[1].Status);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.5")]
        public async Task ReportCancelledRunAsCancelledAndSkipRemainder()
        {
            // Arrange - cancel from the progress callback the moment the first step starts running, so the
            // run is cancelled mid-step without any test waiting on a clock.
            await using var host = BuildHost();
            await host.StartAsync();

            using var cancellation = new CancellationTokenSource();
            var options = new ScenarioRunOptions
                          {
                              OnProgress = report =>
                                           {
                                               if (report.Steps[0].Status == ScenarioStepStatus.Running)
                                               {
                                                   cancellation.Cancel();
                                               }
                                           },
                          };

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "canceled", "topology": "scenario-topology",
                                                "steps": [
                                                  { "label": "never", "waitUntil": { "property": "Counter.Counter", "above": 999999 }, "timeoutSeconds": 30 },
                                                  { "label": "unreached", "advance": { "seconds": 0.1 } }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control, options, cancellation.Token);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Canceled, report.Status, Join(report));
            Assert.AreEqual(ScenarioStepStatus.Skipped, report.Steps[1].Status, Join(report));
            StringAssert.Contains(report.Steps[1].Detail, "canceled");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-005.6")]
        public async Task ReachShadowedServicesViaQualifiedPaths()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "qualified", "topology": "scenario-topology",
                                                "steps": [
                                                  { "set": "DualPoint.PointA.Limit", "value": 3.5 },
                                                  { "set": "DualPoint.PointB.Limit", "value": 7.5 },
                                                  { "waitUntil": { "property": "DualPoint.PointA.Limit", "equals": 3.5 }, "timeoutSeconds": 5 }
                                                ]
                                              }
                                              """);

            // Act
            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual(3.5, host.Control.GetProperty("DualPoint", "PointA", "Limit"));
            Assert.AreEqual(7.5, host.Control.GetProperty("DualPoint", "PointB", "Limit"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.4")]
        public async Task ThrowFromApplyOnFailureForCSharpComposition()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              { "version": 1, "id": "broken", "topology": "scenario-topology",
                                                "steps": [ { "set": "NoSuchBlock.X", "value": 1 } ] }
                                              """);

            // Act
            var e = await Assert.ThrowsExactlyAsync<ScenarioRunException>(() => ScenarioRunner.ApplyAsync(scenario, host.Control));

            // Assert
            StringAssert.Contains(e.Message, "NoSuchBlock");
            Assert.AreEqual(ScenarioRunStatus.Failed, e.Report.Status);
        }

        private static IDevHost BuildHost()
        {
            var config = DevConfigurationBuilder.Create()
                                                .WithTopologyName("scenario-topology")
                                                .AddLogicBlock<CounterBlock>("Counter")
                                                .AddLogicBlock<DualPointBlock>("DualPoint")
                                                .AddLogicBlock<TickerBlock>("Ticker")
                                                .Build();
            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
        }

        private static string Join(ScenarioRunReport report)
        {
            var steps = report.Setup.Concat(report.Steps).Select(s => $"[{s.Index} {s.Kind} {s.Status}: {s.Detail}]");
            return string.Join("; ", report.ValidationErrors.Concat(steps));
        }
    }
}