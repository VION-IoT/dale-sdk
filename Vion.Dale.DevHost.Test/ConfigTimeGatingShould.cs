using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
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
        [TestProperty("spec", "AC-CTRL-019.2")]
        [TestProperty("spec", "AC-GATE-012.2")]
        [TestProperty("spec", "AC-GATE-012.3")]
        public async Task ShowExactlyIncludedComponentServicesForChosenParameter()
        {
            // Arrange / Act
            var included = await ResolveStationServices(2);

            // Assert
            Assert.Contains("GatedStationBlock", included); // the root service, which carries the parameter
            Assert.Contains("Point1", included);
            Assert.Contains("Point2", included);
            Assert.DoesNotContain("Point3", included); // gated out at PointCount = 2
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-019.2")]
        [TestProperty("spec", "AC-GATE-012.3")]
        [TestProperty("spec", "AC-GATE-012.4")]
        public async Task ResolveLiveViewAgainstChosenParameterValue()
        {
            // Arrange / Act
            var onePoint = await ResolveStationServices(1);
            var threePoints = await ResolveStationServices(3);

            // Assert
            Assert.DoesNotContain("Point2", onePoint);
            Assert.Contains("Point3", threePoints);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.9")]
        public async Task CarryChosenParameterValuesInConfigurationOutput()
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
            // Asserted on the serialized document, not the in-memory one: "omitted" is a property of what the
            // API writes, and an object that merely holds null serializes the key with a null value unless the
            // projection says otherwise.

            // Arrange
            var config = DevConfigurationBuilder.Create().WithTopologyName("gated").AddLogicBlock<SmokeHost.LogicBlocks.GatedStationBlock>("Station").Build();

            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            // Act
            var station = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "Station");
            var json = JsonSerializer.Serialize(station, new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

            // Assert
            Assert.IsNull(station.InstantiationParameters);
            Assert.DoesNotContain("instantiationParameters", json);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-012.5")]
        public async Task KeepMemberVisibleWhenItsGateCannotBeResolved()
        {
            // Arrange — a contract binding gated on an optional parameter no value has been chosen for. The
            // gate resolves syntactically, so every compile-time and bind-time check passes; evaluating it is
            // what fails, because the predicate profile treats a null reference as a hard error.
            var configuration = DevConfigurationBuilder.Create().AddLogicBlock<UnresolvableGateBlock>("gated").Build();
            await using var host = DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(configuration).Build();

            // Act
            var block = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "gated");

            // Assert — the development host stays fail-open; the running block is the strict gate.
            Assert.IsTrue(block.Contracts.Any(c => c.Identifier == "Demand"), "a member whose gate cannot be resolved stays visible");
            Assert.IsTrue(block.Contracts.Any(c => c.Identifier == "Ungated"), "an ungated member is unaffected");
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