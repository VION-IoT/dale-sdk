using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The generic <c>serviceProviderSet</c> drive step (RFC 0010): one step kind drives any
    ///     <c>[ServiceProviderContractType]</c> value input contract — replacing the per-family
    ///     <c>digitalInput</c> / <c>analogInput</c>. Direction is read off the contract (an output is assert-only),
    ///     and the drive is routed to the generic stand-in registered under the contract's
    ///     <c>ContractHandlerActorName</c>.
    /// </summary>
    [TestClass]
    public class ServiceProviderSetStepShould
    {
        [TestMethod]
        public async Task DriveAValueInputContract_ReachingTheConsumingBlock()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "sp-set", "topology": "io",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true },
                                                  { "waitUntil": { "property": "io.IsEnabled", "equals": true }, "timeoutSeconds": 5 },
                                                  { "expect": { "property": "io.IsEnabled", "equals": true } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
            Assert.AreEqual("serviceProviderSet", report.Steps[0].Kind);
            Assert.AreEqual("io.EnableInput", report.Steps[0].Target);
            Assert.AreEqual("true", report.Steps[0].Argument);
        }

        [TestMethod]
        public async Task ResolveAnInputToItsHandlerName_AndRejectADriveOnAnOutput()
        {
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();
            var resolver = new ScenarioResolver(host.Control.GetConfiguration());

            // EnableInput is a value input (ZeroOrMore) → drivable; resolves to its generic stand-in name.
            var inputErrors = new List<string>();
            var input = resolver.ResolveStep(new ScenarioStep { ServiceProviderSet = new ScenarioServiceProviderRef { LogicBlock = "io", Contract = "EnableInput" } },
                                             "steps[0]",
                                             inputErrors);
            Assert.IsEmpty(inputErrors, string.Join("; ", inputErrors));
            Assert.IsNotNull(input.Contract);
            Assert.AreEqual("DigitalInputHandler", input.Contract!.HandlerName);

            // ActiveOutput is a single-writer output (ZeroOrOne) → serviceProviderSet must be rejected loudly.
            var outputErrors = new List<string>();
            resolver.ResolveStep(new ScenarioStep { ServiceProviderSet = new ScenarioServiceProviderRef { LogicBlock = "io", Contract = "ActiveOutput" } },
                                 "steps[0]",
                                 outputErrors);
            Assert.IsNotEmpty(outputErrors);
            StringAssert.Contains(outputErrors[0], "output");
        }

        [TestMethod]
        public void RequireAValue_OnAServiceProviderSetStep()
        {
            // Parses with a value.
            ScenarioFile.Parse("""
                               { "version": 1, "id": "ok", "topology": "io",
                                 "steps": [ { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" }, "value": true } ] }
                               """);

            // Missing value is a structural error.
            var error = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                               { "version": 1, "id": "bad", "topology": "io",
                                                                                                 "steps": [ { "serviceProviderSet": { "logicBlock": "io", "contract": "EnableInput" } } ] }
                                                                                               """));
            Assert.IsTrue(error.Errors.Any(e => e.Contains("serviceProviderSet requires value")), error.Message);
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