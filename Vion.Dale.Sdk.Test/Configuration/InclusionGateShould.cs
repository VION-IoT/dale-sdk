using System;
using System.Linq;
using System.Text.Json.Nodes;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Contracts.Predicates;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Configuration
{
    /// <summary>
    ///     Whether a gated member belongs to a configured instance, and what a gate the system cannot resolve
    ///     does (<c>docs/specs/config-gating.md</c>). Evaluation is strict and fail-closed: a block whose shape
    ///     is undecidable must not bind an arbitrary half of itself.
    /// </summary>
    [TestClass]
    public sealed class InclusionGateShould
    {
        private static readonly string[] StationServices = [nameof(GatedCountBlock), nameof(GatedCountBlock.Point1), "Point2", "Point3"];

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.1")]
        public void IncludeUngatedMember()
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), JsonValue.Create(1L)));

            // Assert
            CollectionAssert.Contains(harness.BoundServices().ToArray(), nameof(GatedCountBlock.Point1));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.2")]
        [TestProperty("spec", "AC-GATE-006.3")]
        [DataRow(1, new[] { nameof(GatedCountBlock), "Point1" }, DisplayName = "one point")]
        [DataRow(2, new[] { nameof(GatedCountBlock), "Point1", "Point2" }, DisplayName = "two points")]
        [DataRow(3, new[] { nameof(GatedCountBlock), "Point1", "Point2", "Point3" }, DisplayName = "three points")]
        public void BindExactlyTheIncludedMembers(int pointCount, string[] expectedServices)
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), JsonValue.Create((long)pointCount)));

            // Assert
            CollectionAssert.AreEquivalent(expectedServices, harness.BoundServices().ToArray());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.4")]
        [DataRow(StationModel.Ristretto, true, DisplayName = "a member of the list")]
        [DataRow(StationModel.Cappuccino, true, DisplayName = "another member of the list")]
        [DataRow(StationModel.Bricco, false, DisplayName = "outside the list")]
        public void ResolveEnumParameterByMemberName(StationModel model, bool expectedIncluded)
        {
            // A context built with an int cast or a raw ToString mis-resolves here while the shared
            // conformance vector still passes, because the predicate compares against 'Ristretto'.

            // Arrange
            var block = new GatedEnumBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block,
                              [nameof(GatedEnumBlock), nameof(GatedEnumBlock.Point1), "Point2"],
                              Parameter(nameof(GatedEnumBlock.Model), JsonValue.Create(model.ToString())));

            // Assert
            Assert.AreEqual(expectedIncluded, harness.BoundServices().Contains("Point2"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.5")]
        public void ResolveAgainstConfiguredValueRatherThanDeclaredDefault()
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), JsonValue.Create(2L)));

            // Assert
            CollectionAssert.Contains(harness.BoundServices().ToArray(), "Point2");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.3")]
        public void RefuseGateReferencingOrdinaryServiceProperty()
        {
            // The evaluation context carries [InstantiationParameter] properties and nothing else, so a gate
            // over a runtime value cannot resolve — structure is not runtime-reactive.

            // Arrange
            var block = new NonParameterReferenceGateBlock();
            var harness = new GatingHarness();

            // Act / Assert
            Assert.ThrowsExactly<PredicateEvaluationException>(() => harness.Configure(block, [nameof(NonParameterReferenceGateBlock), "Point2"]));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.6")]
        public void RefuseGateOutsideGrammar()
        {
            // Arrange
            var block = new UnparseableGateBlock();
            var harness = new GatingHarness();

            // Act / Assert
            Assert.ThrowsExactly<PredicateSyntaxException>(() => harness.Configure(block, [nameof(UnparseableGateBlock), "Point2"]));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.7")]
        public void RefuseGateReferencingUnknownName()
        {
            // Arrange
            var block = new UnknownReferenceGateBlock();
            var harness = new GatingHarness();

            // Act / Assert
            Assert.ThrowsExactly<PredicateEvaluationException>(() => harness.Configure(block, [nameof(UnknownReferenceGateBlock), "Point2"]));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.8")]
        public void RefuseGateOverNullParameterValue()
        {
            // Arrange
            var block = new GatedNullParameterBlock();
            var harness = new GatingHarness();

            // Act / Assert
            Assert.ThrowsExactly<PredicateEvaluationException>(() => harness.Configure(block, [nameof(GatedNullParameterBlock), "Point2"]));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-006.1")]
        public void EmitFullMemberSetWithPredicatesWhenIntrospected()
        {
            // Arrange
            var block = new GatedCountBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, GatingHarness.ServiceProvider);

            // Assert
            CollectionAssert.AreEquivalent(StationServices, result.Services.Select(service => service.Identifier).ToArray());
            Assert.AreEqual("PointCount >= 2", result.Services.Single(service => service.Identifier == "Point2").IncludedWhen);
            Assert.AreEqual("PointCount >= 3", result.Services.Single(service => service.Identifier == "Point3").IncludedWhen);
            Assert.IsNull(result.Services.Single(service => service.Identifier == nameof(GatedCountBlock.Point1)).IncludedWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-006.2")]
        public void IntrospectWithoutEvaluatingAnyPredicate()
        {
            // A predicate no evaluator can accept still introspects: the definition view has no operator
            // values to resolve against, so it records rather than decides.

            // Arrange
            var block = new UnparseableGateBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, GatingHarness.ServiceProvider);

            // Assert
            Assert.AreEqual("PointCount >>> 2", result.Services.Single(service => service.Identifier == "Point2").IncludedWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-001.5")]
        public void ResolveParameterAndGateDeclaredOnBaseClass()
        {
            // Arrange
            var block = new LeafStationBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block,
                              [nameof(LeafStationBlock), nameof(LeafStationBlock.Point1), "Point2"],
                              Parameter(nameof(BaseStationBlock.PointCount), JsonValue.Create(2L)));

            // Assert
            Assert.AreEqual(2, block.PointCount);
            CollectionAssert.Contains(harness.BoundServices().ToArray(), "Point2");
        }

        private static SetLogicConfigurationPayload.InstantiationParameterValue Parameter(string identifier, JsonNode? value)
        {
            return new SetLogicConfigurationPayload.InstantiationParameterValue { Identifier = identifier, Value = value };
        }
    }
}
