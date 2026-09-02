using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Persistence;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Persistence
{
    /// <summary>
    ///     Persistence follows the configured shape, not the declared one
    ///     (<c>docs/specs/config-gating.md</c>). There is no dormancy: an excluded member's state is neither
    ///     captured nor restored, so the same file survives a gate flip on either side of it.
    /// </summary>
    [TestClass]
    public sealed class GatedPersistenceShould
    {
        private static readonly string[] ProbeServices = [nameof(GatedInterfaceBlock), "Point2"];

        private static readonly string[] StationServices = [nameof(GatedCountBlock), nameof(GatedCountBlock.Point1), "Point2", "Point3"];

        [TestMethod]
        [TestProperty("spec", "AC-GATE-003.3")]
        public void CaptureNoInstantiationParameter()
        {
            // A parameter has a public setter so the platform can apply it, which is exactly what persistence
            // discovery keys off — so it needs its own exclusion, or a stale persisted value would overwrite
            // the configured one after the gates had already resolved against it.

            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 2));

            // Act
            var keys = harness.SnapshotKeys(block);

            // Assert
            Assert.IsFalse(keys.Any(key => key.Contains(nameof(GatedCountBlock.PointCount))));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-009.1")]
        public void CaptureIncludedComponentStateOnly()
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 2));

            // Act
            var keys = harness.SnapshotKeys(block);

            // Assert
            CollectionAssert.Contains(keys.ToArray(), "_direct.Point1.Energy");
            CollectionAssert.Contains(keys.ToArray(), "_direct.Point2.Energy");
            CollectionAssert.DoesNotContain(keys.ToArray(), "_direct.Point3.Energy");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-009.1")]
        [DataRow(2, true, DisplayName = "the gate includes the component")]
        [DataRow(1, false, DisplayName = "the gate excludes the component")]
        public void CaptureInterfaceOnlyComponentStateByGateRatherThanBinding(int count, bool expectedCaptured)
        {
            // A component bound only through its interface declares no service member, so it is absent from
            // the service-binding keys entirely — inferring the included set from those would drop its state
            // while the gate says to keep it.

            // Arrange
            var block = new GatedInterfaceBlock();
            var harness = new GatingHarness();
            harness.Configure(block, ProbeServices, Parameter(nameof(GatedInterfaceBlock.Count), count));

            // Act
            var keys = harness.SnapshotKeys(block);

            // Assert
            Assert.AreEqual(expectedCaptured, keys.Contains("_direct.Probe.Energy"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-009.2")]
        public void StopCapturingComponentStateOnceItsGateCloses()
        {
            // No dormancy: the same member's state is captured under a configuration that includes it and
            // absent under one that does not, so a lowered count loses that component's history.

            // Arrange
            var included = new GatedCountBlock();
            var includedHarness = new GatingHarness();
            includedHarness.Configure(included, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 2));

            var excluded = new GatedCountBlock();
            var excludedHarness = new GatingHarness();
            excludedHarness.Configure(excluded, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 1));

            // Act
            var afterInclusion = includedHarness.SnapshotKeys(included);
            var afterExclusion = excludedHarness.SnapshotKeys(excluded);

            // Assert
            CollectionAssert.Contains(afterInclusion.ToArray(), "_direct.Point2.Energy");
            CollectionAssert.DoesNotContain(afterExclusion.ToArray(), "_direct.Point2.Energy");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-003.4")]
        public void LeaveStateUnchangedWhenRestoringExcludedMember()
        {
            // The file a host wrote under a wider configuration still names members this one excluded. The
            // restore passes over them and completes, which is what makes the flip above safe in both
            // directions.

            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), 1));

            // Act
            harness.Send(block,
                         new RestorePersistentDataRequest(new List<PersistentDataEntry>
                                                          {
                                                              new("_direct.Point2.Energy", typeof(double).FullName!, 42.0),
                                                          }));

            // Assert
            Assert.AreEqual(0.0, block.Point2.Energy);
            Assert.HasCount(1, harness.Responses.OfType<RestorePersistentDataResponse>().ToList());
        }

        private static SetLogicConfigurationPayload.InstantiationParameterValue Parameter(string identifier, int value)
        {
            return new SetLogicConfigurationPayload.InstantiationParameterValue { Identifier = identifier, Value = JsonValue.Create((long)value) };
        }
    }
}