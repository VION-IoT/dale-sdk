using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The generic <c>serviceProviderExpect</c> assert step (RFC 0010): asserts the value a block last wrote
    ///     on any <c>[ServiceProviderContractType]</c> value output contract, read from the generic output cache
    ///     the stand-in fills. Replaces <c>digitalOutput</c> / <c>analogOutput</c>; an input is drive-only.
    /// </summary>
    [TestClass]
    public class ServiceProviderExpectStepShould
    {
        [TestMethod]
        public async Task AssertOutputs_DrivenAndAssertedEntirelyThroughTheGenericVocabulary()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            // Drive both HAL input families with serviceProviderSet, let the IoBlock's [Timer(1)] mirror them onto
            // its outputs, then assert both output families with serviceProviderExpect — the full generic loop.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect", "topology": "io",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true },
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "LevelInput" }, "value": 3.3 },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "equals": true } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "EchoOutput", "equals": 3.3, "tolerance": 0.001 } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("serviceProviderExpect", report.Steps[4].Kind);
            Assert.AreEqual("io.ActiveOutput", report.Steps[4].Target);
            Assert.AreEqual("== true", report.Steps[4].Argument);
            Assert.AreEqual("== 3.3 ±0.001", report.Steps[5].Argument);
        }

        [TestMethod]
        public async Task SupportEveryComparator_OnOutputs()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect-cmp", "topology": "io",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true },
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "LevelInput" }, "value": 3.3 },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "notEquals": false } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "oneOf": [true] } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "EchoOutput", "above": 3 } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "EchoOutput", "below": 4 } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        [TestMethod]
        public async Task FailLoudly_WhenTheOutputDoesNotHold()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            // ActiveOutput mirrors IsEnabled; we never enable it, so asserting it true must FAIL the run.
            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-expect-fail", "topology": "io",
                                                "steps": [
                                                  { "advance": { "seconds": 1 } },
                                                  { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "equals": true } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Failed, report.Status, Join(report));
            StringAssert.Contains(report.Steps[1].Detail!, "expected io.ActiveOutput to equal true");
        }

        [TestMethod]
        public async Task ResolveAnOutput_AndRejectAnExpectOnAnInput()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            var outputErrors = new List<string>();
            var output = resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "io", Contract = "ActiveOutput" } },
                                              "steps[0]",
                                              outputErrors);
            Assert.IsEmpty(outputErrors, string.Join("; ", outputErrors));
            Assert.IsNotNull(output.Contract);

            var inputErrors = new List<string>();
            resolver.ResolveStep(new ScenarioStep { ServiceProviderExpect = new ScenarioServiceProviderAssert { LogicBlock = "io", Contract = "EnableInput" } },
                                 "steps[0]",
                                 inputErrors);
            Assert.IsNotEmpty(inputErrors);
            StringAssert.Contains(inputErrors[0], "input");
        }

        [TestMethod]
        public void RejectAServiceProviderExpect_InSetup()
        {
            var error = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                               { "version": 1, "id": "bad", "topology": "io",
                                                                                                 "setup": [ { "serviceProviderExpect": { "logicBlock": "io", "contract": "ActiveOutput", "equals": true } } ] }
                                                                                               """));
            Assert.IsTrue(error.Errors.Any(e => e.Contains("setup entries stage state")), error.Message);
        }

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