using System.Globalization;
using System.Linq;
using System.Text.Json;
using Vion.Dale.DevHost.Scenarios;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The structural contract of a <c>*.scenario.json</c> file — everything
    ///     <see cref="ScenarioFile.Parse" /> decides without a wired host: identity, strictness, the closed
    ///     step vocabulary, the per-kind field rules, durations and comparators. Name-path resolution needs a
    ///     configuration and lives in <c>ScenarioRunnerShould</c> / <c>StructFieldPathShould</c>.
    ///     <para>
    ///         Cross-tier: the same rules are mirrored by <c>dale scenario validate</c> and by
    ///         <c>scenario.schema.json</c>; <c>ScenarioDefinitionSitesShould</c> owns the agreement between the
    ///         four sites, this class owns the runner's own verdict.
    ///     </para>
    /// </summary>
    [TestClass]
    public class ScenarioFileShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.1")]
        public void RejectVersionOtherThanOne()
        {
            // Arrange / Act
            var absent = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""{ "id": "x", "topology": "t" }"""));
            var future = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""{ "version": 2, "id": "x", "topology": "t" }"""));

            // Assert
            Assert.IsTrue(absent.Errors.Any(m => m.Contains("version must be 1 (got 0)")), string.Join("; ", absent.Errors));
            Assert.IsTrue(future.Errors.Any(m => m.Contains("version must be 1 (got 2)")), string.Join("; ", future.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.2")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "t", "checks": [] }""", DisplayName = "unmapped property")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "t", "topology": "u" }""", DisplayName = "repeated key")]
        public void RejectUnmappedPropertyOrRepeatedKey(string json)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse(json));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("not valid scenario JSON")), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.3")]
        [DataRow("a..b", "URL-safe slug", DisplayName = "dot-dot")]
        [DataRow(".a", "URL-safe slug", DisplayName = "leading dot")]
        [DataRow("", "URL-safe slug", DisplayName = "empty")]
        [DataRow("a/b", "URL-safe slug", DisplayName = "separator")]
        [DataRow("schema", "reserved", DisplayName = "reserved, lower case")]
        [DataRow("SCHEMA", "reserved", DisplayName = "reserved, upper case")]
        public void RejectIdThatIsNotSlugOrIsReserved(string id, string expectedReason)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "{{id}}", "topology": "t" }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedReason)), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.4")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "" }""", "topology is required", DisplayName = "topology, empty")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "   " }""", "topology is required", DisplayName = "topology, whitespace")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "t", "watch": [""] }""", "watch[0]: empty name path", DisplayName = "watch entry, empty")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "t", "watch": ["  "] }""", "watch[0]: empty name path", DisplayName = "watch entry, whitespace")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "t", "judge": [{ "text": "  " }] }""", "judge[0]: text is required", DisplayName = "judge text, whitespace")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "t", "steps": [{ "set": "  ", "value": 1 }] }""", "set: empty name path", DisplayName = "set path, whitespace")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "t", "steps": [{ "settle": { "until": ["  "] } }] }""", "settle.until[0]: empty name path",
                 DisplayName = "settle.until entry, whitespace")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "t", "steps": [{ "serviceProviderSet": { "logicBlock": "  ", "contract": "C" }, "value": 1 }] }""",
                 "serviceProviderSet.logicBlock is required", DisplayName = "drive block, whitespace")]
        [DataRow("""{ "version": 1, "id": "x", "topology": "t", "steps": [{ "serviceProviderSet": { "logicBlock": "B", "contract": "  " }, "value": 1 }] }""",
                 "serviceProviderSet.contract is required", DisplayName = "drive contract, whitespace")]
        public void RejectRequiredStringThatIsEmptyOrWhitespace(string json, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse(json));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedError)), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.5")]
        public void ReportEveryStructuralProblemAtOnce()
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                 {
                                                                                                   "version": 3, "id": "a..b", "topology": "",
                                                                                                   "watch": [""],
                                                                                                   "judge": [{ "text": " " }]
                                                                                                 }
                                                                                                 """));

            // Assert
            Assert.IsTrue(refused.Errors.Count >= 4, string.Join("; ", refused.Errors));
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("version must be 1")), string.Join("; ", refused.Errors));
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("URL-safe slug")), string.Join("; ", refused.Errors));
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("topology is required")), string.Join("; ", refused.Errors));
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("watch[0]")), string.Join("; ", refused.Errors));
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("judge[0]")), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.8")]
        public void DistinguishExplicitNullFromAbsentValue()
        {
            // Arrange
            const string withNull = """{ "version": 1, "id": "x", "topology": "t", "steps": [{ "set": "A.B", "value": null }] }""";

            // Act
            var parsed = ScenarioFile.Parse(withNull);
            var absent = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                { "version": 1, "id": "x", "topology": "t", "steps": [{ "set": "A.B" }] }
                                                                                                """));

            // Assert
            Assert.AreEqual(JsonValueKind.Null, parsed.Steps![0].Value.ValueKind);
            Assert.IsTrue(absent.Errors.Any(m => m.Contains("set requires value")), string.Join("; ", absent.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-002.1")]
        [DataRow("""{ "set": "A.B", "value": 1 }""", "set")]
        [DataRow("""{ "serviceProviderSet": { "logicBlock": "B", "contract": "C" }, "value": 1 }""", "serviceProviderSet")]
        [DataRow("""{ "serviceProviderExpect": { "logicBlock": "B", "contract": "C", "equals": 1 } }""", "serviceProviderExpect")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1 } }""", "waitUntil")]
        [DataRow("""{ "expect": { "property": "A.B", "equals": 1 } }""", "expect")]
        [DataRow("""{ "advance": { "seconds": 1 } }""", "advance")]
        [DataRow("""{ "settle": {} }""", "settle")]
        public void ReportEachStepKindByName(string step, string expectedKind)
        {
            // Arrange / Act
            var parsed = ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }""");

            // Assert
            Assert.AreEqual(expectedKind, parsed.Steps![0].Kind);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-002.2")]
        [DataRow("""{ "label": "nothing" }""", DisplayName = "no shape")]
        [DataRow("""{ "set": "A.B", "value": 1, "advance": { "seconds": 1 } }""", DisplayName = "two shapes")]
        public void RejectStepThatIsNotExactlyOneShape(string step)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("a step is exactly one of")), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-002.3")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1 } }""", DisplayName = "waitUntil")]
        [DataRow("""{ "expect": { "property": "A.B", "equals": 1 } }""", DisplayName = "expect")]
        [DataRow("""{ "serviceProviderExpect": { "logicBlock": "B", "contract": "C", "equals": 1 } }""", DisplayName = "serviceProviderExpect")]
        [DataRow("""{ "advance": { "seconds": 1 } }""", DisplayName = "advance")]
        [DataRow("""{ "settle": {} }""", DisplayName = "settle")]
        public void RejectNonStagingShapeInSetup(string entry)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "setup": [{{entry}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("setup entries stage state")), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-002.3")]
        [DataRow("""{ "set": "A.B", "value": 1 }""", DisplayName = "set")]
        [DataRow("""{ "serviceProviderSet": { "logicBlock": "B", "contract": "C" }, "value": 1 }""", DisplayName = "serviceProviderSet")]
        public void AcceptStagingShapeInSetup(string entry)
        {
            // Arrange / Act
            var parsed = ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "setup": [{{entry}}] }""");

            // Assert
            Assert.AreEqual(1, parsed.Setup!.Count);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-002.4")]
        public void CarryLabelAndSpecOnEveryStepKind()
        {
            // Arrange
            const string json = """
                                {
                                  "version": 1, "id": "x", "topology": "t",
                                  "steps": [
                                    { "label": "l1", "spec": "s1", "set": "A.B", "value": 1 },
                                    { "label": "l2", "spec": "s2", "settle": {} }
                                  ]
                                }
                                """;

            // Act
            var parsed = ScenarioFile.Parse(json);

            // Assert
            CollectionAssert.AreEqual(new[] { "l1", "l2" }, parsed.Steps!.Select(s => s.Label).ToList());
            CollectionAssert.AreEqual(new[] { "s1", "s2" }, parsed.Steps!.Select(s => s.Spec).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-002.5")]
        [DataRow("""{ "set": "A.B" }""", "set requires value", DisplayName = "set")]
        [DataRow("""{ "serviceProviderSet": { "logicBlock": "B", "contract": "C" } }""", "serviceProviderSet requires value", DisplayName = "serviceProviderSet")]
        public void RequireValueOnStagingShape(string step, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedError)), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-002.6")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1 }, "value": 3 }""", "value is not valid on a waitUntil step", DisplayName = "value on waitUntil")]
        [DataRow("""{ "advance": { "seconds": 1 }, "value": 3 }""", "value is not valid on an advance step", DisplayName = "value on advance")]
        [DataRow("""{ "settle": {}, "value": 3 }""", "value is not valid on a settle step", DisplayName = "value on settle")]
        [DataRow("""{ "expect": { "property": "A.B", "equals": 1 }, "value": 3 }""", "value is not valid on an expect step", DisplayName = "value on expect")]
        [DataRow("""{ "set": "A.B", "value": 1, "timeoutSeconds": 5 }""", "timeoutSeconds is only valid on a waitUntil step", DisplayName = "timeoutSeconds on set")]
        [DataRow("""{ "settle": {}, "timeoutSeconds": 5 }""", "timeoutSeconds is only valid on a waitUntil step", DisplayName = "timeoutSeconds on settle")]
        public void RejectFieldTheStepKindDoesNotCarry(string step, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedError)), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-003.1")]
        [DataRow("""{ "advance": { "seconds": 0 } }""", "advance.seconds must be positive", DisplayName = "advance, zero")]
        [DataRow("""{ "advance": { "seconds": -1 } }""", "advance.seconds must be positive", DisplayName = "advance, negative")]
        [DataRow("""{ "advance": {} }""", "advance.seconds must be positive", DisplayName = "advance, absent")]
        [DataRow("""{ "settle": { "maxSeconds": 0 } }""", "settle.maxSeconds must be positive", DisplayName = "maxSeconds, zero")]
        [DataRow("""{ "settle": { "maxSeconds": -1 } }""", "settle.maxSeconds must be positive", DisplayName = "maxSeconds, negative")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1 }, "timeoutSeconds": 0 }""", "timeoutSeconds must be positive", DisplayName = "timeout, zero")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1 }, "timeoutSeconds": -1 }""", "timeoutSeconds must be positive", DisplayName = "timeout, negative")]
        public void RejectNonPositiveDuration(string step, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedError)), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-003.2")]
        [DataRow("""{ "advance": { "seconds": 1e400 } }""", "advance.seconds", DisplayName = "advance, overflows to infinity")]
        [DataRow("""{ "advance": { "seconds": 1e308 } }""", "advance.seconds", DisplayName = "advance, finite but past the cap")]
        [DataRow("""{ "settle": { "maxSeconds": 1e400 } }""", "settle.maxSeconds", DisplayName = "maxSeconds, overflows to infinity")]
        [DataRow("""{ "settle": { "maxSeconds": 1e308 } }""", "settle.maxSeconds", DisplayName = "maxSeconds, finite but past the cap")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1 }, "timeoutSeconds": 1e400 }""", "timeoutSeconds", DisplayName = "timeout, overflows to infinity")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1 }, "timeoutSeconds": 1e308 }""", "timeoutSeconds", DisplayName = "timeout, finite but past the cap")]
        public void RejectDurationLongerThanTheRunnerCanSpend(string step, string expectedField)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedField) && m.Contains("longer than a run can spend")), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-003.2")]
        [DataRow(1e10, DisplayName = "well inside the cap")]
        [DataRow(922337203685d, DisplayName = "exactly at the cap")]
        public void AcceptDurationTheRunnerCanSpend(double seconds)
        {
            // Arrange
            var json = $$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{ "advance": { "seconds": {{seconds.ToString("R", CultureInfo.InvariantCulture)}} } }] }""";

            // Act
            var parsed = ScenarioFile.Parse(json);

            // Assert
            Assert.AreEqual(seconds, parsed.Steps![0].Advance!.Seconds);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-003.3")]
        [DataRow("""{ "advance": { "seconds": "5" } }""", DisplayName = "advance, quoted")]
        [DataRow("""{ "settle": { "maxSeconds": "5" } }""", DisplayName = "maxSeconds, quoted")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1 }, "timeoutSeconds": "5" }""", DisplayName = "timeout, quoted")]
        public void RejectDurationThatIsNotJsonNumber(string step)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("not valid scenario JSON")), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-003.5")]
        public void RejectEmptySettleTargetListButAcceptOmittedOne()
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                 { "version": 1, "id": "x", "topology": "t", "steps": [{ "settle": { "until": [] } }] }
                                                                                                 """));
            var omitted = ScenarioFile.Parse("""{ "version": 1, "id": "x", "topology": "t", "steps": [{ "settle": { "maxSeconds": 5 } }] }""");

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("settle.until must be a non-empty array")), string.Join("; ", refused.Errors));
            Assert.IsNull(omitted.Steps![0].Settle!.Until);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-003.6")]
        public void AcceptSettleWithNoMembers()
        {
            // Arrange / Act
            var parsed = ScenarioFile.Parse("""{ "version": 1, "id": "x", "topology": "t", "steps": [{ "settle": {} }] }""");

            // Assert
            Assert.IsNull(parsed.Steps![0].Settle!.MaxSeconds);
            Assert.IsNull(parsed.Steps![0].Settle!.Until);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.1")]
        [DataRow("""{ "waitUntil": { "property": "A.B" } }""", "waitUntil takes exactly one of", DisplayName = "waitUntil, none")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1, "below": 2 } }""", "waitUntil takes exactly one of", DisplayName = "waitUntil, two")]
        [DataRow("""{ "expect": { "property": "A.B" } }""", "expect takes exactly one of", DisplayName = "expect, none")]
        [DataRow("""{ "serviceProviderExpect": { "logicBlock": "B", "contract": "C" } }""", "serviceProviderExpect takes exactly one of", DisplayName = "output assert, none")]
        public void RequireExactlyOneComparator(string step, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedError)), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.2")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": "x" } }""", "waitUntil.above must be a number", DisplayName = "waitUntil above, string")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "below": true } }""", "waitUntil.below must be a number", DisplayName = "waitUntil below, boolean")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": { "path": "C.D" } } }""", "waitUntil.above must be a number", DisplayName = "waitUntil above, path form")]
        [DataRow("""{ "serviceProviderExpect": { "logicBlock": "B", "contract": "C", "above": "x" } }""", "serviceProviderExpect.above must be a number",
                 DisplayName = "output assert above, string")]
        public void RejectNonNumericRelationalComparand(string step, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedError)), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.3")]
        public void RejectStructOrArrayComparandButAcceptNull()
        {
            // Arrange / Act
            var structComparand = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                         {
                                                                                                           "version": 1, "id": "x", "topology": "t",
                                                                                                           "steps": [{ "waitUntil": { "property": "A.B", "equals": { "x": 1 } } }]
                                                                                                         }
                                                                                                         """));
            var arrayComparand = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                        {
                                                                                                          "version": 1, "id": "x", "topology": "t",
                                                                                                          "steps": [{ "waitUntil": { "property": "A.B", "notEquals": [1, 2] } }]
                                                                                                        }
                                                                                                        """));
            var nullComparand = ScenarioFile.Parse("""{ "version": 1, "id": "x", "topology": "t", "steps": [{ "waitUntil": { "property": "A.B", "equals": null } }] }""");

            // Assert
            Assert.IsTrue(structComparand.Errors.Any(m => m.Contains("does not compare structs/arrays in v1")), string.Join("; ", structComparand.Errors));
            Assert.IsTrue(arrayComparand.Errors.Any(m => m.Contains("does not compare structs/arrays in v1")), string.Join("; ", arrayComparand.Errors));
            Assert.AreEqual(JsonValueKind.Null, nullComparand.Steps![0].WaitUntil!.EqualTo.ValueKind);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.4")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "oneOf": [] } }""", "must be a non-empty array", DisplayName = "empty")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "oneOf": 5 } }""", "must be an array of scalars", DisplayName = "not an array")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "oneOf": [{ "a": 1 }] } }""", "elements must be scalars", DisplayName = "object element")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "oneOf": [[1]] } }""", "elements must be scalars", DisplayName = "array element")]
        public void RejectOneOfThatIsNotNonEmptyScalarArray(string step, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedError)), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.5")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "equals": 1, "tolerance": -0.5 } }""", "tolerance must be non-negative", DisplayName = "negative")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "equals": "Foo", "tolerance": 1 } }""", "tolerance is only valid with a numeric equals",
                 DisplayName = "with a string equals")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "equals": null, "tolerance": 1 } }""", "tolerance is only valid with a numeric equals",
                 DisplayName = "with a null equals")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "oneOf": [1], "tolerance": 1 } }""", "tolerance is only valid with a numeric equals", DisplayName = "with oneOf")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "above": 1, "tolerance": 1 } }""", "tolerance is only valid with a numeric equals", DisplayName = "with above")]
        public void RejectToleranceThatModifiesNothingNumeric(string step, string expectedError)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }"""));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains(expectedError)), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.5")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "equals": 1, "tolerance": 0 } }""", DisplayName = "zero, exact equality spelled out")]
        [DataRow("""{ "waitUntil": { "property": "A.B", "equals": 1, "tolerance": 0.5 } }""", DisplayName = "positive")]
        [DataRow("""{ "expect": { "property": "A.B", "equals": { "path": "C.D" }, "tolerance": 1 } }""", DisplayName = "with a relational equals")]
        public void AcceptToleranceOnNumericEquals(string step)
        {
            // Arrange / Act
            var parsed = ScenarioFile.Parse($$"""{ "version": 1, "id": "x", "topology": "t", "steps": [{{step}}] }""");

            // Assert
            Assert.IsNotNull(parsed.Steps![0].WaitUntil?.Tolerance ?? parsed.Steps![0].Expect?.Tolerance);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-004.6")]
        public void AcceptRelationalComparandOnlyOnExpectAndOnlyWholeObject()
        {
            // Arrange / Act
            var onExpect = ScenarioFile.Parse("""
                                              { "version": 1, "id": "x", "topology": "t", "steps": [{ "expect": { "property": "A.B", "equals": { "path": "C.D" } } }] }
                                              """);
            var onOutputAssert = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                        {
                                                                                                          "version": 1, "id": "x", "topology": "t",
                                                                                                          "steps": [{ "serviceProviderExpect": { "logicBlock": "B", "contract": "C", "equals": { "path": "X.Y" } } }]
                                                                                                        }
                                                                                                        """));
            var withExtraKey = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse("""
                                                                                                      {
                                                                                                        "version": 1, "id": "x", "topology": "t",
                                                                                                        "steps": [{ "expect": { "property": "A.B", "equals": { "path": "C.D", "extra": 1 } } }]
                                                                                                      }
                                                                                                      """));

            // Assert
            Assert.AreEqual("C.D", onExpect.Steps![0].Expect!.EqualTo.GetProperty("path").GetString());
            Assert.IsTrue(onOutputAssert.Errors.Any(m => m.Contains("does not compare structs/arrays in v1")), string.Join("; ", onOutputAssert.Errors));
            Assert.IsTrue(withExtraKey.Errors.Any(m => m.Contains("the only object form is")), string.Join("; ", withExtraKey.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-007.7")]
        [DataRow("a..b", DisplayName = "empty inner segment")]
        [DataRow("a.", DisplayName = "trailing dot")]
        [DataRow(".b", DisplayName = "leading dot")]
        [DataRow("  ", DisplayName = "whitespace")]
        public void RejectOutputAssertFieldThatIsNotFieldPath(string field)
        {
            // Arrange / Act
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(() => ScenarioFile.Parse($$"""
                                                                                                  {
                                                                                                    "version": 1, "id": "x", "topology": "t",
                                                                                                    "steps": [{ "serviceProviderExpect": { "logicBlock": "B", "contract": "C", "field": "{{field}}", "equals": 1 } }]
                                                                                                  }
                                                                                                  """));

            // Assert
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("field is not a field path")), string.Join("; ", refused.Errors));
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-001.9")]
        public void RevalidateHandConstructedFileBeforeRunning()
        {
            // Arrange — reachable only from C#: no JSON document can carry a NaN duration.
            var handBuilt = new ScenarioFile
                            {
                                Version = ScenarioFile.SupportedVersion,
                                Id = "hand-built",
                                Topology = "t",
                                Steps = new[] { new ScenarioStep { Advance = new ScenarioAdvance { Seconds = double.NaN } } },
                            };

            // Act / Assert
            var refused = Assert.ThrowsExactly<ScenarioFormatException>(handBuilt.EnsureStructurallyValid);
            Assert.IsTrue(refused.Errors.Any(m => m.Contains("advance.seconds")), string.Join("; ", refused.Errors));
        }
    }
}
