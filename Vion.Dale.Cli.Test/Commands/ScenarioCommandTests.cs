using System;
using System.IO;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands;

namespace Vion.Dale.Cli.Test.Commands
{
    /// <summary>
    ///     The `dale scenario` verbs as wired into the CLI. These cover the offline-schema guarantee (DF-10):
    ///     the generic schema is embedded in the CLI assembly, so `dale scenario schema` no longer needs a
    ///     running DevHost — only enrichment (the actual name paths) wants a config source.
    /// </summary>
    [TestClass]
    public class ScenarioCommandTests
    {
        [TestMethod]
        public void GenericScenarioSchema_IsEmbeddedInTheCliAssembly_AndIsTheScenarioSchema()
        {
            // The .csproj links the single Vion.Dale.DevHost source file as a CLI embedded resource; this
            // pins that it is present and is actually the scenario schema (so `schema` can run offline).
            var assembly = typeof(ScenarioCommand).Assembly;
            using var stream = assembly.GetManifestResourceStream("Vion.Dale.Cli.scenario.schema.json");
            Assert.IsNotNull(stream, "The CLI must embed scenario.schema.json so `dale scenario schema` works offline (DF-10).");

            using var reader = new StreamReader(stream!);
            var document = JsonNode.Parse(reader.ReadToEnd());
            Assert.IsNotNull(document!["$defs"]?["namePath"], "The embedded resource must be the scenario schema (has the $defs.namePath the enricher fills).");
        }

        [TestMethod]
        public async Task Schema_FromConfigExport_RunsOfflineAndEnrichesNamePaths()
        {
            var configPath = Path.Combine(Path.GetTempPath(), $"df10-config-{Guid.NewGuid():N}.json");
            var outputPath = Path.Combine(Path.GetTempPath(), $"df10-schema-{Guid.NewGuid():N}.json");
            File.WriteAllText(configPath,
                              """
                              {
                                "topologyName": "demo",
                                "logicBlocks": [
                                  {
                                    "name": "EnergyManager",
                                    "services": [
                                      {
                                        "identifier": "EnergyManager",
                                        "serviceProperties": [ { "identifier": "ActivePowerImportingKw", "schema": { "type": "number" } } ],
                                        "serviceMeasuringPoints": []
                                      }
                                    ]
                                  }
                                ]
                              }
                              """);
            try
            {
                // --port 1: nothing is listening there, so a passing run proves the generic schema came from
                // the embedded resource, not a host — the DF-10 offline guarantee.
                var exit = await Program.BuildRootCommand().Parse(new[] { "scenario", "schema", "--config", configPath, "--port", "1", "-o", outputPath }).InvokeAsync();

                Assert.AreEqual(0, exit);
                var schema = File.ReadAllText(outputPath);
                StringAssert.Contains(schema, "EnergyManager.ActivePowerImportingKw", "The schema must be enriched offline with the topology's name paths.");
                StringAssert.Contains(schema, "namePath");
            }
            finally
            {
                File.Delete(configPath);
                if (File.Exists(outputPath))
                {
                    File.Delete(outputPath);
                }
            }
        }
    }
}