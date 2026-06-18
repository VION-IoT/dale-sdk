using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Xunit.Test
{
    [TestClass]
    public class ScenarioRunExtensionsShould
    {
        [TestMethod]
        public async Task Run_a_committed_scenario_to_success()
        {
            var fixture = new SmokeScenarioFixture();
            await using var host = await fixture.LoadAsync("default", true, SmokeData.TopologiesDir);

            var report = await host.RunScenarioAsync("showcase-tour", SmokeData.ScenariosDir);

            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, string.Join("; ", report.ValidationErrors));
            report.AssertSucceeded();
        }

        [TestMethod]
        public void AssertSucceeded_throws_with_detail_on_a_failed_report()
        {
            var report = new ScenarioRunReport
                         {
                             ScenarioId = "broken",
                             Status = ScenarioRunStatus.Failed,
                             ValidationErrors = new[] { "no logic block named Foo" },
                         };

            var exception = Assert.ThrowsExactly<ScenarioRunException>(() => report.AssertSucceeded());

            StringAssert.Contains(exception.Message, "no logic block named Foo");
            Assert.AreSame(report, exception.Report);
        }
    }
}