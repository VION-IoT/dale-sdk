using System.Threading.Tasks;
using Vion.Dale.DevHost;

namespace Vion.Dale.DevHost.Xunit.Test
{
    [TestClass]
    public class DevHostScenarioFixtureShould
    {
        [TestMethod]
        public async Task Load_a_named_topology_on_a_real_clock_by_default()
        {
            var fixture = new SmokeScenarioFixture();

            await using var host = await fixture.LoadAsync("default", topologiesDir: SmokeData.TopologiesDir);

            Assert.IsFalse(host.Control.IsStepped);
            Assert.AreEqual("default", host.Control.GetConfiguration().TopologyName);
        }

        [TestMethod]
        public async Task Load_a_stepped_host_when_requested()
        {
            var fixture = new SmokeScenarioFixture();

            await using var host = await fixture.LoadAsync("minimal", true, SmokeData.TopologiesDir);

            Assert.IsTrue(host.Control.IsStepped);
            Assert.AreEqual("minimal", host.Control.GetConfiguration().TopologyName);
        }
    }
}