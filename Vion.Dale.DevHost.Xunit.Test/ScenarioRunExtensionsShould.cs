using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Xunit.Test
{
    /// <summary>
    ///     The run and assert helpers a consumer's test body is made of: one call to run a committed scenario
    ///     on a host, and one that turns a failed report into a test failure carrying the diagnosis.
    /// </summary>
    [TestClass]
    public class ScenarioRunExtensionsShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.5")]
        public async Task RunCommittedScenarioOnHostBuiltByFixture()
        {
            // Arrange
            var fixture = new SmokeScenarioFixture();
            await using var host = await fixture.LoadAsync("default", true, SmokeData.TopologiesDir);

            // Act
            var report = await host.RunScenarioAsync("showcase-tour", SmokeData.ScenariosDir);

            // Assert
            Assert.AreEqual(ScenarioRunStatus.Succeeded, report.Status, string.Join("; ", report.ValidationErrors));
            report.AssertSucceeded();
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.4")]
        public void ThrowWithDetailAndReportOnFailedRun()
        {
            // Arrange
            var report = new ScenarioRunReport
                         {
                             ScenarioId = "broken",
                             Status = ScenarioRunStatus.Failed,
                             ValidationErrors = new[] { "no logic block named Foo" },
                         };

            // Act / Assert
            var exception = Assert.ThrowsExactly<ScenarioRunException>(() => report.AssertSucceeded());
            StringAssert.Contains(exception.Message, "no logic block named Foo");
            Assert.AreSame(report, exception.Report);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.8")]
        public async Task ReportSuccessWithJudgmentsStillAwaitingHuman()
        {
            // Arrange - showcase-tour carries judgment items, which no run can satisfy on its own.
            var fixture = new SmokeScenarioFixture();
            await using var host = await fixture.LoadAsync("default", true, SmokeData.TopologiesDir);

            // Act
            var report = await host.RunScenarioAsync("showcase-tour", SmokeData.ScenariosDir);

            // Assert - a consumer's suite is green with unmet judgments, by design: the verdict is a human's.
            Assert.IsNotEmpty(report.Judge);
            Assert.IsTrue(report.Judge.All(j => j.Status == "requiresHuman"), string.Join("; ", report.Judge.Select(j => j.Status)));
            report.AssertSucceeded();
        }
    }
}