using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The step vocabulary has four definition sites and nothing used to check that they agree: the C#
    ///     model and runner, the CLI's <c>dale scenario validate</c>, the JSON schema, and the SPA's step
    ///     forms. Missing one produced an asymmetric quiet failure — a schema that autocompleted a step the
    ///     runner rejected, or a validator that green-lit a file the runner refused.
    ///     <para>
    ///         Each site is read the way a reader of that site reads it: the schema and the two non-owned
    ///         sites (the CLI validator, the SPA catalog) as their committed source text, and the runner
    ///         through its own behaviour. So this suite fails when any one site moves alone.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ScenarioDefinitionSitesShould
    {
        private static readonly string[] ExpectedStepKinds = ["set", "serviceProviderSet", "serviceProviderExpect", "waitUntil", "expect", "advance", "settle"];

        private static readonly string[] ExpectedSetupKinds = ["set", "serviceProviderSet"];

        // One step of each shape, so the runner's discriminator can be read for every kind rather than asserted
        // from its enumeration alone.
        private static readonly Dictionary<string, string> StepOfKind = new()
                                                                       {
                                                                           ["set"] = """{ "set": "A.B", "value": 1 }""",
                                                                           ["serviceProviderSet"] = """{ "serviceProviderSet": { "logicBlock": "B", "contract": "C" }, "value": 1 }""",
                                                                           ["serviceProviderExpect"] = """{ "serviceProviderExpect": { "logicBlock": "B", "contract": "C", "equals": 1 } }""",
                                                                           ["waitUntil"] = """{ "waitUntil": { "property": "A.B", "above": 1 } }""",
                                                                           ["expect"] = """{ "expect": { "property": "A.B", "equals": 1 } }""",
                                                                           ["advance"] = """{ "advance": { "seconds": 1 } }""",
                                                                           ["settle"] = """{ "settle": {} }""",
                                                                       };

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-002.1")]
        public void DeclareOneStepVocabularyAtEverySite()
        {
            // Arrange
            var schema = SchemaKinds("step");
            var runnerEnumeration = RunnerEnumeration();
            var runnerDiscriminators = ExpectedStepKinds.Select(kind => ScenarioFile.Parse(Scenario(StepOfKind[kind])).Steps![0].Kind).ToList();
            var validator = SourceArray("Vion.Dale.Cli/Commands/ScenarioFileChecks.cs", "StepKinds");
            var spa = SpaKindKeys();

            // Act / Assert — each site's own reading, against the one expected set.
            CollectionAssert.AreEqual(ExpectedStepKinds, schema, $"JSON schema: [{string.Join(", ", schema)}]");
            CollectionAssert.AreEqual(ExpectedStepKinds, runnerEnumeration, $"runner enumeration: [{string.Join(", ", runnerEnumeration)}]");
            CollectionAssert.AreEqual(ExpectedStepKinds, runnerDiscriminators, $"ScenarioStep.Kind: [{string.Join(", ", runnerDiscriminators)}]");
            CollectionAssert.AreEqual(ExpectedStepKinds, validator, $"CLI validator: [{string.Join(", ", validator)}]");
            CollectionAssert.AreEqual(ExpectedStepKinds, spa, $"SPA catalog: [{string.Join(", ", spa)}]");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-002.3")]
        public void DeclareOneSetupSubsetAtEverySite()
        {
            // Arrange
            var schema = SchemaKinds("setupEntry");
            var accepted = ExpectedStepKinds.Where(kind => SetupAccepts(StepOfKind[kind])).ToList();
            var validator = SourceArray("Vion.Dale.Cli/Commands/ScenarioFileChecks.cs", "SetupStepKinds");
            var spa = SpaSetupKindKeys();

            // Act / Assert
            CollectionAssert.AreEqual(ExpectedSetupKinds, schema, $"JSON schema: [{string.Join(", ", schema)}]");
            CollectionAssert.AreEqual(ExpectedSetupKinds, accepted, $"runner: [{string.Join(", ", accepted)}]");
            CollectionAssert.AreEqual(ExpectedSetupKinds, validator, $"CLI validator: [{string.Join(", ", validator)}]");
            CollectionAssert.AreEqual(ExpectedSetupKinds, spa, $"SPA catalog: [{string.Join(", ", spa)}]");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.3")]
        public void RefuseInScenarioSchemaTheIdsTheRunnerRefuses()
        {
            // Arrange
            var id = ScenarioSchema().GetProperty("properties").GetProperty("id");

            // Act
            var pattern = id.GetProperty("pattern").GetString();
            var reserved = id.GetProperty("not").GetProperty("pattern").GetString();

            // Assert — the pattern rejects '..' and the reserved name is refused case-insensitively, matching
            // ScenarioFile.StructuralErrors. Asserted as the regexes the schema publishes, then exercised.
            Assert.IsFalse(Regex.IsMatch("a..b", pattern!), pattern);
            Assert.IsTrue(Regex.IsMatch("a.b-c_1", pattern!), pattern);
            Assert.IsTrue(Regex.IsMatch("schema", reserved!), reserved);
            Assert.IsTrue(Regex.IsMatch("SCHEMA", reserved!), reserved);
            Assert.IsFalse(Regex.IsMatch("schematic", reserved!), reserved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.3")]
        public void RefuseInTopologySchemaTheIdsTheParserRefuses()
        {
            // Arrange
            var id = TopologySchema().GetProperty("properties").GetProperty("id");

            // Act
            var pattern = id.GetProperty("pattern").GetString();
            var reserved = id.GetProperty("not").GetProperty("pattern").GetString();

            // Assert
            Assert.IsFalse(Regex.IsMatch("a..b", pattern!), pattern);
            Assert.IsTrue(Regex.IsMatch("default", pattern!), pattern);
            Assert.IsTrue(Regex.IsMatch("schema", reserved!), reserved);
            Assert.IsTrue(Regex.IsMatch("SCHEMA", reserved!), reserved);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.4")]
        public void RefuseInScenarioSchemaWhitespaceJudgmentText()
        {
            // Arrange
            var text = ScenarioSchema().GetProperty("properties").GetProperty("judge").GetProperty("items").GetProperty("properties").GetProperty("text");

            // Act
            var pattern = text.GetProperty("pattern").GetString();

            // Assert — minLength alone passed "   ", which the runner and the CLI validator both refuse.
            Assert.IsFalse(Regex.IsMatch("   ", pattern!), pattern);
            Assert.IsTrue(Regex.IsMatch("the light came on", pattern!), pattern);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-003.2")]
        public void BoundEveryDurationInScenarioSchemaAtWhatARunCanSpend()
        {
            // Arrange
            var defs = ScenarioSchema().GetProperty("$defs");

            // Act
            var timeout = defs.GetProperty("waitUntilStep").GetProperty("properties").GetProperty("timeoutSeconds");
            var advance = defs.GetProperty("advanceStep").GetProperty("properties").GetProperty("advance").GetProperty("properties").GetProperty("seconds");
            var settle = defs.GetProperty("settleStep").GetProperty("properties").GetProperty("settle").GetProperty("properties").GetProperty("maxSeconds");

            // Assert — the same bound the runner and the CLI validator name.
            foreach (var budget in new[] { timeout, advance, settle })
            {
                Assert.AreEqual(0d, budget.GetProperty("exclusiveMinimum").GetDouble());
                Assert.AreEqual(ScenarioFile.MaxDurationSeconds, budget.GetProperty("maximum").GetDouble());
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-003.2")]
        public void NameOneDurationBoundInTheValidatorAndTheRunner()
        {
            // Arrange
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "Vion.Dale.Cli", "Commands", "ScenarioFileChecks.cs"));

            // Act
            var declared = Regex.Match(source, @"MaxDurationSeconds\s*=\s*(?<value>[0-9]+)\s*;");

            // Assert — the CLI deliberately does not reference Vion.Dale.DevHost, so the number is restated
            // there; nothing but this comparison keeps the two from drifting.
            Assert.IsTrue(declared.Success, "ScenarioFileChecks declares no MaxDurationSeconds constant");
            Assert.AreEqual(ScenarioFile.MaxDurationSeconds, double.Parse(declared.Groups["value"].Value, System.Globalization.CultureInfo.InvariantCulture));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.5")]
        public void RequireInScenarioSchemaThatToleranceModifiesNumericEquals()
        {
            // Arrange
            var defs = ScenarioSchema().GetProperty("$defs");

            // Act — waitUntil and the output assert take literal comparands; expect additionally takes {path}.
            var waitUntil = defs.GetProperty("waitUntilStep").GetProperty("properties").GetProperty("waitUntil").GetProperty("dependentSchemas").GetProperty("tolerance");
            var outputAssert = defs.GetProperty("serviceProviderOutputAssert").GetProperty("dependentSchemas").GetProperty("tolerance");
            var expect = defs.GetProperty("expectStep").GetProperty("properties").GetProperty("expect").GetProperty("dependentSchemas").GetProperty("tolerance");

            // Assert — dependentRequired only demanded that `equals` be present, so the schema green-lit
            // { equals: "Foo", tolerance: 1 } that both other sites refuse.
            foreach (var literalOnly in new[] { waitUntil, outputAssert })
            {
                CollectionAssert.AreEqual(new[] { "equals" }, literalOnly.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList());
                Assert.AreEqual("number", literalOnly.GetProperty("properties").GetProperty("equals").GetProperty("type").GetString());
            }

            CollectionAssert.AreEqual(new[] { "equals" }, expect.GetProperty("required").EnumerateArray().Select(e => e.GetString()).ToList());
            Assert.AreEqual("#/$defs/numericComparand", expect.GetProperty("properties").GetProperty("equals").GetProperty("$ref").GetString());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.13")]
        public void AcceptInTopologySchemaEveryInstantiationParameterValueTheLoaderDecodes()
        {
            // Arrange
            var instance = TopologySchema().GetProperty("properties").GetProperty("logicBlockInstances").GetProperty("items");

            // Act
            var accepted = instance.GetProperty("properties")
                                   .GetProperty("instantiationParameters")
                                   .GetProperty("additionalProperties")
                                   .GetProperty("type")
                                   .EnumerateArray()
                                   .Select(e => e.GetString())
                                   .ToList();

            // Assert — the loader decodes with the parameter's own declared type, so a fractional value for a
            // double parameter is legal to it; the schema used to red-squiggle exactly that.
            CollectionAssert.Contains(accepted, "number");
            CollectionAssert.Contains(accepted, "integer");
            CollectionAssert.Contains(accepted, "boolean");
            CollectionAssert.Contains(accepted, "string");
            CollectionAssert.Contains(accepted, "null");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.5")]
        public void ShipTheCanonicalScenarioSchemaFromEveryServingProject()
        {
            // Arrange
            var root = RepoRoot();
            var devHost = File.ReadAllText(Path.Combine(root, "Vion.Dale.DevHost", "Vion.Dale.DevHost.csproj"));
            var cli = File.ReadAllText(Path.Combine(root, "Vion.Dale.Cli", "Vion.Dale.Cli.csproj"));

            // Act / Assert — the DevHost embeds both of its own schemas; the CLI links the DevHost's file
            // rather than keeping a copy, which is the one shape that could silently drift.
            StringAssert.Contains(devHost, @"EmbeddedResource Include=""Scenarios\scenario.schema.json""");
            StringAssert.Contains(devHost, @"EmbeddedResource Include=""Topologies\topology.schema.json""");
            StringAssert.Contains(cli, @"EmbeddedResource Include=""..\Vion.Dale.DevHost\Scenarios\scenario.schema.json""");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.3")]
        public void CarryTheCanonicalSchemaInEveryCommittedProjectCopy()
        {
            // Arrange — `dale scenario schema` writes the canonical document with one node replaced (the
            // topology's own name-path enum), so every committed copy must equal canonical everywhere else.
            var root = RepoRoot();
            var canonical = JsonNode(File.ReadAllText(Path.Combine(root, "Vion.Dale.DevHost", "Scenarios", "scenario.schema.json")));
            var copies = Directory.GetFiles(root, "scenario.schema.json", SearchOption.AllDirectories)
                                  .Where(p => p.Contains(".dale") && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}") &&
                                              !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}"))
                                  .ToList();

            // Act / Assert
            Assert.IsTrue(copies.Count > 0, $"no committed .dale scenario schema copies under {root}");
            foreach (var copy in copies)
            {
                var without = WithoutNamePath(JsonNode(File.ReadAllText(copy)));
                Assert.AreEqual(WithoutNamePath(canonical), without, $"{copy} diverges from the canonical schema outside $defs/namePath");
            }
        }

        // The kinds a schema `oneOf` union names, in declaration order: "#/$defs/setStep" -> "set".
        private static List<string> SchemaKinds(string union)
        {
            return ScenarioSchema()
                   .GetProperty("$defs")
                   .GetProperty(union)
                   .GetProperty("oneOf")
                   .EnumerateArray()
                   .Select(e => e.GetProperty("$ref").GetString()!)
                   .Select(reference => reference["#/$defs/".Length..])
                   .Select(name => name.EndsWith("Step", StringComparison.Ordinal) ? name[..^"Step".Length] : name)
                   .ToList();
        }

        // The runner's own enumeration of the closed vocabulary, taken from the message it refuses a
        // no-shape step with.
        private static List<string> RunnerEnumeration()
        {
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse(Scenario("""{ "label": "nothing" }""")));
            var sentence = refused.Errors.Single(m => m.Contains("a step is exactly one of"));
            return sentence[(sentence.IndexOf("one of", StringComparison.Ordinal) + "one of".Length)..].Split('/').Select(part => part.Trim()).ToList();
        }

        private static bool SetupAccepts(string entry)
        {
            try
            {
                ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "setup": [{{entry}}] }""");
                return true;
            }
            catch (ScenarioFormatException)
            {
                return false;
            }
        }

        // The keys of the SPA's KINDS catalog, in declaration order, read from the module as text — the SPA is
        // Tier C and its node tests are not scanned by spec-trace, so the agreement is checked from here.
        private static List<string> SpaKindKeys()
        {
            return SpaKindEntries().Select(entry => entry.Key).ToList();
        }

        private static List<string> SpaSetupKindKeys()
        {
            return SpaKindEntries().Where(entry => entry.SetupOk).Select(entry => entry.Key).ToList();
        }

        private static List<(string Key, bool SetupOk)> SpaKindEntries()
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot(), "Vion.Dale.DevHost.Web", "wwwroot", "scenario-forms.js"));
            var catalog = Regex.Match(source, @"export const KINDS = \{(?<body>.*?)\n\};", RegexOptions.Singleline);
            Assert.IsTrue(catalog.Success, "scenario-forms.js declares no KINDS catalog");

            return Regex.Matches(catalog.Groups["body"].Value, @"^\s*(?<key>\w+):.*?setupOk:\s*(?<setupOk>true|false)", RegexOptions.Multiline)
                        .Select(entry => (entry.Groups["key"].Value, entry.Groups["setupOk"].Value == "true"))
                        .ToList();
        }

        // A `public static readonly string[] Name = ["a", "b"];` literal, read from a project this suite does
        // not reference — the CLI is deliberately standalone (no Vion.Dale.DevHost dependency).
        private static List<string> SourceArray(string relativePath, string name)
        {
            var source = File.ReadAllText(Path.Combine(RepoRoot(), relativePath.Replace('/', Path.DirectorySeparatorChar)));
            var declaration = Regex.Match(source, name + @"\s*=\s*\[(?<items>[^\]]*)\]");
            Assert.IsTrue(declaration.Success, $"{relativePath} declares no {name} array");

            return Regex.Matches(declaration.Groups["items"].Value, "\"(?<item>[^\"]+)\"").Select(item => item.Groups["item"].Value).ToList();
        }

        private static JsonElement ScenarioSchema()
        {
            return JsonNode(File.ReadAllText(Path.Combine(RepoRoot(), "Vion.Dale.DevHost", "Scenarios", "scenario.schema.json")));
        }

        private static JsonElement TopologySchema()
        {
            return JsonNode(File.ReadAllText(Path.Combine(RepoRoot(), "Vion.Dale.DevHost", "Topologies", "topology.schema.json")));
        }

        private static JsonElement JsonNode(string json)
        {
            return JsonDocument.Parse(json).RootElement.Clone();
        }

        // Canonical minus the one node `dale scenario schema` replaces, as a stable string for comparison.
        private static string WithoutNamePath(JsonElement schema)
        {
            var document = System.Text.Json.Nodes.JsonNode.Parse(schema.GetRawText())!;
            document["$defs"]!.AsObject().Remove("namePath");
            return document.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        }

        // The repository root, by the .git marker — the same bound DevDataDirectory.Resolve walks to.
        private static string RepoRoot()
        {
            var current = AppContext.BaseDirectory;
            for (var depth = 0; depth < 8 && current is not null; depth++)
            {
                if (Directory.Exists(Path.Combine(current, ".git")))
                {
                    return current;
                }

                current = Path.GetDirectoryName(current);
            }

            throw new DirectoryNotFoundException($"no .git ancestor within 8 levels of {AppContext.BaseDirectory}");
        }

        private static string Scenario(string step)
        {
            return $$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }""";
        }
    }
}
