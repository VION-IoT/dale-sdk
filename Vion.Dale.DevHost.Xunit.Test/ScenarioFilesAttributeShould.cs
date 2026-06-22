using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Xunit;
using Xunit;
using Xunit.Sdk;

namespace Vion.Dale.DevHost.Xunit.Test
{
    [TestClass]
    public class ScenarioFilesAttributeShould
    {
        // GetData ignores the test method; any MethodInfo satisfies the signature.
        private static readonly MethodInfo AnyMethod = typeof(ScenarioFilesAttributeShould).GetMethods()[0];

        [TestMethod]
        public async Task Discover_every_committed_scenario_as_an_id_topology_row()
        {
            var rows = await Discover();

            var byId = rows.ToDictionary(r => (string)r.GetData()[0]!, r => (string)r.GetData()[1]!);

            CollectionAssert.AreEquivalent(new[] { "showcase-tour", "io-control", "grid-demand", "minimal-subset" }, byId.Keys.ToList());
            Assert.AreEqual("default", byId["showcase-tour"]);
            Assert.AreEqual("default", byId["io-control"]);
            Assert.AreEqual("default", byId["grid-demand"]);
            Assert.AreEqual("minimal", byId["minimal-subset"]);
        }

        [TestMethod]
        public async Task Use_the_scenario_title_as_the_display_name()
        {
            var rows = await Discover();

            var showcase = rows.Single(r => (string)r.GetData()[0]! == "showcase-tour");

            Assert.IsNotNull(showcase.TestDisplayName);
            StringAssert.Contains(showcase.TestDisplayName!, "Showcase tour");
        }

        [TestMethod]
        public async Task Filter_to_a_single_topology_when_requested()
        {
            var defaultRows = await Discover("default");
            CollectionAssert.AreEquivalent(new[] { "showcase-tour", "io-control", "grid-demand" }, defaultRows.Select(r => (string)r.GetData()[0]!).ToList());

            var minimalRows = await Discover("minimal");
            Assert.HasCount(1, minimalRows);
            Assert.AreEqual("minimal-subset", (string)minimalRows[0].GetData()[0]!);
        }

        private static async Task<IReadOnlyList<ITheoryDataRow>> Discover(string? topology = null)
        {
            var attribute = new ScenarioFilesAttribute { Directory = SmokeData.ScenariosDir, Topology = topology };
            var rows = await attribute.GetData(AnyMethod, new DisposalTracker());
            return rows.ToList();
        }
    }
}