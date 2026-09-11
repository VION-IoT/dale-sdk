using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands;

namespace Vion.Dale.Cli.Test.Commands
{
    /// <summary>
    ///     The `dale topology validate` core — the lite, language-neutral mirror of the rules
    ///     <c>DevTopologyFile</c> applies when the host loads a topology, plus the two checks the loader
    ///     does not make (a wire declared twice, a missing <c>$schema</c> reference).
    /// </summary>
    [TestClass]
    public class TopologyFileChecksTests
    {
        // A valid two-instance topology with one interface mapping and one pairing — the shape the
        // mutations below break one rule of at a time.
        private const string Valid = """
                                     {
                                       "$schema": "./.dale/topology.schema.json",
                                       "id": "demo",
                                       "logicBlockInstances": [
                                         { "typeFullName": "Acme.Blocks.Meter", "name": "Meter" },
                                         { "typeFullName": "Acme.Blocks.Sim", "name": "Sim", "instantiationParameters": { "Phases": 3 } }
                                       ],
                                       "interfaceMappings": [
                                         { "sourceLogicBlockName": "Meter", "sourceInterfaceIdentifier": "Out",
                                           "targetLogicBlockName": "Sim", "targetInterfaceIdentifier": "In" }
                                       ],
                                       "contractPairings": [
                                         { "a": { "logicBlockName": "Meter", "contractIdentifier": "Grid" },
                                           "b": { "logicBlockName": "Sim", "contractIdentifier": "Grid" } }
                                       ],
                                       "contractMappings": [
                                         { "logicBlockName": "Meter", "contractIdentifier": "Grid", "mappedServiceIdentifier": "GridService" }
                                       ]
                                     }
                                     """;

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        public void PassTopologyMeetingEveryStructuralRule()
        {
            // Arrange / Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", Valid);

            // Assert
            CollectionAssert.AreEqual(new string[0], outcome.Errors.ToArray());
            CollectionAssert.AreEqual(new string[0], outcome.Warnings.ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        [DataRow("demo.topology.json", "{ nope", "not valid topology JSON", DisplayName = "text that does not parse")]
        [DataRow("demo.topology.json", "[]", "not a JSON object", DisplayName = "a document that is not an object")]
        [DataRow("demo.topology.json", """{ "logicBlockInstances": [] }""", "id is required", DisplayName = "no id")]
        [DataRow("demo.topology.json", """{ "id": 7, "logicBlockInstances": [] }""", "id is required", DisplayName = "an id of the wrong JSON kind")]
        [DataRow("demo.topology.json", """{ "id": "a..b", "logicBlockInstances": [] }""", "id is required", DisplayName = "an id carrying '..'")]
        [DataRow("schema.topology.json", """{ "id": "schema", "logicBlockInstances": [] }""", "is reserved", DisplayName = "the reserved id")]
        [DataRow("renamed.topology.json", """{ "id": "demo", "logicBlockInstances": [] }""", "does not match the file name", DisplayName = "an id the file name contradicts")]
        [DataRow("demo.topology.json", """{ "id": "demo", "logicBlockInstances": [] }""", "at least one instance", DisplayName = "no instance")]
        public void ReportStructuralBreak(string fileName, string json, string expectedFragment)
        {
            // Arrange / Act
            var outcome = TopologyFileChecks.Validate(fileName, json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains(expectedFragment)),
                          $"expected an error carrying '{expectedFragment}', got: {string.Join(" | ", outcome.Errors)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        [DataRow("""{ "name": "Meter" }""", "typeFullName is required", DisplayName = "no type name")]
        [DataRow("""{ "typeFullName": "Acme.Blocks.Meter" }""", "name is required", DisplayName = "no instance name")]
        [DataRow("""{ "typeFullName": "Acme.Blocks.Meter", "name": "Has.Dot" }""", "must not contain '.'", DisplayName = "a dotted instance name")]
        [DataRow("""{ "typeFullName": "Acme.Blocks.Meter", "name": "Meter" }""", "duplicate instance name 'Meter'", DisplayName = "a name already declared")]
        [DataRow("""{ "typeFullName": "Acme.Blocks.Meter", "name": "Second", "instantiationParameters": { "Phases": { "nested": 1 } } }""",
                 "must be a JSON scalar",
                 DisplayName = "an instantiation parameter that is not a scalar")]
        public void ReportInstanceBreak(string instance, string expectedFragment)
        {
            // Arrange
            var json = $$"""
                         {
                           "id": "demo",
                           "logicBlockInstances": [ { "typeFullName": "Acme.Blocks.Meter", "name": "Meter" }, {{instance}} ]
                         }
                         """;

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains(expectedFragment)),
                          $"expected an error carrying '{expectedFragment}', got: {string.Join(" | ", outcome.Errors)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        [DataRow("""{ "sourceLogicBlockName": "Meter", "sourceInterfaceIdentifier": "Out", "targetLogicBlockName": "Sim" }""",
                 "are all required",
                 DisplayName = "a mapping missing one of its four fields")]
        [DataRow("""{ "sourceLogicBlockName": "Ghost", "sourceInterfaceIdentifier": "Out", "targetLogicBlockName": "Sim", "targetInterfaceIdentifier": "In" }""",
                 "'Ghost' is not a declared instance",
                 DisplayName = "a mapping naming an undeclared source")]
        [DataRow("""{ "sourceLogicBlockName": "Meter", "sourceInterfaceIdentifier": "Out", "targetLogicBlockName": "Ghost", "targetInterfaceIdentifier": "In" }""",
                 "'Ghost' is not a declared instance",
                 DisplayName = "a mapping naming an undeclared target")]
        public void ReportInterfaceMappingBreak(string mapping, string expectedFragment)
        {
            // Arrange
            var json = $$"""
                         {
                           "id": "demo",
                           "logicBlockInstances": [ { "typeFullName": "Acme.Blocks.Meter", "name": "Meter" }, { "typeFullName": "Acme.Blocks.Sim", "name": "Sim" } ],
                           "interfaceMappings": [ {{mapping}} ]
                         }
                         """;

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains(expectedFragment)),
                          $"expected an error carrying '{expectedFragment}', got: {string.Join(" | ", outcome.Errors)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        [DataRow("""{ "a": { "logicBlockName": "Meter" }, "b": { "logicBlockName": "Sim", "contractIdentifier": "Grid" } }""",
                 "logicBlockName and contractIdentifier are both required",
                 DisplayName = "an endpoint missing its contract")]
        [DataRow("""{ "a": { "logicBlockName": "Ghost", "contractIdentifier": "Grid" }, "b": { "logicBlockName": "Sim", "contractIdentifier": "Grid" } }""",
                 "'Ghost' is not a declared instance",
                 DisplayName = "an endpoint naming an undeclared block")]
        [DataRow("""{ "a": { "logicBlockName": "Meter", "contractIdentifier": "Grid" }, "b": { "logicBlockName": "Meter", "contractIdentifier": "Grid" } }""",
                 "a pairing joins two distinct endpoints",
                 DisplayName = "an endpoint paired with itself")]
        public void ReportContractPairingBreak(string pairing, string expectedFragment)
        {
            // Arrange
            var json = PairedTopology(pairing);

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains(expectedFragment)),
                          $"expected an error carrying '{expectedFragment}', got: {string.Join(" | ", outcome.Errors)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        [DataRow("""{ "contractIdentifier": "Grid" }""", "logicBlockName and contractIdentifier are both required", DisplayName = "a contract mapping naming no block")]
        [DataRow("""{ "logicBlockName": "Meter" }""", "logicBlockName and contractIdentifier are both required", DisplayName = "a contract mapping naming no contract")]
        [DataRow("""{ "logicBlockName": "Ghost", "contractIdentifier": "Grid" }""",
                 "'Ghost' is not a declared instance",
                 DisplayName = "a contract mapping naming an undeclared block")]
        public void ReportContractMappingBreak(string mapping, string expectedFragment)
        {
            // Arrange
            var json = $$"""
                         {
                           "id": "demo",
                           "logicBlockInstances": [ { "typeFullName": "Acme.Blocks.Meter", "name": "Meter" } ],
                           "contractMappings": [ {{mapping}} ]
                         }
                         """;

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains(expectedFragment)),
                          $"expected an error carrying '{expectedFragment}', got: {string.Join(" | ", outcome.Errors)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        [DataRow("interfaceMappings", DisplayName = "the interface mappings")]
        [DataRow("contractPairings", DisplayName = "the contract pairings")]
        [DataRow("contractMappings", DisplayName = "the contract mappings")]
        public void ReportOptionalCollectionOfWrongJsonKind(string member)
        {
            // Arrange — the brackets around a single entry dropped, which the host's deserializer throws on.
            // Skipping it would pass the file and silently drop every check over that collection.
            var json = $$"""
                         {
                           "id": "demo",
                           "logicBlockInstances": [ { "typeFullName": "Acme.Blocks.Meter", "name": "Meter" } ],
                           "{{member}}": { "logicBlockName": "Meter", "contractIdentifier": "Grid" }
                         }
                         """;

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains($"{member} must be an array")), string.Join(" | ", outcome.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        public void ReportInstancesOfWrongJsonKind()
        {
            // Arrange
            var json = """{ "id": "demo", "logicBlockInstances": { "typeFullName": "Acme.Blocks.Meter", "name": "Meter" } }""";

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains("logicBlockInstances must be an array")), string.Join(" | ", outcome.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        public void ReportSchemaReferenceOfWrongJsonKind()
        {
            // Arrange
            var json = """{ "$schema": 42, "id": "demo", "logicBlockInstances": [ { "typeFullName": "T", "name": "N" } ] }""";

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains("$schema must be a string")), string.Join(" | ", outcome.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.6")]
        public void ReportEveryErrorRatherThanFirst()
        {
            // Arrange — three independent breaks: a wrong id, a duplicate instance name, a dangling mapping.
            var json = """
                       {
                         "id": "wrong",
                         "logicBlockInstances": [
                           { "typeFullName": "Acme.Blocks.Meter", "name": "Meter" },
                           { "typeFullName": "Acme.Blocks.Sim", "name": "Meter" }
                         ],
                         "interfaceMappings": [
                           { "sourceLogicBlockName": "Ghost", "sourceInterfaceIdentifier": "Out",
                             "targetLogicBlockName": "Meter", "targetInterfaceIdentifier": "In" }
                         ]
                       }
                       """;

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.AreEqual(3, outcome.Errors.Count, string.Join(" | ", outcome.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.7")]
        [DataRow("""{ "id": "demo", "logicBlockInstance": [], "logicBlockInstances": [ { "typeFullName": "T", "name": "N" } ] }""",
                 "'logicBlockInstance' is not a topology field",
                 DisplayName = "a misspelled root member")]
        [DataRow("""{ "id": "demo", "logicBlockInstances": [ { "typeFullName": "T", "name": "N", "typeName": "T" } ] }""",
                 "'typeName' is not a topology field",
                 DisplayName = "a misspelled instance member")]
        [DataRow("""{ "id": "demo", "logicBlockInstances": [ { "typeFullName": "T", "name": "N" } ], "contractPairings": [ { "a": { "logicBlockName": "N", "contractIdentifier": "C", "extra": 1 }, "b": { "logicBlockName": "N", "contractIdentifier": "D" } } ] }""",
                 "'extra' is not a topology field",
                 DisplayName = "a misspelled pairing-endpoint member")]
        public void ReportUndeclaredMember(string json, string expectedFragment)
        {
            // Arrange / Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains(expectedFragment)),
                          $"expected an error carrying '{expectedFragment}', got: {string.Join(" | ", outcome.Errors)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.7")]
        public void ReportMemberDeclaredTwice()
        {
            // Arrange — JsonNode keeps the last of a repeated member silently, so the second id would win
            // and the file would pass here and fail at load.
            var json = """{ "id": "demo", "id": "other", "logicBlockInstances": [ { "typeFullName": "T", "name": "N" } ] }""";

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains("not valid topology JSON")), string.Join(" | ", outcome.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.8")]
        [DataRow("""{ "a": { "logicBlockName": "Meter", "contractIdentifier": "Grid" }, "b": { "logicBlockName": "Sim", "contractIdentifier": "Grid" } }""",
                 DisplayName = "the same order twice")]
        [DataRow("""{ "a": { "logicBlockName": "Sim", "contractIdentifier": "Grid" }, "b": { "logicBlockName": "Meter", "contractIdentifier": "Grid" } }""",
                 DisplayName = "the endpoints swapped")]
        public void ReportWireDeclaredTwice(string repeat)
        {
            // Arrange
            var json = PairedTopology("""{ "a": { "logicBlockName": "Meter", "contractIdentifier": "Grid" }, "b": { "logicBlockName": "Sim", "contractIdentifier": "Grid" } }""",
                                      repeat);

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains("are already paired")), string.Join(" | ", outcome.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.8")]
        public void PassTwoDistinctWiresOnSameBlockPair()
        {
            // Arrange — the same two blocks joined on two different contract bindings is two wires, not a repeat.
            var json = PairedTopology("""{ "a": { "logicBlockName": "Meter", "contractIdentifier": "Grid" }, "b": { "logicBlockName": "Sim", "contractIdentifier": "Grid" } }""",
                                      """{ "a": { "logicBlockName": "Meter", "contractIdentifier": "Pv" }, "b": { "logicBlockName": "Sim", "contractIdentifier": "Pv" } }""");

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            CollectionAssert.AreEqual(new string[0], outcome.Errors.ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.9")]
        public void WarnWhenSchemaReferenceAbsent()
        {
            // Arrange
            var json = """{ "id": "demo", "logicBlockInstances": [ { "typeFullName": "T", "name": "N" } ] }""";

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json);

            // Assert
            CollectionAssert.AreEqual(new string[0], outcome.Errors.ToArray());
            Assert.IsTrue(outcome.Warnings.Any(warning => warning.Contains("no '$schema' reference")), string.Join(" | ", outcome.Warnings));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.9")]
        public void RefuseMissingSchemaReferenceWhenRequired()
        {
            // Arrange
            var json = """{ "id": "demo", "logicBlockInstances": [ { "typeFullName": "T", "name": "N" } ] }""";

            // Act
            var outcome = TopologyFileChecks.Validate("demo.topology.json", json, true);

            // Assert
            Assert.IsTrue(outcome.Errors.Any(error => error.Contains("no '$schema' reference")), string.Join(" | ", outcome.Errors));
            CollectionAssert.AreEqual(new string[0], outcome.Warnings.ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-015.9")]
        public void PassWhenSchemaReferenceRequiredAndPresent()
        {
            // Arrange / Act — the reference is present, so the flag changes nothing.
            var outcome = TopologyFileChecks.Validate("demo.topology.json", Valid, true);

            // Assert
            CollectionAssert.AreEqual(new string[0], outcome.Errors.ToArray());
        }

        private static string PairedTopology(params string[] pairings)
        {
            return $$"""
                     {
                       "id": "demo",
                       "logicBlockInstances": [ { "typeFullName": "Acme.Blocks.Meter", "name": "Meter" }, { "typeFullName": "Acme.Blocks.Sim", "name": "Sim" } ],
                       "contractPairings": [ {{string.Join(", ", pairings)}} ]
                     }
                     """;
        }
    }
}