using System.IO;
using Vion.Dale.DevHost.Topologies;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The structural contract of a <c>*.topology.json</c> file — everything
    ///     <see cref="DevTopologyFile.Parse" /> decides without a block catalog: identity, strictness,
    ///     instance declarations, and the endpoint references of interface mappings and contract pairings.
    ///     What needs the catalog (type resolution, interface compatibility, contract bindings, pairing wire
    ///     types) is <c>DevTopologyLoaderShould</c>'s and <c>ContractPairingShould</c>'s.
    /// </summary>
    [TestClass]
    public class DevTopologyFileShould
    {
        private const string OneInstance = """{ "typeFullName": "X.Y", "name": "A" }""";

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.2")]
        [DataRow("""{ "id": "t", "logicBlockInstances": [{ "typeFullName": "X.Y", "name": "A" }], "bogus": 1 }""", DisplayName = "unmapped property")]
        [DataRow("""{ "id": "t", "id": "u", "logicBlockInstances": [{ "typeFullName": "X.Y", "name": "A" }] }""", DisplayName = "repeated key")]
        public void RejectUnmappedPropertyOrRepeatedKey(string json)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse(json));

            // Assert
            StringAssert.Contains(refused.Message, "not valid topology JSON");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.3")]
        [DataRow("a..b", "URL-safe slug", DisplayName = "dot-dot")]
        [DataRow(".t", "URL-safe slug", DisplayName = "leading dot")]
        [DataRow("", "URL-safe slug", DisplayName = "empty")]
        [DataRow("t/u", "URL-safe slug", DisplayName = "separator")]
        [DataRow("schema", "reserved", DisplayName = "reserved, lower case")]
        [DataRow("SCHEMA", "reserved", DisplayName = "reserved, upper case")]
        public void RejectNonSlugOrReservedId(string id, string expectedReason)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse($$"""{ "id": "{{id}}", "logicBlockInstances": [{{OneInstance}}] }"""));

            // Assert
            StringAssert.Contains(refused.Message, expectedReason);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.4")]
        [DataRow("""{ "typeFullName": "X.Y", "name": "" }""", "name is required", DisplayName = "instance name, empty")]
        [DataRow("""{ "typeFullName": "X.Y", "name": "   " }""", "name is required", DisplayName = "instance name, whitespace")]
        [DataRow("""{ "typeFullName": "", "name": "A" }""", "typeFullName is required", DisplayName = "typeFullName, empty")]
        [DataRow("""{ "typeFullName": "  ", "name": "A" }""", "typeFullName is required", DisplayName = "typeFullName, whitespace")]
        public void RejectEmptyOrWhitespaceInstanceField(string instance, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse($$"""{ "id": "t", "logicBlockInstances": [{{instance}}] }"""));

            // Assert
            StringAssert.Contains(refused.Message, expectedError);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.4")]
        [DataRow("""{ "sourceLogicBlockName": "  ", "sourceInterfaceIdentifier": "I", "targetLogicBlockName": "A", "targetInterfaceIdentifier": "J" }""",
                 DisplayName = "source block, whitespace")]
        [DataRow("""{ "sourceLogicBlockName": "A", "sourceInterfaceIdentifier": "  ", "targetLogicBlockName": "A", "targetInterfaceIdentifier": "J" }""",
                 DisplayName = "source interface, whitespace")]
        [DataRow("""{ "sourceLogicBlockName": "A", "sourceInterfaceIdentifier": "I", "targetLogicBlockName": "  ", "targetInterfaceIdentifier": "J" }""",
                 DisplayName = "target block, whitespace")]
        [DataRow("""{ "sourceLogicBlockName": "A", "sourceInterfaceIdentifier": "I", "targetLogicBlockName": "A", "targetInterfaceIdentifier": "  " }""",
                 DisplayName = "target interface, whitespace")]
        public void RejectWhitespaceInterfaceMappingField(string mapping)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse($$"""
                                                                                                   {
                                                                                                     "id": "t", "logicBlockInstances": [{{OneInstance}}],
                                                                                                     "interfaceMappings": [{{mapping}}]
                                                                                                   }
                                                                                                   """));

            // Assert
            StringAssert.Contains(refused.Message, "are all required");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.4")]
        [DataRow("""{ "logicBlockName": "  ", "contractIdentifier": "C" }""", DisplayName = "endpoint block, whitespace")]
        [DataRow("""{ "logicBlockName": "A", "contractIdentifier": "  " }""", DisplayName = "endpoint contract, whitespace")]
        public void RejectWhitespacePairingEndpointField(string endpoint)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse($$"""
                                                                                                   {
                                                                                                     "id": "t", "logicBlockInstances": [{{OneInstance}}],
                                                                                                     "contractPairings": [{ "a": {{endpoint}}, "b": { "logicBlockName": "A", "contractIdentifier": "D" } }]
                                                                                                   }
                                                                                                   """));

            // Assert
            StringAssert.Contains(refused.Message, "logicBlockName and contractIdentifier are both required");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.1")]
        [DataRow("""{ "id": "t", "logicBlockInstances": [] }""", "must declare at least one instance", DisplayName = "empty instance list")]
        [DataRow("""{ "id": "t" }""", "must declare at least one instance", DisplayName = "absent instance list")]
        [DataRow("""{ "id": "t", "logicBlockInstances": [{ "typeFullName": "X.Y", "name": "A" }, { "typeFullName": "X.Z", "name": "A" }] }""",
                 "duplicate instance name 'A'",
                 DisplayName = "duplicate name")]
        [DataRow("""{ "id": "t", "logicBlockInstances": [{ "typeFullName": "X.Y", "name": "A.B" }] }""", "must not contain '.'", DisplayName = "name with a dot")]
        public void RejectInstanceListNoNamePathCanAddress(string json, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse(json));

            // Assert
            StringAssert.Contains(refused.Message, expectedError);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-014.2")]
        [DataRow("""{ "a": { "logicBlockName": "Z", "contractIdentifier": "C" }, "b": { "logicBlockName": "A", "contractIdentifier": "D" } }""",
                 "'Z' is not a declared instance",
                 DisplayName = "undeclared instance")]
        [DataRow("""{ "a": { "logicBlockName": "A", "contractIdentifier": "C" }, "b": { "logicBlockName": "A", "contractIdentifier": "C" } }""",
                 "a pairing joins two distinct endpoints",
                 DisplayName = "coinciding endpoints")]
        public void RejectPairingNamingNoWire(string pairing, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse($$"""
                                                                                                   {
                                                                                                     "id": "t", "logicBlockInstances": [{{OneInstance}}],
                                                                                                     "contractPairings": [{{pairing}}]
                                                                                                   }
                                                                                                   """));

            // Assert
            StringAssert.Contains(refused.Message, expectedError);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.2")]
        public void RejectInterfaceMappingNamingUndeclaredInstance()
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse($$"""
                                                                                                   {
                                                                                                     "id": "t", "logicBlockInstances": [{{OneInstance}}],
                                                                                                     "interfaceMappings": [
                                                                                                       { "sourceLogicBlockName": "Z", "sourceInterfaceIdentifier": "I",
                                                                                                         "targetLogicBlockName": "A", "targetInterfaceIdentifier": "J" }
                                                                                                     ]
                                                                                                   }
                                                                                                   """));

            // Assert
            StringAssert.Contains(refused.Message, "'Z' is not a declared instance");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.5")]
        public void ReportEveryStructuralProblemAtOnce()
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<InvalidDataException>(() => DevTopologyFile.Parse("""
                                                                                                 {
                                                                                                   "id": "a..b",
                                                                                                   "logicBlockInstances": [
                                                                                                     { "typeFullName": "", "name": "A.B" },
                                                                                                     { "typeFullName": "X.Y", "name": "   " }
                                                                                                   ]
                                                                                                 }
                                                                                                 """));

            // Assert
            StringAssert.Contains(refused.Message, "URL-safe slug");
            StringAssert.Contains(refused.Message, "typeFullName is required");
            StringAssert.Contains(refused.Message, "name is required");
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.7")]
        public void OmitEmptyOptionalCollectionOnSerialization()
        {
            // Arrange — an editor that added and then removed a pairing hands back an empty array, and an
            // instance with no gated members hands back an empty parameter map; neither may reach the file.
            var emptied = DevTopologyFile.Parse("""
                                                {
                                                  "id": "t",
                                                  "logicBlockInstances": [{ "typeFullName": "X.Y", "name": "A", "instantiationParameters": {} }],
                                                  "contractPairings": []
                                                }
                                                """);

            // Act
            var json = emptied.ToJson();

            // Assert
            Assert.IsFalse(json.Contains("contractPairings"), json);
            Assert.IsFalse(json.Contains("instantiationParameters"), json);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-013.7")]
        public void KeepPopulatedOptionalCollectionOnSerialization()
        {
            // Arrange
            var populated = DevTopologyFile.Parse("""
                                                  {
                                                    "id": "t",
                                                    "logicBlockInstances": [
                                                      { "typeFullName": "X.Y", "name": "A", "instantiationParameters": { "P": 1 } },
                                                      { "typeFullName": "X.Z", "name": "B" }
                                                    ],
                                                    "contractPairings": [
                                                      { "a": { "logicBlockName": "A", "contractIdentifier": "C" }, "b": { "logicBlockName": "B", "contractIdentifier": "D" } }
                                                    ]
                                                  }
                                                  """);

            // Act
            var json = populated.ToJson();

            // Assert
            StringAssert.Contains(json, "contractPairings");
            StringAssert.Contains(json, "instantiationParameters");
        }
    }
}