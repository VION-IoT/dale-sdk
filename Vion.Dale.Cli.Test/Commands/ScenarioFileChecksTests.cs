using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.Cli.Commands;

namespace Vion.Dale.Cli.Test.Commands
{
    /// <summary>
    ///     The `dale scenario validate` core — the lite, language-neutral mirror of the RFC 0006 format
    ///     rules and revision 5 name-path resolution, evaluated against a configuration export.
    /// </summary>
    [TestClass]
    public class ScenarioFileChecksTests
    {
        private static readonly JsonNode Config = JsonNode.Parse("""
                                                                 {
                                                                   "topologyName": "demo",
                                                                   "logicBlocks": [
                                                                     {
                                                                       "name": "Counter",
                                                                       "services": [
                                                                         {
                                                                           "identifier": "CounterService",
                                                                           "serviceProperties": [
                                                                             { "identifier": "Counter", "schema": { "type": "integer" } },
                                                                             { "identifier": "Sealed", "schema": { "type": "integer", "readOnly": true } }
                                                                           ],
                                                                           "serviceMeasuringPoints": [ { "identifier": "CounterDoubled", "schema": { "type": "integer" } } ]
                                                                         }
                                                                       ],
                                                                       "contracts": [ { "identifier": "EnableInput", "matchingContractType": "DigitalInput" } ],
                                                                       "contractMappings": [ { "contractIdentifier": "EnableInput" } ]
                                                                     },
                                                                     {
                                                                       "name": "DualPoint",
                                                                       "services": [
                                                                         { "identifier": "PointA", "serviceProperties": [ { "identifier": "Limit", "schema": { "type": "number" } } ], "serviceMeasuringPoints": [] },
                                                                         { "identifier": "PointB", "serviceProperties": [ { "identifier": "Limit", "schema": { "type": "number" } } ], "serviceMeasuringPoints": [] }
                                                                       ],
                                                                       "contracts": [],
                                                                       "contractMappings": []
                                                                     }
                                                                   ]
                                                                 }
                                                                 """)!;

        // A block with a struct-typed property (AllocatedCurrent {l1,l2,l3}) on a single service, plus a scalar
        // property — the DF-26 shape. The enricher emits struct field-paths for it; validate must resolve them.
        private static readonly JsonNode StructConfig = JsonNode.Parse("""
                                                                       {
                                                                         "topologyName": "energy",
                                                                         "logicBlocks": [
                                                                           {
                                                                             "name": "RefControllableConsumer",
                                                                             "services": [
                                                                               {
                                                                                 "identifier": "ConsumerService",
                                                                                 "serviceProperties": [
                                                                                   { "identifier": "AllocatedCurrent", "schema": { "type": "object", "properties": { "l1": { "type": "number" }, "l2": { "type": "number" }, "l3": { "type": "number" } } } },
                                                                                   { "identifier": "OperatingMode", "schema": { "type": "string" } }
                                                                                 ],
                                                                                 "serviceMeasuringPoints": []
                                                                               }
                                                                             ]
                                                                           }
                                                                         ]
                                                                       }
                                                                       """)!;

        [TestMethod]
        public void AcceptsAValidScenario()
        {
            var outcome = ScenarioFileChecks.Validate("ok.scenario.json",
                                                      """
                                                      {
                                                        "version": 1, "id": "ok", "topology": "demo",
                                                        "setup": [ { "set": "Counter.Counter", "value": 1 } ],
                                                        "steps": [
                                                          { "set": "DualPoint.PointA.Limit", "value": 2.5 },
                                                          { "serviceProviderSet": { "logicBlock": "Counter", "contract": "EnableInput" }, "value": true },
                                                          { "waitUntil": { "property": "Counter.CounterDoubled", "above": 1 }, "timeoutSeconds": 5 },
                                                          { "advance": { "seconds": 0.5 } }
                                                        ],
                                                        "watch": [ "Counter.Counter" ],
                                                        "judge": [ { "text": "looks right" } ]
                                                      }
                                                      """,
                                                      Config);
            Assert.AreEqual(0, outcome.Errors.Count, string.Join("; ", outcome.Errors));
            Assert.IsNull(outcome.SkippedForTopology);
        }

