using System.Threading.Tasks;
using Vion.Dale.DevHost.Xunit;
using Xunit.Sdk;

namespace Vion.Dale.DevHost.Xunit.Test
{
    /// <summary>
    ///     End-to-end proof that the package's three pieces compose the way a consumer would use them: discover
    ///     every committed scenario with <see cref="ScenarioFilesAttribute" />, build the host for each row's
    ///     topology via the fixture, run it, and assert success — the in-process equivalent of the consumer's
    ///     hand-rolled CommittedScenariosShould theory, now with zero boilerplate.
    /// </summary>
    [TestClass]
    public class CommittedScenariosShould
    {
        [TestMethod]
        public async Task Run_green_through_the_fixture_and_run_helpers()
        {
            var attribute = new ScenarioFilesAttribute { Directory = SmokeData.ScenariosDir };
            var rows = await attribute.GetData(typeof(CommittedScenariosShould).GetMethods()[0], new DisposalTracker());

            var fixture = new SmokeScenarioFixture();
            var ran = 0;

            foreach (var row in rows)
            {
                var data = row.GetData();
                var id = (string)data[0]!;
                var topology = (string)data[1]!;

                // One fresh host per scenario, disposed before the next — never two live at once (the serial
                // discipline the runner enforces with its one-active-run-per-host guard).
                await using (var host = await fixture.LoadAsync(topology, true, SmokeData.TopologiesDir))
                {
                    (await host.RunScenarioAsync(id, SmokeData.ScenariosDir)).AssertSucceeded();
                }

                ran++;
            }

            Assert.AreEqual(3, ran);
        }
    }
}