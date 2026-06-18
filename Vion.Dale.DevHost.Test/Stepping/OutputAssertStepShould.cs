using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The <c>digitalOutput</c> / <c>analogOutput</c> auto-assert scenario steps — the read half of HAL
    ///     testing, symmetric with <c>digitalInput</c> / <c>analogInput</c> (drive). Each is a contract-ref +
    ///     comparator that reads the mocked output the block last Set (via the control's
    ///     <c>GetDigitalOutput</c> / <c>GetAnalogOutput</c> cache) and FAILS the run if the comparator does not
    ///     hold — the CI-failing tier, like <c>expect</c>. Covers the comparators, the failing-detail value,
    ///     contract-type resolution, the report rendering, and the structural validation.
    /// </summary>
    [TestClass]
    public class OutputAssertStepShould
    {
        // ── end-to-end against the committed SmokeHost IoBlock (drive inputs → timer mirrors → assert outputs) ──

        [TestMethod]
        public async Task AssertDigitalAndAnalogOutputs_AfterTheTimerMirrorsTheInputs()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            // The IoBlock's [Timer(1)] OnTick mirrors IsEnabled -> ActiveOutput and CurrentLevel -> EchoOutput.
            // Drive the inputs, wait for the block to observe them, advance one virtual second to fire the timer,
            // then ASSERT the mocked outputs carry the mirrored values.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "io-out", "topology": "io",
                                                "steps": [
                                                  { "digitalInput": { "block": "io", "contract": "EnableInput" }, "value": true },
                                                  { "analogInput": { "block": "io", "contract": "LevelInput" }, "value": 3.3 },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "digitalOutput": { "block": "io", "contract": "ActiveOutput", "equals": true } },
                                                  { "analogOutput": { "block": "io", "contract": "EchoOutput", "equals": 3.3, "tolerance": 0.001 } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Ok), Join(report));

            // The report names the contract and renders the comparator.
            Assert.AreEqual("digitalOutput", report.Steps[4].Kind);
            Assert.AreEqual("io.ActiveOutput", report.Steps[4].Target);
            Assert.AreEqual("== true", report.Steps[4].Argument);
            Assert.AreEqual("analogOutput", report.Steps[5].Kind);
            Assert.AreEqual("io.EchoOutput", report.Steps[5].Target);
            Assert.AreEqual("== 3.3 ±0.001", report.Steps[5].Argument);
        }

        [TestMethod]
        public async Task SupportEveryComparator_OnOutputs()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "io-out-comparators", "topology": "io",
                                                "steps": [
                                                  { "digitalInput": { "block": "io", "contract": "EnableInput" }, "value": true },
                                                  { "analogInput": { "block": "io", "contract": "LevelInput" }, "value": 3.3 },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "digitalOutput": { "block": "io", "contract": "ActiveOutput", "notEquals": false } },
                                                  { "digitalOutput": { "block": "io", "contract": "ActiveOutput", "oneOf": [true] } },
                                                  { "analogOutput": { "block": "io", "contract": "EchoOutput", "above": 3 } },
                                                  { "analogOutput": { "block": "io", "contract": "EchoOutput", "below": 4 } },
                                                  { "analogOutput": { "block": "io", "contract": "EchoOutput", "oneOf": [3.3] } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.IsTrue(report.Steps.All(s => s.Status == ScenarioStepStatus.Ok), Join(report));
        }

        [TestMethod]
        public async Task FailTheRun_WhenTheOutputDoesNotMatch_WithTheValueInTheDetail()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            // ActiveOutput becomes true after the timer; asserting it equals false must fail and carry the value.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "io-out-fail", "topology": "io",
                                                "steps": [
                                                  { "digitalInput": { "block": "io", "contract": "EnableInput" }, "value": true },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "label": "must be off", "digitalOutput": { "block": "io", "contract": "ActiveOutput", "equals": false } },
                                                  { "analogOutput": { "block": "io", "contract": "EchoOutput", "equals": 9.9 } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.AreEqual(ScenarioStepStatus.Failed, report.Steps[3].Status, Join(report));
            StringAssert.Contains(report.Steps[3].Detail, "io.ActiveOutput");
            StringAssert.Contains(report.Steps[3].Detail, "equal false");
            StringAssert.Contains(report.Steps[3].Detail, "but was True");
            Assert.AreEqual(ScenarioStepStatus.Skipped, report.Steps[4].Status, Join(report));
        }

        [TestMethod]
        public async Task FailValidation_WhenAnOutputStepReferencesANonOutputContract()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            // EnableInput is a DigitalInput, not a DigitalOutput — the resolver's contract-type guard must reject it.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "io-out-wrongtype", "topology": "io",
                                                "steps": [ { "digitalOutput": { "block": "io", "contract": "EnableInput", "equals": true } } ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            Assert.IsTrue(report.ValidationErrors.Any(e => e.Contains("DigitalInput") && e.Contains("DigitalOutput")), Join(report));
        }

        [TestMethod]
        public async Task FailingDetail_NamesTheContractAndValue_ForEveryComparator()
        {
            // OutputFailureDetail is a hand-written near-clone of ExpectFailureDetail; pin every branch so a
            // copy/drift bug (wrong operator word, swapped above/below) can't ship green. One failing step skips
            // the rest, so run a separate single-assert scenario per comparator against the same primed host.
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var prime = ScenarioFile.Parse("""
                                           {
                                             "version": 1, "id": "prime", "topology": "io",
                                             "steps": [
                                               { "digitalInput": { "block": "io", "contract": "EnableInput" }, "value": true },
                                               { "analogInput": { "block": "io", "contract": "LevelInput" }, "value": 3.3 },
                                               { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                               { "advance": { "seconds": 1 } }
                                             ]
                                           }
                                           """);
            Assert.AreEqual(ScenarioRunStatus.Succeeded, (await ScenarioRunner.RunAsync(prime, host.Control)).Status);

            async Task<string> FailDetail(string assertJson)
            {
                var scenario = ScenarioFile.Parse($$"""{ "version": 1, "id": "f", "topology": "io", "steps": [ {{assertJson}} ] }""");
                var report = await ScenarioRunner.RunAsync(scenario, host.Control);
                Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
                return report.Steps[0].Detail ?? string.Empty;
            }

            var above = await FailDetail("""{ "analogOutput": { "block": "io", "contract": "EchoOutput", "above": 9 } }""");
            StringAssert.Contains(above, "io.EchoOutput above 9");
            StringAssert.Contains(above, "but was 3.3");

            var below = await FailDetail("""{ "analogOutput": { "block": "io", "contract": "EchoOutput", "below": 1 } }""");
            StringAssert.Contains(below, "io.EchoOutput below 1");
            StringAssert.Contains(below, "but was 3.3");

            var notEquals = await FailDetail("""{ "digitalOutput": { "block": "io", "contract": "ActiveOutput", "notEquals": true } }""");
            StringAssert.Contains(notEquals, "io.ActiveOutput to not equal true");
            StringAssert.Contains(notEquals, "but was True");

            var oneOf = await FailDetail("""{ "analogOutput": { "block": "io", "contract": "EchoOutput", "oneOf": [1, 2] } }""");
            StringAssert.Contains(oneOf, "io.EchoOutput to be one of [1, 2]");
            StringAssert.Contains(oneOf, "but was 3.3");
        }

        [TestMethod]
        public async Task FailWithNull_WhenAssertingAnOutputTheBlockNeverSet()
        {
            // Asserting a still-default output (no preceding input/advance, so OnTick never fired) is a common
            // authoring mistake — the runner must report it cleanly as "but was null", not throw or false-pass.
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "io-out-null", "topology": "io",
                                                "steps": [ { "digitalOutput": { "block": "io", "contract": "ActiveOutput", "equals": true } } ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[0].Detail, "io.ActiveOutput");
            StringAssert.Contains(report.Steps[0].Detail, "but was null");
        }

        // ── structural validation (no host) ────────────────────────────────────────────────────────────────

        [TestMethod]
        public void ParseOutputAsserts_RoundTrip()
        {
            var file = ScenarioFile.Parse("""
                                          {
                                            "version": 1, "id": "x", "topology": "t",
                                            "steps": [
                                              { "digitalOutput": { "block": "Light", "contract": "DigitalOutput", "equals": true } },
                                              { "analogOutput": { "block": "Io", "contract": "EchoOutput", "equals": 3.3, "tolerance": 0.001 } }
                                            ]
                                          }
                                          """);

            Assert.AreEqual("digitalOutput", file.Steps![0].Kind);
            Assert.AreEqual("Light", file.Steps[0].DigitalOutput!.Block);
            Assert.AreEqual("DigitalOutput", file.Steps[0].DigitalOutput!.Contract);
            Assert.AreEqual("analogOutput", file.Steps[1].Kind);
            Assert.AreEqual("Io", file.Steps[1].AnalogOutput!.Block);
            Assert.AreEqual(0.001, file.Steps[1].AnalogOutput!.Tolerance!.Value, 0.0001);
        }

        [TestMethod]
        public void RejectOutputAssertsInSetup()
        {
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                           {
                                                                                             "version": 1, "id": "x", "topology": "t",
                                                                                             "setup": [ { "digitalOutput": { "block": "A", "contract": "B", "equals": true } } ]
                                                                                           }
                                                                                           """));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("setup entries")), string.Join("; ", e.Errors));
        }

        [TestMethod]
        public void RejectTwoComparators()
        {
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                           {
                                                                                             "version": 1, "id": "x", "topology": "t",
                                                                                             "steps": [ { "analogOutput": { "block": "A", "contract": "B", "above": 1, "below": 2 } } ]
                                                                                           }
                                                                                           """));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("exactly one of above")), string.Join("; ", e.Errors));
        }

        [TestMethod]
        public void RejectMissingBlockOrContract()
        {
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                           {
                                                                                             "version": 1, "id": "x", "topology": "t",
                                                                                             "steps": [ { "digitalOutput": { "equals": true } } ]
                                                                                           }
                                                                                           """));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("digitalOutput.block is required")), string.Join("; ", e.Errors));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("digitalOutput.contract is required")), string.Join("; ", e.Errors));
        }

        [TestMethod]
        public void RejectATopLevelValueOnAnOutputStep()
        {
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                           {
                                                                                             "version": 1, "id": "x", "topology": "t",
                                                                                             "steps": [ { "digitalOutput": { "block": "A", "contract": "B", "equals": true }, "value": true } ]
                                                                                           }
                                                                                           """));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("value is not valid on a digitalOutput step")), string.Join("; ", e.Errors));
        }

        [TestMethod]
        public void RejectAPathComparand_OnOutputs()
        {
            // Outputs take literals only — no relational {path} comparand (that is expect-only).
            var e = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                           {
                                                                                             "version": 1, "id": "x", "topology": "t",
                                                                                             "steps": [ { "analogOutput": { "block": "A", "contract": "B", "above": { "path": "A.C" } } } ]
                                                                                           }
                                                                                           """));
            Assert.IsTrue(e.Errors.Any(m => m.Contains("must be a number")), string.Join("; ", e.Errors));
        }

        // ── Helpers ────────────────────────────────────────────────────────────────────────────────────────

        private static IDevHost BuildSteppedIoHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("io").AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io").Build();
            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        private static string Join(ScenarioRunReport report)
        {
            var steps = report.Setup.Concat(report.Steps).Select(s => $"[{s.Index} {s.Kind} {s.Status}: {s.Detail}]");
            return string.Join("; ", report.ValidationErrors.Concat(steps));
        }
    }
}