        [TestMethod]
        public void AcceptsServiceProviderSetAndExpect()
        {
            var outcome = ScenarioFileChecks.Validate("sp.scenario.json",
                                                      """
                                                      {
                                                        "version": 1, "id": "sp", "topology": "demo",
                                                        "steps": [
                                                          { "serviceProviderSet": { "logicBlock": "Counter", "contract": "EnableInput" }, "value": true },
                                                          { "serviceProviderExpect": { "logicBlock": "Counter", "contract": "EnableInput", "equals": true } }
                                                        ]
                                                      }
                                                      """,
                                                      Config);
            Assert.AreEqual(0, outcome.Errors.Count, string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void RejectsServiceProviderSetWithoutValue_AndAnUnknownContract()
        {
            var noValue = ScenarioFileChecks.Validate("a.scenario.json",
                                                      """{ "version": 1, "id": "a", "topology": "demo", "steps": [ { "serviceProviderSet": { "logicBlock": "Counter", "contract": "EnableInput" } } ] }""",
                                                      Config);
            Assert.IsTrue(noValue.Errors.Any(e => e.Contains("serviceProviderSet requires value")), string.Join("; ", noValue.Errors));

            var unknown = ScenarioFileChecks.Validate("b.scenario.json",
                                                      """{ "version": 1, "id": "b", "topology": "demo", "steps": [ { "serviceProviderSet": { "logicBlock": "Counter", "contract": "Nope" }, "value": true } ] }""",
                                                      Config);
            Assert.IsTrue(unknown.Errors.Any(e => e.Contains("has no contract 'Nope'")), string.Join("; ", unknown.Errors));
        }

        [TestMethod]
        public void RejectsAmbiguousTwoSegmentPaths_ListingQualifiedCandidates()
        {
            var outcome = ScenarioFileChecks.Validate("amb.scenario.json",
                                                      """{ "version": 1, "id": "amb", "topology": "demo", "steps": [ { "set": "DualPoint.Limit", "value": 1 } ] }""",
                                                      Config);
            var error = outcome.Errors.Single();
            StringAssert.Contains(error, "ambiguous");
            StringAssert.Contains(error, "DualPoint.PointA.Limit");
            StringAssert.Contains(error, "DualPoint.PointB.Limit");
        }

        [TestMethod]
        public void RejectsWritesToMeasuringPointsAndReadOnlyProperties()
        {
            var outcome = ScenarioFileChecks.Validate("ro.scenario.json",
                                                      """
                                                      { "version": 1, "id": "ro", "topology": "demo",
                                                        "steps": [
                                                          { "set": "Counter.CounterDoubled", "value": 1 },
                                                          { "set": "Counter.Sealed", "value": 1 }
                                                        ] }
                                                      """,
                                                      Config);
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("measuring point")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("read-only property")), string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void RejectsUnknownBlocksMembersAndContracts()
        {
            var outcome = ScenarioFileChecks.Validate("bad.scenario.json",
                                                      """
                                                      { "version": 1, "id": "bad", "topology": "demo",
                                                        "steps": [
                                                          { "set": "Nope.Counter", "value": 1 },
                                                          { "set": "Counter.Nope", "value": 1 },
                                                          { "serviceProviderSet": { "logicBlock": "Counter", "contract": "Nope" }, "value": true }
                                                        ] }
                                                      """,
                                                      Config);
            Assert.AreEqual(3, outcome.Errors.Count, string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void SkipsPathResolutionForOtherTopologies_ButKeepsStructuralChecks()
        {
            var clean = ScenarioFileChecks.Validate("other.scenario.json",
                                                    """{ "version": 1, "id": "other", "topology": "elsewhere", "steps": [ { "set": "Nope.X", "value": 1 } ] }""",
                                                    Config);
            Assert.AreEqual(0, clean.Errors.Count, string.Join("; ", clean.Errors));
            Assert.AreEqual("elsewhere", clean.SkippedForTopology);

            var broken = ScenarioFileChecks.Validate("other.scenario.json",
                                                     """{ "version": 1, "id": "other", "topology": "elsewhere", "steps": [ { "set": "Nope.X" } ] }""",
                                                     Config);
            Assert.IsTrue(broken.Errors.Any(e => e.Contains("set requires value")), string.Join("; ", broken.Errors));
        }

        [TestMethod]
        public void RejectsMalformedSteps()
        {
            var outcome = ScenarioFileChecks.Validate("steps.scenario.json",
                                                      """
                                                      { "version": 1, "id": "steps", "topology": "demo",
                                                        "setup": [ { "advance": { "seconds": 1 } } ],
                                                        "steps": [
                                                          { "set": "Counter.Counter", "value": 1, "advance": { "seconds": 1 } },
                                                          { "waitUntil": { "property": "Counter.Counter", "above": 1, "below": 2 } },
                                                          { "serviceProviderSet": { "logicBlock": "Counter", "contract": "EnableInput" } },
                                                          { "advance": { "seconds": 0 } }
                                                        ] }
                                                      """,
                                                      Config);
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("setup entries stage state")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("exactly one of set")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("exactly one of above")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("serviceProviderSet requires value")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("advance.seconds")), string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void SettleUntil_ResolvesTargetPaths_AndRejectsEmptyOrUnknown()
        {
            // A valid settle.until resolves its target paths against the topology, like watch.
            var ok = ScenarioFileChecks.Validate("settle-ok.scenario.json",
                                                 """
                                                 { "version": 1, "id": "settle-ok", "topology": "demo",
                                                   "watch": [ "Counter.Counter" ],
                                                   "steps": [ { "settle": { "until": [ "Counter.Counter" ], "maxSeconds": 5 } } ] }
                                                 """,
                                                 Config);
            Assert.AreEqual(0, ok.Errors.Count, string.Join("; ", ok.Errors));

            // An empty until list and an unresolvable target path are both reported.
            var bad = ScenarioFileChecks.Validate("settle-bad.scenario.json",
                                                  """
                                                  { "version": 1, "id": "settle-bad", "topology": "demo",
                                                    "steps": [
                                                      { "settle": { "until": [] } },
                                                      { "settle": { "until": [ "Counter.Nope" ] } }
                                                    ] }
                                                  """,
                                                  Config);
            Assert.IsTrue(bad.Errors.Any(e => e.Contains("settle.until must be a non-empty array")), string.Join("; ", bad.Errors));
            Assert.IsTrue(bad.Errors.Any(e => e.Contains("settle.until[0]")), string.Join("; ", bad.Errors));

            // Structural until checks are config-independent (mirroring the model): a blank entry is rejected
            // even for a non-matching topology where path resolution is skipped — the two validators agree.
            var blankSkipped = ScenarioFileChecks.Validate("settle-blank.scenario.json",
                                                           """{ "version": 1, "id": "settle-blank", "topology": "elsewhere", "steps": [ { "settle": { "until": ["  "] } } ] }""",
                                                           Config);
            Assert.IsTrue(blankSkipped.Errors.Any(e => e.Contains("settle.until[0]") && e.Contains("empty name path")), string.Join("; ", blankSkipped.Errors));
        }

        [TestMethod]
        public void AcceptsExpectOneOfAndPathComparand()
        {
            var outcome = ScenarioFileChecks.Validate("exp.scenario.json",
                                                      """
                                                      {
                                                        "version": 1, "id": "exp", "topology": "demo",
                                                        "steps": [
                                                          { "expect": { "property": "Counter.Counter", "above": 1 } },
                                                          { "expect": { "property": "Counter.Counter", "equals": 5, "tolerance": 1 } },
                                                          { "expect": { "property": "Counter.Counter", "oneOf": [1, 2, 3] } },
                                                          { "expect": { "property": "DualPoint.PointA.Limit", "above": { "path": "DualPoint.PointB.Limit" } } },
                                                          { "waitUntil": { "property": "Counter.Counter", "oneOf": [4, 5] }, "timeoutSeconds": 2 }
                                                        ]
                                                      }
                                                      """,
                                                      Config);
            Assert.AreEqual(0, outcome.Errors.Count, string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void RejectsExpectStructuralProblems()
        {
            var outcome = ScenarioFileChecks.Validate("bad-exp.scenario.json",
                                                      """
                                                      {
                                                        "version": 1, "id": "bad-exp", "topology": "demo",
                                                        "setup": [ { "expect": { "property": "Counter.Counter", "equals": 1 } } ],
                                                        "steps": [
                                                          { "expect": { "property": "Counter.Counter", "above": 1, "below": 2 } },
                                                          { "expect": { "property": "Counter.Counter", "oneOf": [] } },
                                                          { "expect": { "property": "Counter.Counter", "oneOf": [1, { "x": 1 }] } },
                                                          { "expect": { "property": "Counter.Counter", "equals": { "l1": 1, "l2": 2 } } }
                                                        ]
                                                      }
                                                      """,
                                                      Config);
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("setup entries stage state")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("exactly one of above")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("oneOf must be a non-empty array")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("oneOf elements must be scalars")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("does not compare structs/arrays")), string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void RejectsServiceProviderExpectStructuralProblems()
        {
            // serviceProviderExpect (RFC 0010) is step-only and takes exactly one comparator (topology
            // "elsewhere" skips path/contract resolution, isolating the structural checks).
            var outcome = ScenarioFileChecks.Validate("bad-out.scenario.json",
                                                      """
                                                      {
                                                        "version": 1, "id": "bad-out", "topology": "elsewhere",
                                                        "setup": [ { "serviceProviderExpect": { "logicBlock": "Io", "contract": "ActiveOutput", "equals": true } } ],
                                                        "steps": [ { "serviceProviderExpect": { "logicBlock": "Io", "contract": "EchoOutput", "above": 1, "below": 2 } } ]
                                                      }
                                                      """,
                                                      Config);
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("setup entries")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("exactly one of above")), string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void RejectsToleranceWithoutANumericEquals()
        {
            // DF-22: the runtime loader rejects `tolerance` unless paired with a numeric `equals`; the lite
            // validator (and the generated schema) must agree, so `dale scenario validate` fails as early as
            // the loader rather than green-lighting a form the run then rejects.
            var outcome = ScenarioFileChecks.Validate("tol.scenario.json",
                                                      """
                                                      {
                                                        "version": 1, "id": "tol", "topology": "demo",
                                                        "steps": [
                                                          { "expect": { "property": "Counter.Counter", "above": 1, "tolerance": 0.3 } },
                                                          { "waitUntil": { "property": "Counter.Counter", "oneOf": [1, 2], "tolerance": 0.5 }, "timeoutSeconds": 2 }
                                                        ]
                                                      }
                                                      """,
                                                      Config);
            Assert.AreEqual(2, outcome.Errors.Count(e => e.Contains("tolerance is only valid with a numeric equals")), string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void AcceptsToleranceWithANumericEquals()
        {
            // The valid pairing (numeric equals + tolerance), incl. the expect-only relational {path} equals
            // whose resolved value is checked numeric at run time — neither must be flagged.
            var outcome = ScenarioFileChecks.Validate("tol-ok.scenario.json",
                                                      """
                                                      {
                                                        "version": 1, "id": "tol-ok", "topology": "demo",
                                                        "steps": [
                                                          { "expect": { "property": "Counter.Counter", "equals": 5, "tolerance": 0.5 } },
                                                          { "expect": { "property": "DualPoint.PointA.Limit", "equals": { "path": "DualPoint.PointB.Limit" }, "tolerance": 0.1 } }
                                                        ]
                                                      }
                                                      """,
                                                      Config);
            Assert.AreEqual(0, outcome.Errors.Count, string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void ResolvesStructFieldPaths_TheEnricherEmitsAndTheRunnerResolves()
        {
            // DF-26: validate must resolve the struct-field name paths its own enricher emits and the runner
            // accepts — both the Block.Member.Field (3-seg) and Block.Service.Member.Field (4-seg) forms.
            var outcome = ScenarioFileChecks.Validate("sf.scenario.json",
                                                      """
                                                      {
                                                        "version": 1, "id": "sf", "topology": "energy",
                                                        "watch": [
                                                          "RefControllableConsumer.AllocatedCurrent.L1",
                                                          "RefControllableConsumer.ConsumerService.AllocatedCurrent.L3"
                                                        ],
                                                        "steps": [
                                                          { "expect": { "property": "RefControllableConsumer.AllocatedCurrent.L2", "above": 0 } }
                                                        ]
                                                      }
                                                      """,
                                                      StructConfig);
            Assert.AreEqual(0, outcome.Errors.Count, string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void RejectsUnknownStructFieldAndDescentIntoAScalar()
        {
            var outcome = ScenarioFileChecks.Validate("sf-bad.scenario.json",
                                                      """
                                                      {
                                                        "version": 1, "id": "sf-bad", "topology": "energy",
                                                        "watch": [
                                                          "RefControllableConsumer.AllocatedCurrent.L9",
                                                          "RefControllableConsumer.OperatingMode.Nope"
                                                        ]
                                                      }
                                                      """,
                                                      StructConfig);
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("has no field 'L9'")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("is not a struct")), string.Join("; ", outcome.Errors));
        }

        [TestMethod]
        public void RejectsIdProblems()
        {
            var mismatch = ScenarioFileChecks.Validate("a.scenario.json", """{ "version": 1, "id": "b", "topology": "demo" }""", Config);
            Assert.IsTrue(mismatch.Errors.Any(e => e.Contains("does not match the file name")), string.Join("; ", mismatch.Errors));

            var reserved = ScenarioFileChecks.Validate("schema.scenario.json", """{ "version": 1, "id": "schema", "topology": "demo" }""", Config);
            Assert.IsTrue(reserved.Errors.Any(e => e.Contains("reserved")), string.Join("; ", reserved.Errors));
        }

        [TestMethod]
        public void EnrichesTheSchemaWithThisTopologysNamePaths()
        {
            var schema = JsonNode.Parse("""{ "$defs": { "namePath": { "type": "string", "pattern": "x" } } }""")!;
            ScenarioFileChecks.EnrichSchemaWithNamePaths(schema, Config);

            var namePath = schema["$defs"]!["namePath"]!.AsObject();
            Assert.IsFalse(namePath.ContainsKey("pattern"));
            var paths = namePath["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

            // Unique members get both forms; the duplicated 'Limit' only the qualified ones.
            CollectionAssert.Contains(paths, "Counter.Counter");
            CollectionAssert.Contains(paths, "Counter.CounterService.Counter");
            CollectionAssert.Contains(paths, "DualPoint.PointA.Limit");
            CollectionAssert.Contains(paths, "DualPoint.PointB.Limit");
            CollectionAssert.DoesNotContain(paths, "DualPoint.Limit");
        }

        [TestMethod]
        public void EnrichesTheSchemaWithStructFieldPaths_ForStructTypedMembers()
        {
            // A block with a struct-typed service property (AllocatedCurrent with scalar fields L1, L2, L3)
            // and a plain scalar property. The enricher must emit the field paths in PascalCase.
            var config = JsonNode.Parse("""
                                        {
                                          "topologyName": "energy",
                                          "logicBlocks": [
                                            {
                                              "name": "RefControllableConsumer",
                                              "services": [
                                                {
                                                  "identifier": "ConsumerService",
                                                  "serviceProperties": [
                                                    {
                                                      "identifier": "AllocatedCurrent",
                                                      "schema": {
                                                        "type": "object",
                                                        "properties": {
                                                          "l1": { "type": "number" },
                                                          "l2": { "type": "number" },
                                                          "l3": { "type": "number" }
                                                        }
                                                      }
                                                    },
                                                    { "identifier": "OperatingMode", "schema": { "type": "string" } }
                                                  ],
                                                  "serviceMeasuringPoints": []
                                                }
                                              ]
                                            }
                                          ]
                                        }
                                        """)!;

            var schema = JsonNode.Parse("""{ "$defs": { "namePath": { "type": "string", "pattern": "x" } } }""")!;
            ScenarioFileChecks.EnrichSchemaWithNamePaths(schema, config);

            var paths = schema["$defs"]!["namePath"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

            // The struct member itself (valid set target).
            CollectionAssert.Contains(paths, "RefControllableConsumer.AllocatedCurrent");
            CollectionAssert.Contains(paths, "RefControllableConsumer.ConsumerService.AllocatedCurrent");

            // Field paths — camelCase keys converted to PascalCase.
            CollectionAssert.Contains(paths, "RefControllableConsumer.AllocatedCurrent.L1");
            CollectionAssert.Contains(paths, "RefControllableConsumer.AllocatedCurrent.L2");
            CollectionAssert.Contains(paths, "RefControllableConsumer.AllocatedCurrent.L3");

            // Service-qualified field paths.
            CollectionAssert.Contains(paths, "RefControllableConsumer.ConsumerService.AllocatedCurrent.L1");
            CollectionAssert.Contains(paths, "RefControllableConsumer.ConsumerService.AllocatedCurrent.L2");
            CollectionAssert.Contains(paths, "RefControllableConsumer.ConsumerService.AllocatedCurrent.L3");

            // The scalar member still gets its path.
            CollectionAssert.Contains(paths, "RefControllableConsumer.OperatingMode");
            CollectionAssert.Contains(paths, "RefControllableConsumer.ConsumerService.OperatingMode");

            // No path that ends on the object (would not be addressable as a scalar target in waitUntil/expect).
            // (The struct member path itself IS included for set / watch, so we only check that intermediate
            // objects without being a top-level member are not emitted as synthetic entries.)
            CollectionAssert.DoesNotContain(paths, "RefControllableConsumer.AllocatedCurrent."); // no trailing dot
        }

        [TestMethod]
        public void EnrichesTheSchemaWithNestedStructFieldPaths()
        {
            // A struct member with a nested struct field — should recurse and emit the scalar leaf only.
            var config = JsonNode.Parse("""
                                        {
                                          "topologyName": "nested",
                                          "logicBlocks": [
                                            {
                                              "name": "Block",
                                              "services": [
                                                {
                                                  "identifier": "Service",
                                                  "serviceProperties": [
                                                    {
                                                      "identifier": "Status",
                                                      "schema": {
                                                        "type": "object",
                                                        "properties": {
                                                          "inner": {
                                                            "type": "object",
                                                            "properties": {
                                                              "value": { "type": "number" }
                                                            }
                                                          },
                                                          "flag": { "type": "boolean" }
                                                        }
                                                      }
                                                    }
                                                  ],
                                                  "serviceMeasuringPoints": []
                                                }
                                              ]
                                            }
                                          ]
                                        }
                                        """)!;

            var schema = JsonNode.Parse("""{ "$defs": { "namePath": { "type": "string", "pattern": "x" } } }""")!;
            ScenarioFileChecks.EnrichSchemaWithNamePaths(schema, config);

            var paths = schema["$defs"]!["namePath"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();

            // Nested scalar leaf via the intermediate struct.
            CollectionAssert.Contains(paths, "Block.Status.Inner.Value");
            CollectionAssert.Contains(paths, "Block.Service.Status.Inner.Value");

            // Scalar field at top level of the struct.
            CollectionAssert.Contains(paths, "Block.Status.Flag");
            CollectionAssert.Contains(paths, "Block.Service.Status.Flag");

            // The intermediate struct field itself is NOT emitted as a separate path (only scalar leaves).
            CollectionAssert.DoesNotContain(paths, "Block.Status.Inner");
            CollectionAssert.DoesNotContain(paths, "Block.Service.Status.Inner");

            // The struct member itself IS still emitted.
            CollectionAssert.Contains(paths, "Block.Status");
            CollectionAssert.Contains(paths, "Block.Service.Status");
        }

        [TestMethod]
        public void OffersTwoSegmentForm_WhenAMemberIsBothPropertyAndMeasuringPointOnOneService()
        {
            // A single-service block can expose the same member as BOTH a serviceProperty and a
            // serviceMeasuringPoint — the real EnergyManager does exactly this for ActivePowerImportingKw.
            // The resolver counts ONE carrier service, so the two-segment path resolves; the schema enricher
            // must offer it too, not red-squiggle a path that validate/run accept (DF-06).
            var config = JsonNode.Parse("""
                                        {
                                          "topologyName": "demo",
                                          "logicBlocks": [
                                            {
                                              "name": "EnergyManager",
                                              "services": [
                                                {
                                                  "identifier": "EnergyManager",
                                                  "serviceProperties": [ { "identifier": "ActivePowerImportingKw", "schema": { "type": "number" } } ],
                                                  "serviceMeasuringPoints": [ { "identifier": "ActivePowerImportingKw", "schema": { "type": "number" } } ]
                                                }
                                              ]
                                            }
                                          ]
                                        }
                                        """)!;

            var schema = JsonNode.Parse("""{ "$defs": { "namePath": { "type": "string", "pattern": "x" } } }""")!;
            ScenarioFileChecks.EnrichSchemaWithNamePaths(schema, config);

            var paths = schema["$defs"]!["namePath"]!["enum"]!.AsArray().Select(n => n!.GetValue<string>()).ToList();
            CollectionAssert.Contains(paths, "EnergyManager.ActivePowerImportingKw");
            CollectionAssert.Contains(paths, "EnergyManager.EnergyManager.ActivePowerImportingKw");

            // The enricher and the resolver must agree: the two-segment form the schema now offers also
            // passes validate (the consistency DF-06 is about).
            var outcome = ScenarioFileChecks.Validate("imp.scenario.json",
                                                      """{ "version": 1, "id": "imp", "topology": "demo", "watch": [ "EnergyManager.ActivePowerImportingKw" ] }""",
                                                      config);
            Assert.AreEqual(0, outcome.Errors.Count, string.Join("; ", outcome.Errors));
        }
    }
}