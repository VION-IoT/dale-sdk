using System;
using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The RFC 0006 scenario interpreter, end to end against a real host: structural parsing, name-path
    ///     resolution (incl. the revision 5 ambiguity rule), the topology guard, the waitUntil protocol, and
    ///     the failure taxonomy. The runner is the ONE evaluator the Player, CI, and agents share — these
    ///     tests are the semantics contract.
    /// </summary>
    [TestClass]
    public class ScenarioRunnerShould
    {
        [TestMethod]
        public void RejectUnknownVocabularyVersions()
        {
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""{ "version": 2, "id": "x", "topology": "t" }"""));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("version must be 1")), string.Join("; ", e.Errors));
        }

        [TestMethod]
        public void RejectUnknownFields()
        {
            // additionalProperties: false posture — evolution is by version bump, not silent extra fields.
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""{ "version": 1, "id": "x", "topology": "t", "checks": [] }"""));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("not valid scenario JSON")), string.Join("; ", e.Errors));
        }

        [TestMethod]
        public void RejectMalformedSteps()
        {
            var twoShapes = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                   {
                                                                                                     "version": 1, "id": "x", "topology": "t",
                                                                                                     "steps": [ { "set": "A.B", "value": 1, "advance": { "seconds": 1 } } ]
                                                                                                   }
                                                                                                   """));
            Assert.IsTrue(twoShapes.Errors.Any(m => m.Contains("exactly one of")), string.Join("; ", twoShapes.Errors));

            var setWithoutValue = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                         { "version": 1, "id": "x", "topology": "t", "steps": [ { "set": "A.B" } ] }
                                                                                                         """));
            Assert.IsTrue(setWithoutValue.Errors.Any(m => m.Contains("set requires value")), string.Join("; ", setWithoutValue.Errors));

            var twoComparators = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                        {
                                                                                                          "version": 1, "id": "x", "topology": "t",
                                                                                                          "steps": [ { "waitUntil": { "property": "A.B", "above": 1, "below": 2 } } ]
                                                                                                        }
                                                                                                        """));
            Assert.IsTrue(twoComparators.Errors.Any(m => m.Contains("exactly one of above")), string.Join("; ", twoComparators.Errors));

            var waitInSetup = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                     { "version": 1, "id": "x", "topology": "t", "setup": [ { "advance": { "seconds": 1 } } ] }
                                                                                                     """));
            Assert.IsTrue(waitInSetup.Errors.Any(m => m.Contains("setup entries stage state")), string.Join("; ", waitInSetup.Errors));
        }

        [TestMethod]
        public void DistinguishExplicitNullFromAbsentValue()
        {
            // "value": null is a legal write (nullable properties); a missing "value" is a format error.
            var file = ScenarioFile.Parse("""
                                          { "version": 1, "id": "x", "topology": "t", "steps": [ { "set": "A.B", "value": null } ] }
                                          """);
            Assert.AreEqual(System.Text.Json.JsonValueKind.Null, file.Steps![0].Value.ValueKind);
        }

        [TestMethod]
        public void RejectReservedAndDuplicateKeyedFiles()
        {
            // 'schema' is shadowed by GET /api/scenarios/schema; duplicate keys would silently last-win
            // against the additionalProperties: false posture.
            var reserved = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""{ "version": 1, "id": "schema", "topology": "t" }"""));
            Assert.IsTrue(reserved.Errors.Any(m => m.Contains("reserved")), string.Join("; ", reserved.Errors));

            var duplicate = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""{ "version": 1, "id": "x", "topology": "t", "topology": "u" }"""));
            Assert.IsTrue(duplicate.Errors.Any(m => m.Contains("not valid scenario JSON")), string.Join("; ", duplicate.Errors));
        }

        [TestMethod]
        public async Task FailValidationForUnknownAndAmbiguousNamePaths()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status);
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("NoSuchBlock")), Join(report));

            // The revision 5 ambiguity rule: never silent last-wins — the error lists the qualified candidates.
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("ambiguous") && e.Contains("DualPoint.PointA.Limit") && e.Contains("DualPoint.PointB.Limit")), Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("measuring point")), Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("NoSuchProperty")), Join(report));
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Skipped), Join(report));
        }

        [TestMethod]
        public async Task BlockOnTopologyMismatch()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              { "version": 1, "id": "wrong-topo", "topology": "some-other-topology",
                                                "steps": [ { "set": "Counter.Counter", "value": 5 } ] }
                                              """);

            // A scenario only runs against the topology it declares — there is no "force" override. The web
            // caller brings the host to the right topology first (recycle-on-run); the in-process runner just
            // refuses a mismatch loudly.
            var blocked = await ScenarioRunner.RunAsync(scenario, host.Control);
            Assert.AreEqual(ScenarioRunStatus.TopologyMismatch, blocked.Status);
            Assert.AreEqual("scenario-topology", blocked.HostTopology);
            Assert.IsTrue(blocked.Steps.All(s => s.Status == ScenarioStepStatus.Skipped), Join(blocked));
        }

        [TestMethod]
        public async Task RunSetupStepsAndWaits_EndToEnd()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

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
        public async Task ObserveAFutureEventInWaitUntil()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        public async Task FailTheStepOnWaitUntilTimeout_AndSkipTheRest()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.AreEqual(ScenarioStepStatus.Failed, report.Steps[0].Status);
            StringAssert.Contains(report.Steps[0].Detail, "condition not met within 1 s");
            Assert.AreEqual(ScenarioStepStatus.Skipped, report.Steps[1].Status);
        }

        [TestMethod]
        public async Task ReachShadowedServicesViaQualifiedPaths()
        {
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

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual(3.5, host.Control.GetProperty("DualPoint", "PointA", "Limit"));
            Assert.AreEqual(7.5, host.Control.GetProperty("DualPoint", "PointB", "Limit"));
        }

        [TestMethod]
        public async Task ThrowFromApplyOnFailure_ForCSharpComposition()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              { "version": 1, "id": "broken", "topology": "scenario-topology",
                                                "steps": [ { "set": "NoSuchBlock.X", "value": 1 } ] }
                                              """);

            var e = await Assert.ThrowsExactlyAsync<ScenarioRunException>(() => ScenarioRunner.ApplyAsync(scenario, host.Control));
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