using System.Threading.Tasks;
using Vion.Dale.DevHost;

namespace Vion.Dale.DevHost.Xunit.Test
{
    /// <summary>
    ///     The fixture a consumer derives from: a host factory, not a host. Each load hands back a fresh host
    ///     on the named topology, in the requested clock mode, which the caller owns and disposes.
    /// </summary>
    [TestClass]
    public class DevHostScenarioFixtureShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.5")]
        [DataRow("default", false, DisplayName = "real clock by default")]
        [DataRow("minimal", true, DisplayName = "stepped when requested")]
        public async Task LoadNamedTopologyInRequestedClockMode(string topology, bool stepped)
        {
            // Arrange
            var fixture = new SmokeScenarioFixture();

            // Act
            await using var host = await fixture.LoadAsync(topology, stepped, SmokeData.TopologiesDir);

            // Assert
            Assert.AreEqual(stepped, host.Control.IsStepped);
            Assert.AreEqual(topology, host.Control.GetConfiguration().TopologyName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.5")]
        public async Task HandBackFreshHostPerLoadFromFixtureHoldingNone()
        {
            // Arrange - one fixture, shared the way an IClassFixture is.
            var fixture = new SmokeScenarioFixture();

            // Act
            await using var first = await fixture.LoadAsync("minimal", true, SmokeData.TopologiesDir);
            await using var second = await fixture.LoadAsync("minimal", true, SmokeData.TopologiesDir);

            // Assert - two independent hosts, so two scenarios can never interleave on a shared network.
            Assert.AreNotSame(first, second);
            Assert.AreNotSame(first.Control, second.Control);
        }
    }
}