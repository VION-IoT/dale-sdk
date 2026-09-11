using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands;
using Vion.Dale.Cli.Output;

namespace Vion.Dale.Cli.Test.Commands
{
    /// <summary>
    ///     The `dale topology` verbs as wired into the CLI. Every test here runs with no DevHost anywhere:
    ///     both subcommands address no host at all, which is the guarantee the verbs exist for — a consumer
    ///     gating hand-edited topology files in a fast PR lane.
    /// </summary>
    [TestClass]
    public class TopologyCommandTests
    {
        private const string ValidTopology = """
                                             {
                                               "$schema": "./.dale/topology.schema.json",
                                               "id": "demo",
                                               "logicBlockInstances": [ { "typeFullName": "Acme.Blocks.Meter", "name": "Meter" } ]
                                             }
                                             """;

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.1")]
        public void OfferTwoTopologySubcommandsThatNameNoPort()
        {
            // Arrange
            var topology = Program.BuildRootCommand().Subcommands.Single(command => command.Name == "topology");

            // Act
            var names = topology.Subcommands.Select(command => command.Name).ToArray();

            // Assert
            CollectionAssert.AreEquivalent(new[] { "validate", "schema" }, names);
            foreach (var name in names)
            {
                var parseResult = Program.BuildRootCommand().Parse(new[] { "topology", name, "--port", "5000" });
                Assert.AreNotEqual(0, parseResult.Errors.Count, $"`topology {name}` addresses no host and must not accept a port");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.2")]
        public async Task ValidateEveryFileInOrdinalNameOrder()
        {
            // Arrange — the JSON document carries the walk's order, which the decorated table lines do not
            // expose to a writer a test can capture.
            var dir = NewDirectory();
            File.WriteAllText(Path.Combine(dir, "beta.topology.json"), ValidTopology.Replace("\"demo\"", "\"beta\""));
            File.WriteAllText(Path.Combine(dir, "alpha.topology.json"), ValidTopology.Replace("\"demo\"", "\"alpha\""));
            var output = new StringWriter();
            var previousOut = Console.Out;
            Console.SetOut(output);
            DaleConsole.JsonMode = true;

            // Act
            int exit;
            try
            {
                exit = await Program.BuildRootCommand().Parse(new[] { "topology", "validate", "--dir", dir }).InvokeAsync();
            }
            finally
            {
                DaleConsole.JsonMode = false;
                Console.SetOut(previousOut);
                Directory.Delete(dir, true);
            }

            // Assert
            Assert.AreEqual(0, exit);
            var files = JsonNode.Parse(output.ToString())!["files"]!.AsArray().Select(file => file!["file"]!.GetValue<string>()).ToArray();
            CollectionAssert.AreEqual(new[] { "alpha.topology.json", "beta.topology.json" }, files);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.2")]
        public async Task ExitNonZeroWhenAnyFileHasError()
        {
            // Arrange — one good file and one whose id contradicts its name.
            var dir = NewDirectory();
            File.WriteAllText(Path.Combine(dir, "demo.topology.json"), ValidTopology);
            File.WriteAllText(Path.Combine(dir, "renamed.topology.json"), ValidTopology);

            // Act
            int exit;
            try
            {
                exit = await Program.BuildRootCommand().Parse(new[] { "topology", "validate", "--dir", dir }).InvokeAsync();
            }
            finally
            {
                Directory.Delete(dir, true);
            }

            // Assert
            Assert.AreEqual(1, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.3")]
        public async Task RefuseTopologiesDirectoryHoldingNoTopologyFile()
        {
            // Arrange
            var dir = NewDirectory();

            // Act
            int exit;
            try
            {
                exit = await Program.BuildRootCommand().Parse(new[] { "topology", "validate", "--dir", dir }).InvokeAsync();
            }
            finally
            {
                Directory.Delete(dir, true);
            }

            // Assert
            Assert.AreEqual(1, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.3")]
        public async Task RefuseTopologiesDirectoryThatDoesNotExist()
        {
            // Arrange
            var dir = Path.Combine(Path.GetTempPath(), $"dale-absent-{Guid.NewGuid():N}");

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "topology", "validate", "--dir", dir }).InvokeAsync();

            // Assert
            Assert.AreEqual(1, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.3")]
        public async Task PassDirectoryHoldingOneValidFile()
        {
            // Arrange — the other half of the anti-vacuous floor: a directory with a file in it must pass,
            // so the refusal above is not simply "validate never succeeds".
            var dir = NewDirectory();
            File.WriteAllText(Path.Combine(dir, "demo.topology.json"), ValidTopology);

            // Act
            int exit;
            try
            {
                exit = await Program.BuildRootCommand().Parse(new[] { "topology", "validate", "--dir", dir }).InvokeAsync();
            }
            finally
            {
                Directory.Delete(dir, true);
            }

            // Assert
            Assert.AreEqual(0, exit);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.9")]
        public async Task ExitNonZeroForMissingSchemaReferenceOnlyWhereRequired()
        {
            // Arrange
            var dir = NewDirectory();
            File.WriteAllText(Path.Combine(dir, "demo.topology.json"), """{ "id": "demo", "logicBlockInstances": [ { "typeFullName": "T", "name": "N" } ] }""");

            // Act
            int warned;
            int refused;
            try
            {
                warned = await Program.BuildRootCommand().Parse(new[] { "topology", "validate", "--dir", dir }).InvokeAsync();
                refused = await Program.BuildRootCommand().Parse(new[] { "topology", "validate", "--dir", dir, "--require-schema-ref" }).InvokeAsync();
            }
            finally
            {
                Directory.Delete(dir, true);
            }

            // Assert
            Assert.AreEqual(0, warned);
            Assert.AreEqual(1, refused);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.4")]
        [TestProperty("spec", "AC-SCEN-015.10")]
        public void EmbedGenericTopologySchemaInCliAssembly()
        {
            // Arrange
            // The .csproj links the single Vion.Dale.DevHost source file as a CLI embedded resource; this
            // pins that it is present and is actually the topology schema, so `schema` can run offline.
            var assembly = typeof(TopologyCommand).Assembly;

            // Act
            using var stream = assembly.GetManifestResourceStream("Vion.Dale.Cli.topology.schema.json");

            // Assert
            Assert.IsNotNull(stream, "the CLI must embed topology.schema.json so `dale topology schema` works with no host");
            using var reader = new StreamReader(stream!);
            var document = JsonNode.Parse(reader.ReadToEnd());
            Assert.IsNotNull(document!["properties"]?["logicBlockInstances"], "the embedded resource must be the topology schema");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.4")]
        public async Task WriteSchemaUnaltered()
        {
            // Arrange — the canonical file, read from the CLI assembly rather than from disk, is what a
            // written copy must equal byte for byte: a re-serialized document reflows the source's one-line
            // arrays and turns a consumer's regeneration into a whole-file diff.
            var outputPath = Path.Combine(Path.GetTempPath(), $"dale-topology-schema-{Guid.NewGuid():N}.json");
            using var stream = typeof(TopologyCommand).Assembly.GetManifestResourceStream("Vion.Dale.Cli.topology.schema.json")!;
            using var reader = new StreamReader(stream);
            var canonical = reader.ReadToEnd();

            // Act
            var exit = await Program.BuildRootCommand().Parse(new[] { "topology", "schema", "--out", outputPath }).InvokeAsync();

            // Assert
            try
            {
                Assert.AreEqual(0, exit);
                Assert.AreEqual(canonical, File.ReadAllText(outputPath));
            }
            finally
            {
                File.Delete(outputPath);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.5")]
        [DataRow("--out")]
        [DataRow("-O")]
        [DataRow("-o")]
        public void TakeSchemaFilePathFromOutOptionAndItsTwoShortForms(string form)
        {
            // Arrange / Act
            var parseResult = Program.BuildRootCommand().Parse(new[] { "topology", "schema", form, "written.json" });

            // Assert
            Assert.AreEqual(0, parseResult.Errors.Count);
            Assert.AreEqual("written.json", parseResult.GetValue<string?>("--out"));
            Assert.AreEqual("table", parseResult.GetValue<string>("--output"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CLI-020.5")]
        public void LeaveOutputMeaningFormatOnTopologySchema()
        {
            // Arrange / Act
            var parseResult = Program.BuildRootCommand().Parse(new[] { "topology", "schema", "--out", "written.json", "--output", "json" });

            // Assert
            Assert.AreEqual(0, parseResult.Errors.Count);
            Assert.AreEqual("json", parseResult.GetValue<string>("--output"));
            Assert.AreEqual("written.json", parseResult.GetValue<string?>("--out"));
        }

        private static string NewDirectory()
        {
            var dir = Path.Combine(Path.GetTempPath(), $"dale-topologies-{Guid.NewGuid():N}");
            Directory.CreateDirectory(dir);
            return dir;
        }
    }
}