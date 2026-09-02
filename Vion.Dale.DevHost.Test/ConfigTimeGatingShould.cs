using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The DevHost is the local stand-in for cloud-api's LiveViewResolver: a gated block's
    ///     introspection is filtered to the live view for its topology-set <c>[InstantiationParameter]</c>
    ///     values, so the UI shows exactly the included services (no dead gated-out slot) and the minted
    ///     service ids match the set the running block binds. Drives the committed SmokeHost gated block.
    /// </summary>
    [TestClass]
    public class ConfigTimeGatingShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.2")]
        [TestProperty("spec", "AC-GATE-012.3")]
        public async Task ShowExactlyTheIncludedComponentServices_ForTheTopologyParameter()
        {
            var included = await ResolveStationServices(2);

            Assert.Contains("GatedStationBlock", included); // the root service (carries the parameter)
            Assert.Contains("Point1", included);
            Assert.Contains("Point2", included);
            Assert.DoesNotContain("Point3", included); // gated out at PointCount = 2
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.3")]
        [TestProperty("spec", "AC-GATE-012.4")]
        public async Task ResolveTheLiveViewAgainstTheParameterValue()
        {
            Assert.DoesNotContain("Point2", await ResolveStationServices(1)); // only Point1
            Assert.Contains("Point3", await ResolveStationServices(3)); // full set
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.9")]
        public async Task CarryChosenParameterValuesInTheConfigurationOutput()
        {
            // Arrange
            // What `dale dev --export-topology` round-trips and the editor Save reads back — an instance's
            // chosen values have to survive the host, or a topology loses them on export.
            var config = DevConfigurationBuilder.Create().WithTopologyName("gated").AddLogicBlock<SmokeHost.LogicBlocks.GatedStationBlock>("Station").Build();
            config.LogicBlocks[0].InstantiationParameters = new Dictionary<string, JsonNode> { ["PointCount"] = JsonValue.Create(2L) };

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            // Act
            var station = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "Station");

            // Assert
            Assert.IsNotNull(station.InstantiationParameters);
            Assert.AreEqual(2L, station.InstantiationParameters!["PointCount"]!.GetValue<long>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.9")]
        public async Task OmitParameterValuesForInstanceThatChoseNone()
        {
            // Arrange
            var config = DevConfigurationBuilder.Create().WithTopologyName("gated").AddLogicBlock<SmokeHost.LogicBlocks.GatedStationBlock>("Station").Build();

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            // Act
            var station = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "Station");

            // Assert
            Assert.IsNull(station.InstantiationParameters);
        }

        private static async Task<HashSet<string>> ResolveStationServices(int pointCount)
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("gated").AddLogicBlock<SmokeHost.LogicBlocks.GatedStationBlock>("Station").Build();
            config.LogicBlocks[0].InstantiationParameters = new Dictionary<string, JsonNode> { ["PointCount"] = JsonValue.Create((long)pointCount) };

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            var station = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "Station");
            return station.Services.Select(s => s.Identifier).ToHashSet();
        }
    }
}