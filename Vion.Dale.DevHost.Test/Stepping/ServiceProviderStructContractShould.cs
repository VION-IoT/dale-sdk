using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The RFC 0010 / DF-27 unblock proven end to end through the real DevHost: a synthetic third-party
    ///     value contract whose wire payload is a multi-field struct with a 1-level nested struct + an enum is
    ///     driven by <c>serviceProviderSet</c> into its consuming block, which surfaces every field — including
    ///     the nested ones — as service properties. The non-HAL, struct case the four old HAL step kinds could
    ///     never address; complements the codec/handler unit tests with the full host path.
    /// </summary>
    [TestClass]
    public class ServiceProviderStructContractShould
    {
        [TestMethod]
        [TestCategory("Smoke")]
        public async Task DriveAMultiFieldNestedStructContract_ReachingTheConsumingBlock()
        {
            await using var host = BuildSteppedGridHost();
            await host.StartAsync();

            var scenario = ScenarioFile.Parse("""
                                              {
                                                "version": 1, "id": "grid", "topology": "grid",
                                                "steps": [
                                                  { "serviceProviderSet": { "logicBlock": "grid", "contract": "Demand" },
                                                    "value": { "valid": true, "scope": "PerPhase", "limits": { "activePowerW": 1500, "reactivePowerVar": 200 } } },
                                                  { "waitUntil": { "property": "grid.DemandValid", "equals": true }, "timeoutSeconds": 5 },
                                                  { "expect": { "property": "grid.Scope", "equals": "PerPhase" } },
                                                  { "expect": { "property": "grid.ActivePowerW", "equals": 1500, "tolerance": 0.01 } },
                                                  { "expect": { "property": "grid.ReactivePowerVar", "equals": 200, "tolerance": 0.01 } }
                                                ]
                                              }
                                              """);

            var report = await ScenarioRunner.RunAsync(scenario, host.Control);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, Join(report));
        }

        private static IDevHost BuildSteppedGridHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("grid").AddLogicBlock<SmokeHost.LogicBlocks.GridBlock>("grid").Build();
            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }

        private static string Join(ScenarioRunReport report)
        {
            var steps = report.Setup.Concat(report.Steps).Select(s => $"[{s.Index} {s.Kind} {s.Status}: {s.Detail}]");
            return string.Join("; ", report.ValidationErrors.Concat(steps));
        }
    }
}