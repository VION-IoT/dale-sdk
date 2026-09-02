using System;
using System.Linq;
using System.Text.Json.Nodes;
using Vion.Contracts.Conventions;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Configuration
{
    /// <summary>
    ///     What an excluded member loses, and what an exclusion deliberately leaves alone
    ///     (<c>docs/specs/config-gating.md</c>). Gating removes a bound unit — a component's whole service, a
    ///     contract binding, an interface endpoint — never a lone scalar.
    /// </summary>
    [TestClass]
    public sealed class GatedMemberBindingShould
    {
        private static readonly string[] StationServices = [nameof(GatedCountBlock), nameof(GatedCountBlock.Point1), "Point2", "Point3"];

        [TestMethod]
        [TestProperty("spec", "AC-GATE-007.3")]
        public void BindNeitherPropertiesNorMeasuringPointsOfExcludedComponent()
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 1));

            // Assert
            Assert.IsEmpty(harness.BoundProperties("Point2"));
            Assert.IsEmpty(harness.BoundMeasuringPoints("Point2"));
            Assert.IsNotEmpty(harness.BoundProperties(nameof(GatedCountBlock.Point1)));
            Assert.IsNotEmpty(harness.BoundMeasuringPoints(nameof(GatedCountBlock.Point1)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-007.2")]
        [DataRow(2, true, DisplayName = "the gate includes the binding")]
        [DataRow(1, false, DisplayName = "the gate excludes the binding")]
        public void ConstructContractBindingOnlyWhileIncluded(int pointCount, bool expectedConstructed)
        {
            // A contract is what the binder constructs, so an excluded one is null rather than inert — the
            // documented authoring hazard, and why a gated contract property is declared nullable and its
            // fan-out null-guarded.

            // Arrange
            var block = new GatedContractBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, [nameof(GatedContractBlock)], Parameter(nameof(GatedContractBlock.PointCount), pointCount));

            // Assert
            Assert.AreEqual(expectedConstructed, block.Point2Output is not null);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-007.4")]
        public void LeaveExcludedComponentInstanceReachable()
        {
            // Block code holds the component through its own property and drives it unconditionally — an
            // excluded component is inert, not absent, so a timer that samples every point still compiles
            // and runs.

            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 1));

            // Assert
            Assert.IsNotNull(block.Point2);
            block.Point2.Active = true;
            Assert.IsTrue(block.Point2.Active);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-007.5")]
        public void BindRootServiceWithEveryGateClosed()
        {
            // Whole-block existence is the operator adding the instance or not, and the parameters the gates
            // read live on this service — so it is never gated.

            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 1));

            // Assert
            CollectionAssert.Contains(harness.BoundProperties(nameof(GatedCountBlock)).ToArray(), nameof(GatedCountBlock.PointCount));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-007.8")]
        public void SkipInterfaceBindingHoldingNullWhenConfiguring()
        {
            // The other half of the definition-view relaxation: an endpoint with nothing behind it can be
            // described, but it cannot serve, so a configured instance skips it. Asserted on the bound
            // interface set — the bound service map cannot see this, because the component declares no
            // service member and so is absent from it whatever the binder decides.

            // Arrange
            var block = new NullInterfaceComponentBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, [nameof(NullInterfaceComponentBlock)], Parameter(nameof(NullInterfaceComponentBlock.Count), 2));

            // Assert
            Assert.IsEmpty(block.BoundInterfaceIdentifiers());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-007.8")]
        public void DescribeInterfaceBindingHoldingNullWhenIntrospecting()
        {
            // The same block through the definition view: the endpoint is there, so the two halves of the
            // guard are pinned by two tests over one fixture rather than by one that cannot tell them apart.

            // Arrange
            var block = new NullInterfaceComponentBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, GatingHarness.ServiceProvider);

            // Assert
            var endpoint = result.Interfaces.Single(iface => iface.Identifier.StartsWith(nameof(NullInterfaceComponentBlock.Probe), StringComparison.Ordinal));
            Assert.AreEqual("Count >= 2", endpoint.Annotations[LogicBlockWiringConventions.IncludedWhenAnnotationKey]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-007.7")]
        public void OmitGatedComponentHoldingNullFromDefinitionView()
        {
            // The definition view enumerates a component's members off the instance, so a null one has
            // nothing to describe — the member and its gate are both absent, and an author reading the
            // catalog cannot tell the gate was ever declared.

            // Arrange
            var block = new NullComponentBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, GatingHarness.ServiceProvider);

            // Assert
            CollectionAssert.AreEquivalent(new[] { nameof(NullComponentBlock), nameof(NullComponentBlock.Point1) },
                                           result.Services.Select(service => service.Identifier).ToArray());
        }

        private static SetLogicConfigurationPayload.InstantiationParameterValue Parameter(string identifier, int value)
        {
            return new SetLogicConfigurationPayload.InstantiationParameterValue { Identifier = identifier, Value = JsonValue.Create((long)value) };
        }
    }
}