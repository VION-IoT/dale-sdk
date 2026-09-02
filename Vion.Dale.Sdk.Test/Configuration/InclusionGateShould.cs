using System;
using System.Linq;
using System.Text.Json.Nodes;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Contracts.Predicates;
using Vion.Dale.Sdk.Configuration;
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
        public void IntrospectWithoutDecidingAnyGate()
        {
            // The definition view records a workable gate rather than deciding it: the default instance's
            // values are nobody's configuration, so every gated member is present whatever they say.

            // Arrange
            var block = new GatedCountBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, GatingHarness.ServiceProvider);

            // Assert
            CollectionAssert.Contains(result.Services.Select(service => service.Identifier).ToArray(), "Point3");
            Assert.AreEqual("PointCount >= 3", result.Services.Single(service => service.Identifier == "Point3").IncludedWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-006.4")]
        public void RefuseIntrospectingGateOutsideGrammar()
        {
            // Introspection is what `dotnet pack` runs, so refusing here is what stops an artifact shipping a
            // block that fails every activation.

            // Arrange
            var block = new UnparseableGateBlock();

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => LogicBlockIntrospection.IntrospectLogicBlock(block, GatingHarness.ServiceProvider));

            StringAssert.Contains(failure.Message, "Point2");
            StringAssert.Contains(failure.Message, "PointCount >>> 2");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-006.4")]
        public void RefuseIntrospectingGateReferencingUnknownName()
        {
            // Arrange
            var block = new UnknownReferenceGateBlock();

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => LogicBlockIntrospection.IntrospectLogicBlock(block, GatingHarness.ServiceProvider));

            StringAssert.Contains(failure.Message, "Missing >= 2");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-006.4")]
        [DataRow("PointCount >= 5 && Missing >= 1", DisplayName = "behind a conjunction")]
        [DataRow("PointCount <= 5 || Missing >= 1", DisplayName = "behind a disjunction")]
        [DataRow("(PointCount >= 5 && (Missing >= 1))", DisplayName = "nested in parentheses")]
        [DataRow("!(PointCount >= 5 && Missing >= 1)", DisplayName = "under a negation")]
        [DataRow("PointCount >= 5 && Missing in [1, 2]", DisplayName = "in a membership list")]
        public void RefuseIntrospectingCompoundGateReferencingUnknownName(string predicate)
        {
            // Every shape here decides without reaching the operand that names 'Missing', so an evaluator
            // returns a verdict and never learns the name is undeclared. The references are read off the
            // parsed tree instead, which sees the whole predicate whatever it would evaluate to.

            // Arrange
            var block = new GatedCountBlock();

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => InclusionGate.EnsureResolvable(predicate, block, "Point2"));

            StringAssert.Contains(failure.Message, "Point2");
            StringAssert.Contains(failure.Message, predicate);
            StringAssert.Contains(failure.Message, "'Missing' is not an [InstantiationParameter] of this block");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-006.1")]
        public void IntrospectGateOverBareBooleanReference()
        {
            // A bare reference is the only gate whose tree is a lone reference node, so it is the only one that
            // proves the walk handles that node kind at all — every other shape reaches its references through
            // a comparison or a membership list.

            // Arrange
            var block = new GatedBoolParameterBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, GatingHarness.ServiceProvider);

            // Assert
            Assert.AreEqual("Enabled", result.Services.Single(service => service.Identifier == "Point2").IncludedWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.2")]
        [DataRow(true, true, DisplayName = "the parameter is true")]
        [DataRow(false, false, DisplayName = "the parameter is false")]
        public void BindOnBareBooleanReference(bool enabled, bool expectedIncluded)
        {
            // Arrange
            var block = new GatedBoolParameterBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, [nameof(GatedBoolParameterBlock), "Point2"], Parameter(nameof(GatedBoolParameterBlock.Enabled), JsonValue.Create(enabled)));

            // Assert
            Assert.AreEqual(expectedIncluded, harness.BoundServices().Contains("Point2"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-006.5")]
        public void IntrospectGateOverNullDefaultedParameter()
        {
            // The second reason resolvability is read off the tree and never evaluated: this gate is
            // perfectly good and fails closed at bind only because no value was configured. Evaluating here,
            // against the block's own null default, would refuse the block outright.

            // Arrange
            var block = new GatedNullParameterBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, GatingHarness.ServiceProvider);

            // Assert
            Assert.AreEqual("Region == 'EU'", result.Services.Single(service => service.Identifier == "Point2").IncludedWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-001.5")]
        public void ResolveParameterAndGateDeclaredOnBaseClass()
        {
            // Arrange
            var block = new LeafStationBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, [nameof(LeafStationBlock), nameof(LeafStationBlock.Point1), "Point2"], Parameter(nameof(BaseStationBlock.PointCount), JsonValue.Create(2L)));

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