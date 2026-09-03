using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Xunit;
using Xunit;
using Xunit.Sdk;

namespace Vion.Dale.DevHost.Xunit.Test
{
    /// <summary>
    ///     The theory data source a consumer points at their committed scenarios: one test case per file,
    ///     carrying the id and the topology the case needs a host on, named by the scenario's title, and
    ///     tagged with the scenario's own trace ids.
    /// </summary>
    [TestClass]
    public class ScenarioFilesAttributeShould
    {
        // GetData ignores the test method; any MethodInfo satisfies the signature.
        private static readonly MethodInfo AnyMethod = typeof(ScenarioFilesAttributeShould).GetMethods()[0];

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.1")]
        public async Task YieldOneRowPerCommittedScenarioCarryingIdAndTopology()
        {
            // Arrange / Act
            var rows = await Discover();

            // Assert
            var byId = rows.ToDictionary(r => (string)r.GetData()[0]!, r => (string)r.GetData()[1]!);
            CollectionAssert.AreEquivalent(new[]
                                           {
                                               "showcase-tour", "io-control", "output-confirmation", "provider-faces", "paired-loop", "grid-demand", "plant-control",
                                               "minimal-subset",
                                           },
                                           byId.Keys.ToList());
            Assert.AreEqual("default", byId["showcase-tour"]);
            Assert.AreEqual("default", byId["io-control"]);
            Assert.AreEqual("default", byId["output-confirmation"]);
            Assert.AreEqual("default", byId["provider-faces"]);
            Assert.AreEqual("paired", byId["paired-loop"]);
            Assert.AreEqual("default", byId["grid-demand"]);
            Assert.AreEqual("default", byId["plant-control"]);
            Assert.AreEqual("minimal", byId["minimal-subset"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.1")]
        public async Task NameEachRowByItsScenarioTitle()
        {
            // Arrange / Act
            var rows = await Discover();

            // Assert
            var showcase = rows.Single(r => (string)r.GetData()[0]! == "showcase-tour");
            Assert.IsNotNull(showcase.TestDisplayName);
            StringAssert.Contains(showcase.TestDisplayName!, "Showcase tour");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.2")]
        public async Task CarryEachScenariosTraceIdsAsTraits()
        {
            // Arrange / Act
            var rows = await Discover();

            // Assert — the scenario's own `specs` array, which is how a consumer filters a run by spec.
            var pairedLoop = rows.Single(r => (string)r.GetData()[0]! == "paired-loop");
            Assert.IsNotNull(pairedLoop.Traits);
            CollectionAssert.AreEquivalent(new[] { "smoke", "pairing" }, pairedLoop.Traits!["spec"].ToList());

            var minimal = rows.Single(r => (string)r.GetData()[0]! == "minimal-subset");
            CollectionAssert.AreEquivalent(new[] { "smoke", "topology" }, minimal.Traits!["spec"].ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.3")]
        public async Task RestrictRowsToOneTopologyWhenNamed()
        {
            // Arrange / Act
            var defaultRows = await Discover("default");
            var pairedRows = await Discover("paired");
            var minimalRows = await Discover("minimal");

            // Assert
            CollectionAssert.AreEquivalent(new[] { "showcase-tour", "io-control", "output-confirmation", "provider-faces", "grid-demand", "plant-control" },
                                           defaultRows.Select(r => (string)r.GetData()[0]!).ToList());
            Assert.HasCount(1, pairedRows);
            Assert.AreEqual("paired-loop", (string)pairedRows[0].GetData()[0]!);
            Assert.HasCount(1, minimalRows);
            Assert.AreEqual("minimal-subset", (string)minimalRows[0].GetData()[0]!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.3")]
        public async Task OmitBrokenAndTopologylessScenariosFromRows()
        {
            // Arrange — a directory holding one runnable scenario beside a broken one and one that declares
            // no topology. Catching those two is `dale scenario validate`'s job in CI, not the runner's.
            var directory = Path.Combine(Path.GetTempPath(), "scen-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "runnable.scenario.json"),
                              """{ "version": 1, "id": "runnable", "topology": "default", "title": "Runnable" }""");
            File.WriteAllText(Path.Combine(directory, "broken.scenario.json"), """{ "version": 1, "id": "broken" """);
            File.WriteAllText(Path.Combine(directory, "homeless.scenario.json"), """{ "version": 1, "id": "homeless", "topology": "" }""");

            try
            {
                // Act
                var attribute = new ScenarioFilesAttribute { Directory = directory };
                var rows = (await attribute.GetData(AnyMethod, new DisposalTracker())).ToList();

                // Assert
                CollectionAssert.AreEqual(new[] { "runnable" }, rows.Select(r => (string)r.GetData()[0]!).ToList());
            }
            finally
            {
                Directory.Delete(directory, true);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.4")]
        public void SurfaceRowsAtDiscoveryTime()
        {
            // Arrange
            var attribute = new ScenarioFilesAttribute { Directory = SmokeData.ScenariosDir };

            // Act / Assert — without this, every scenario would collapse into one unnamed theory entry.
            Assert.IsTrue(attribute.SupportsDiscoveryEnumeration());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-016.6")]
        public async Task ResolveScenariosDirectoryFromWorkingDirectoryUpward()
        {
            // Arrange — no explicit directory, and the test host's working directory is its bin folder, which
            // holds the copied `scenarios/`. A consumer's project root behaves the same way.
            var previous = Directory.GetCurrentDirectory();
            Directory.SetCurrentDirectory(Path.GetDirectoryName(SmokeData.ScenariosDir)!);

            try
            {
                // Act
                var rows = (await new ScenarioFilesAttribute().GetData(AnyMethod, new DisposalTracker())).ToList();

                // Assert
                CollectionAssert.Contains(rows.Select(r => (string)r.GetData()[0]!).ToList(), "showcase-tour");
            }
            finally
            {
                Directory.SetCurrentDirectory(previous);
            }
        }

        private static async Task<IReadOnlyList<ITheoryDataRow>> Discover(string? topology = null)
        {
            var attribute = new ScenarioFilesAttribute { Directory = SmokeData.ScenariosDir, Topology = topology };
            var rows = await attribute.GetData(AnyMethod, new DisposalTracker());
            return rows.ToList();
        }
    }
}
