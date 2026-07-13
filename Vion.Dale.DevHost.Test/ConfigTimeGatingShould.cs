using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     RFC 0016 — the DevHost is the local stand-in for cloud-api's LiveViewResolver: a gated block's
    ///     introspection is filtered to the live view for its topology-set <c>[InstantiationParameter]</c>
    ///     values, so the UI shows exactly the included services (no dead gated-out slot) and the minted
    ///     service ids match the set the running block binds. Drives the committed SmokeHost gated block.
    /// </summary>
    [TestClass]
    public class ConfigTimeGatingShould
    {
        [TestMethod]
        public async Task ShowExactlyTheIncludedComponentServices_ForTheTopologyParameter()
        {
            var included = await ResolveStationServices(2);

            Assert.Contains("GatedStationBlock", included); // the root service (carries the parameter)
            Assert.Contains("Point1", included);
            Assert.Contains("Point2", included);
            Assert.DoesNotContain("Point3", included); // gated out at PointCount = 2
        }

        [TestMethod]
        public async Task ResolveTheLiveViewAgainstTheParameterValue()
        {
            Assert.DoesNotContain("Point2", await ResolveStationServices(1)); // only Point1
            Assert.Contains("Point3", await ResolveStationServices(3)); // full set
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