using System;
using System.Linq;
using System.Text.Json.Nodes;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     An <c>[InstantiationParameter]</c> is chosen at configuration time and the inclusion gates are
    ///     resolved against it, so the value must not move while the block runs
    ///     (<c>docs/specs/config-gating.md</c>).
    /// </summary>
    [TestClass]
    public sealed class InstantiationParameterImmutabilityShould
    {
        private static readonly string[] StationServices = [nameof(GatedCountBlock), nameof(GatedCountBlock.Point1), "Point2", "Point3"];

        [TestMethod]
        [TestProperty("spec", "AC-GATE-003.1")]
        public void RefuseWriteToInstantiationParameter()
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 2));

            // Act
            harness.Send(block, new SetServicePropertyValueRequest(nameof(GatedCountBlock), nameof(GatedCountBlock.PointCount), 3));

            // Assert
            Assert.AreEqual(2, block.PointCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-003.2")]
        public void AnswerRefusedWriteWithUnchangedValue()
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 2));

            // Act
            harness.Send(block, new SetServicePropertyValueRequest(nameof(GatedCountBlock), nameof(GatedCountBlock.PointCount), 3));

            // Assert
            var response = harness.Responses.OfType<SetServicePropertyValueResponse>().Single();
            Assert.AreEqual(nameof(GatedCountBlock.PointCount), response.PropertyIdentifier);
            Assert.AreEqual(2, response.Value);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-003.1")]
        public void ApplyWriteToOrdinaryServiceProperty()
        {
            // The refusal is narrow: it reads the declaration, not the request. Without this row a guard
            // that refused every write would still pass the two above.

            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 2));

            // Act
            harness.Send(block, new SetServicePropertyValueRequest(nameof(GatedCountBlock.Point1), nameof(GatedPoint.Active), true));

            // Assert
            Assert.IsTrue(block.Point1.Active);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-004.1")]
        public void RefuseSecondConfiguration()
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 2));

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Send(block,
                                                                                             GatingHarness.Initialize(StationServices,
                                                                                                                      Parameter(nameof(GatedCountBlock.PointCount), 3))));

            StringAssert.Contains(failure.Message, "already configured");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-004.1")]
        public void KeepBoundMembersOnRefusedSecondConfiguration()
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 2));

            // Act
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Send(block, GatingHarness.Initialize(StationServices, Parameter(nameof(GatedCountBlock.PointCount), 3))));

            // Assert
            CollectionAssert.AreEquivalent(new[] { nameof(GatedCountBlock), nameof(GatedCountBlock.Point1), "Point2" }, harness.BoundServices().ToArray());
            Assert.AreEqual(2, block.PointCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-004.1")]
        public void NameEarlierFailureWhenRefusingAfterFailedConfiguration()
        {
            // A configuration that threw still spends the instance — the binders may have registered half a
            // member set before it failed. Refusing the retry is right; refusing it with "re-activate the
            // configuration" would send the operator back to the thing that just failed, so the refusal
            // carries the original reason instead.

            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block, StationServices, Parameter("Nonexistent", 2)));

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Send(block,
                                                                                             GatingHarness.Initialize(StationServices,
                                                                                                                      Parameter(nameof(GatedCountBlock.PointCount), 2))));

            StringAssert.Contains(failure.Message, "Nonexistent");
            StringAssert.Contains(failure.Message, "Re-instantiate");
        }

        private static SetLogicConfigurationPayload.InstantiationParameterValue Parameter(string identifier, int value)
        {
            // Integers ride the wire long-backed, so the node has to be created as one to decode.
            return new SetLogicConfigurationPayload.InstantiationParameterValue { Identifier = identifier, Value = JsonValue.Create((long)value) };
        }
    }
}