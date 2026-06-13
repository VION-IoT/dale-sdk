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

        [TestMethod]
        public void Scaffold_EmitsACompileShapedTest_WithApplyAsyncAndAJudgmentTodoPerItem()
        {
            var scenario = JsonNode.Parse("""
                                          {
                                            "version": 1, "id": "peak-shaving", "title": "Peak shaving", "topology": "em-closed-loop",
                                            "specs": [ "AC-EM-23" ],
                                            "setup": [ { "set": "RefControllableConsumer.OperatingMode", "value": "PeakShaving" } ],
                                            "steps": [
                                              { "label": "Raise demand", "set": "RefControllableConsumer.RequestedCurrentA", "value": 16.0 },
                                              { "waitUntil": { "property": "RefControllableBuffer.AllocatedActivePowerKw", "below": -1.0 }, "timeoutSeconds": 20 }
                                            ],
                                            "watch": [ "RefControllableBuffer.AllocatedActivePowerKw" ],
                                            "judge": [ { "text": "Buffer discharges", "spec": "AC-EM-23.2" }, { "text": "No oscillation" } ]
                                          }
                                          """)!;

            var code = ScenarioCommand.EmitScaffold(scenario, "MyTests", "scenarios");

            StringAssert.Contains(code, "namespace MyTests");
            StringAssert.Contains(code, "public class PeakShavingScenario");
            StringAssert.Contains(code, "[Fact]");
            StringAssert.Contains(code, "public async Task PeakShaving()");
            StringAssert.Contains(code, "ScenarioRunner.ApplyAsync(ScenarioId, host.Control)");
            StringAssert.Contains(code, "private const string ScenarioId = \"peak-shaving\";");
            StringAssert.Contains(code, "// Topology: em-closed-loop");
            StringAssert.Contains(code, "// TODO [AC-EM-23.2]: Buffer discharges");
            StringAssert.Contains(code, "// TODO: No oscillation");
            StringAssert.Contains(code, "Build the 'em-closed-loop' DevHost");
        }

        [TestMethod]
        public async Task Scaffold_LocatesTheFileByIdAndWritesACSharpTest()
        {
            var root = Path.Combine(Path.GetTempPath(), $"df09-{Guid.NewGuid():N}");
            var scenariosDir = Path.Combine(root, "scenarios");
            Directory.CreateDirectory(scenariosDir);
            File.WriteAllText(Path.Combine(scenariosDir, "smoke.scenario.json"), """{ "version": 1, "id": "smoke", "topology": "t", "watch": [ "A.B" ] }""");
            var outputPath = Path.Combine(root, "SmokeScenarioTest.cs");
            try
            {
                var exit = await Program.BuildRootCommand().Parse(new[] { "scenario", "scaffold", "smoke", "--dir", scenariosDir, "-o", outputPath }).InvokeAsync();

                Assert.AreEqual(0, exit);
                Assert.IsTrue(File.Exists(outputPath));
                var code = File.ReadAllText(outputPath);
                StringAssert.Contains(code, "public class SmokeScenario");
                StringAssert.Contains(code, "ScenarioRunner.ApplyAsync(ScenarioId, host.Control)");
            }
            finally
            {
                Directory.Delete(root, true);
            }
        }
    }
}