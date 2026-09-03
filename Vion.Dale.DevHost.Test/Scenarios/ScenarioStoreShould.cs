using System;
using System.IO;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Scenarios;
using Vion.Dale.DevHost.Topologies;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     What a scenario or topology <em>file</em> promises about its own identity on disk: an id that
    ///     addresses exactly one file, ordinally, and never a path outside its directory. Discovery, listing
    ///     and the read-only switch are the control API's, not this page's.
    /// </summary>
    [TestClass]
    public class ScenarioStoreShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.7")]
        [DataRow("../outside", DisplayName = "parent traversal")]
        [DataRow("..", DisplayName = "bare dot-dot")]
        [DataRow("nested/inside", DisplayName = "separator")]
        [DataRow("a..b", DisplayName = "dot-dot inside a slug")]
        [DataRow("", DisplayName = "empty")]
        public void RefuseIdResolvingOutsideItsOwnDirectory(string id)
        {
            // Arrange — a real directory holding one legitimate scenario, so a refusal cannot be "not found".
            var directory = NewDirectory();
            File.WriteAllText(Path.Combine(directory, "inside.scenario.json"), """{ "version": 1, "id": "inside", "topology": "t" }""");
            var store = new ScenarioStore(directory);

            // Act / Assert — every read, load and save path is confined by the same rule.
            Assert.IsNull(store.ReadRaw(id));
            Assert.IsNull(store.FileHash(id));
            Assert.ThrowsExactly<ScenarioFormatException>(() => store.LoadFile(id));
            Assert.ThrowsExactly<ScenarioFormatException>(() => store.Save(id, """{ "version": 1, "id": "inside", "topology": "t" }"""));
            Assert.AreEqual("inside", store.LoadFile("inside").Id);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.7")]
        [DataRow("../outside", DisplayName = "parent traversal")]
        [DataRow("a..b", DisplayName = "dot-dot inside a slug")]
        public void RefuseTopologyIdResolvingOutsideItsOwnDirectory(string id)
        {
            // Arrange
            var directory = NewDirectory();
            File.WriteAllText(Path.Combine(directory, "inside.topology.json"),
                              """{ "id": "inside", "logicBlockInstances": [{ "typeFullName": "X.Y", "name": "A" }] }""");
            var store = new DevTopologyStore(directory);

            // Act / Assert
            Assert.IsNull(store.ReadRaw(id));
            Assert.IsNotNull(store.ReadRaw("inside"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.6")]
        public void ResolveIdToItsFileNameOrdinally()
        {
            // Arrange — a differently-cased id must not resolve, or a file that loads on Windows 404s on
            // Linux CI. The file system may match the name case-insensitively; the store must not.
            var directory = NewDirectory();
            File.WriteAllText(Path.Combine(directory, "smoke.scenario.json"), """{ "version": 1, "id": "smoke", "topology": "t" }""");
            var store = new ScenarioStore(directory);

            // Act / Assert
            Assert.AreEqual("smoke", store.LoadFile("smoke").Id);
            Assert.IsNull(store.ReadRaw("Smoke"));
            Assert.ThrowsExactly<FileNotFoundException>(() => store.LoadFile("Smoke"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.6")]
        public void RefuseFileWhoseDeclaredIdDiffersFromItsName()
        {
            // Arrange
            var directory = NewDirectory();
            File.WriteAllText(Path.Combine(directory, "named.scenario.json"), """{ "version": 1, "id": "different", "topology": "t" }""");

            // Act / Assert
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => new ScenarioStore(directory).LoadFile("named"));
            StringAssert.Contains(refused.Message, "does not match the file name (expected 'named')");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.15")]
        public async Task CarryFileContentHashOnRunReport()
        {
            // Arrange — the same content under two ids hashes the same; a changed byte changes the hash.
            var directory = NewDirectory();
            const string body = """{ "version": 1, "id": "{0}", "topology": "elsewhere", "steps": [{ "advance": { "seconds": 1 } }] }""";
            File.WriteAllText(Path.Combine(directory, "first.scenario.json"), body.Replace("{0}", "first"));
            File.WriteAllText(Path.Combine(directory, "second.scenario.json"), body.Replace("{0}", "second"));

            await using var host = BuildHost();
            await host.StartAsync();

            // Act
            var first = await ScenarioRunner.RunAsync("first", host.Control, directory);
            var second = await ScenarioRunner.RunAsync("second", host.Control, directory);
            var byInstance = await ScenarioRunner.RunAsync(ScenarioFile.Parse(body.Replace("{0}", "first")), host.Control);

            // Assert — the hash pins a verification report to an exact file version, and is absent when the
            // caller handed over an in-memory scenario with no file behind it.
            Assert.IsNotNull(first.FileHash);
            Assert.AreEqual(40, first.FileHash!.Length, first.FileHash);
            Assert.AreNotEqual(first.FileHash, second.FileHash);
            Assert.IsNull(byInstance.FileHash);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-009.15")]
        public void ReportNoHashForAbsentFile()
        {
            // Arrange
            var store = new ScenarioStore(NewDirectory());

            // Act / Assert
            Assert.IsNull(store.FileHash("absent"));
        }

        private static IDevHost BuildHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("store-topology").AddLogicBlock<CounterBlock>("Counter").Build();

            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(config).Build();
        }

        private static string NewDirectory()
        {
            var directory = Path.Combine(Path.GetTempPath(), "scen-store-" + Path.GetRandomFileName());
            Directory.CreateDirectory(directory);
            return directory;
        }
    }
}
