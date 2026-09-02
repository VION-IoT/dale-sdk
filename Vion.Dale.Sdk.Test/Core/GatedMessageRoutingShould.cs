using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     A configuration can name an endpoint this instance's gates removed — a stale payload, a topology
    ///     whose count was lowered, a sender that has not caught up. Every such path keeps the block running
    ///     (<c>docs/specs/config-gating.md</c>): a gate narrows what exists, it does not make a peer's message
    ///     fatal.
    /// </summary>
    [TestClass]
    public sealed class GatedMessageRoutingShould
    {
        private const string ClassInterface = nameof(IGatedProbeSink);

        private const string PropertyInterface = $"{nameof(GatedInterfaceBlock.Probe)}_{nameof(IGatedProbeSink)}";

        private static readonly string[] ProbeServices = [nameof(GatedInterfaceBlock), "Point2"];

        [TestMethod]
        [TestProperty("spec", "AC-GATE-008.3")]
        [DataRow(2, 7, DisplayName = "the gate includes the endpoint")]
        [DataRow(1, 0, DisplayName = "the gate excludes the endpoint")]
        public void DeliverToGatedInterfaceEndpointOnlyWhileIncluded(int count, int expectedPoll)
        {
            // An excluded endpoint is never bound, so the message has nowhere to go. Dropping it keeps the
            // receiving block up; the row above is the control that proves the drop is the gate's doing.

            // Arrange
            var block = new GatedInterfaceBlock();
            var harness = new GatingHarness();
            harness.Configure(block, ProbeServices, Parameter(nameof(GatedInterfaceBlock.Count), count));

            // Act
            harness.Send(block, Poll(PropertyInterface));

            // Assert
            Assert.AreEqual(expectedPoll, block.Probe.LastPoll);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-007.6")]
        public void DeliverToClassImplementedInterfaceWithEveryGateClosed()
        {
            // A class-implemented interface has no member to carry a gate, so it binds whatever the
            // parameters say.

            // Arrange
            var block = new GatedInterfaceBlock();
            var harness = new GatingHarness();
            harness.Configure(block, ProbeServices, Parameter(nameof(GatedInterfaceBlock.Count), 1));

            // Act
            harness.Send(block, Poll(ClassInterface));

            // Assert
            Assert.AreEqual(7, block.LastPoll);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-008.2")]
        public void KeepDeliveringAfterLinkToExcludedInterface()
        {
            // Arrange
            var block = new GatedInterfaceBlock();
            var harness = new GatingHarness();
            harness.Configure(block, ProbeServices, Parameter(nameof(GatedInterfaceBlock.Count), 1));

            // Act
            harness.Send(block,
                         new SetLinkedInterfaces(new Dictionary<InterfaceId, Dictionary<InterfaceId, IActorReference>>
                                                 {
                                                     [new InterfaceId("cfg", PropertyInterface)] = new()
                                                                                                   {
                                                                                                       [new InterfaceId("peer",
                                                                                                                            "IGatedProbeSource")] =
                                                                                                           harness.Context.LookupByName("peer"),
                                                                                                   },
                                                 }));
            harness.Send(block, Poll(ClassInterface));

            // Assert
            Assert.AreEqual(7, block.LastPoll);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-008.1")]
        public void LeaveExcludedContractUnmappedAndKeepConfiguring()
        {
            // A stale payload still maps the excluded contract. The mapping is skipped rather than taking the
            // whole block down, and the contract stays excluded rather than being conjured by the mapping.

            // Arrange
            var block = new GatedContractBlock();
            var harness = new GatingHarness();

            // Act
            harness.Link(block);
            harness.Send(block,
                         GatingHarness.Initialize([nameof(GatedContractBlock)], [nameof(GatedContractBlock.Point2Output)], Parameter(nameof(GatedContractBlock.PointCount), 1)));

            // Assert
            Assert.IsNull(block.Point2Output);
            CollectionAssert.Contains(harness.BoundProperties(nameof(GatedContractBlock)).ToArray(), nameof(GatedContractBlock.PointCount));
        }

        private static FunctionInterfaceMessage<GatedProbeLink.Poll> Poll(string interfaceIdentifier)
        {
            return new FunctionInterfaceMessage<GatedProbeLink.Poll>(new InterfaceId("peer", "IGatedProbeSource"),
                                                                     new InterfaceId("cfg", interfaceIdentifier),
                                                                     new GatedProbeLink.Poll(7));
        }

        private static SetLogicConfigurationPayload.InstantiationParameterValue Parameter(string identifier, int value)
        {
            return new SetLogicConfigurationPayload.InstantiationParameterValue { Identifier = identifier, Value = JsonValue.Create((long)value) };
        }
    }
}