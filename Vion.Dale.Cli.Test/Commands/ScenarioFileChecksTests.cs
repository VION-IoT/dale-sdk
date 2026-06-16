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
                                                          { "digitalInput": { "block": "Counter", "contract": "EnableInput" }, "value": true },
                                                          { "waitUntil": { "property": "Counter.CounterDoubled", "above": 1 }, "timeoutSeconds": 5 },
                                                          { "wait": { "seconds": 0.5 } }
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
                                                          { "digitalInput": { "block": "Counter", "contract": "Nope" }, "value": true }
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
                                                        "setup": [ { "wait": { "seconds": 1 } } ],
                                                        "steps": [
                                                          { "set": "Counter.Counter", "value": 1, "wait": { "seconds": 1 } },
                                                          { "waitUntil": { "property": "Counter.Counter", "above": 1, "below": 2 } },
                                                          { "digitalInput": { "block": "Counter", "contract": "EnableInput" }, "value": 7 },
                                                          { "wait": { "seconds": 0 } }
                                                        ] }
                                                      """,
                                                      Config);
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("setup entries stage state")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("exactly one of set")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("exactly one of above")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("boolean value")), string.Join("; ", outcome.Errors));
            Assert.IsTrue(outcome.Errors.Any(e => e.Contains("wait.seconds")), string.Join("; ", outcome.Errors));
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