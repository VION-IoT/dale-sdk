using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Configuration.Interfaces
{
    /// <summary>
    ///     What a generated sender promises the block that calls it: who a message reaches, whose identity it
    ///     carries, and what happens when the endpoint it names is not linked. Each test drives the block
    ///     through its configuration and then relays what the recording context captured to the far block by
    ///     hand, which is what a host's actor system would have done.
    /// </summary>
    [TestClass]
    public class InterfaceMessagingShould
    {
        private const string SourceEndpoint = nameof(IBindSource);

        private static readonly InterfaceId SinkId = new(new LogicBlockId("sink-block"), $"{nameof(BindSinkBlock.Endpoint)}_{nameof(IBindSink)}");

        private static readonly InterfaceId SourceId = new(new LogicBlockId("block-1"), SourceEndpoint);

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.1")]
        public void AddressMessageBySendingAndReceivingEndpoint()
        {
            // Arrange
            var harness = new LifecycleHarness();
            var block = new BindSourceBlock();
            harness.Configure(block, serviceProvider: BindHosts.Bare);
            harness.Send(block, LinkedInterfaces(SourceId, SinkId));

            // Act
            block.SendCommand(SinkId, new BindLinkContract.Nudge(4));

            // Assert
            var envelope = harness.Published.OfType<FunctionInterfaceMessage<BindLinkContract.Nudge>>().Single();
            Assert.AreEqual(SourceId, envelope.FromId);
            Assert.AreEqual(SinkId, envelope.ToId);
            Assert.AreEqual(4, envelope.Data.Amount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.2")]
        public void SendCommandToOneNamedEndpointOnly()
        {
            // Arrange
            var harness = new LifecycleHarness();
            var block = new BindSourceBlock();
            harness.Configure(block, serviceProvider: BindHosts.Bare);
            var secondSink = new InterfaceId(new LogicBlockId("sink-block2"), "Endpoint_IBindSink");
            harness.Send(block, LinkedInterfaces(SourceId, SinkId, secondSink));

            // Act
            block.SendCommand(SinkId, new BindLinkContract.Nudge(4));

            // Assert
            var recipients = harness.Published.OfType<FunctionInterfaceMessage<BindLinkContract.Nudge>>().Select(envelope => envelope.ToId);
            CollectionAssert.AreEqual(new[] { SinkId }, recipients.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.2")]
        public void SendStateUpdateToEveryLinkedEndpoint()
        {
            // Arrange
            var harness = new LifecycleHarness();
            var block = new BindSinkBlock();
            harness.Configure(block, serviceProvider: BindHosts.Bare);
            var secondSource = new InterfaceId(new LogicBlockId("source-block2"), SourceEndpoint);
            harness.Send(block, LinkedInterfaces(SinkOf(block), SourceId, secondSource));

            // Act
            block.Endpoint!.SendStateUpdate(new BindLinkContract.Level(7));

            // Assert
            var recipients = harness.Published.OfType<FunctionInterfaceMessage<BindLinkContract.Level>>().Select(envelope => envelope.ToId);
            CollectionAssert.AreEquivalent(new[] { SourceId, secondSource }, recipients.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.3")]
        public void DeliverStateUpdateWithSenderIdentity()
        {
            // Arrange
            var sinkHarness = new LifecycleHarness();
            var sink = new BindSinkBlock();
            sinkHarness.Configure(sink, serviceProvider: BindHosts.Bare);
            sinkHarness.Send(sink, LinkedInterfaces(SinkOf(sink), SourceId));
            var sourceHarness = new LifecycleHarness();
            var source = new BindSourceBlock();
            sourceHarness.Configure(source, serviceProvider: BindHosts.Bare);

            // Act
            sink.Endpoint!.SendStateUpdate(new BindLinkContract.Level(7));
            Relay(sinkHarness, sourceHarness, source);

            // Assert
            CollectionAssert.AreEqual(new[] { new BindLinkContract.Level(7) }, source.Levels);
            CollectionAssert.AreEqual(new[] { SinkOf(sink) }, source.LevelSenders);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.3")]
        public void DeliverCommandWithoutSenderIdentity()
        {
            // Arrange
            var sourceHarness = new LifecycleHarness();
            var source = new BindSourceBlock();
            sourceHarness.Configure(source, serviceProvider: BindHosts.Bare);
            sourceHarness.Send(source, LinkedInterfaces(SourceId, SinkId));
            var sinkHarness = new LifecycleHarness();
            var sink = new BindSinkBlock();
            sinkHarness.Configure(sink, serviceProvider: BindHosts.Bare);

            // Act
            source.SendCommand(SinkId, new BindLinkContract.Nudge(4));
            Relay(sourceHarness, sinkHarness, sink);

            // Assert — HandleCommand takes the payload and nothing else, so the sender is not in reach.
            CollectionAssert.AreEqual(new[] { new BindLinkContract.Nudge(4) }, sink.Endpoint!.Nudges);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.4")]
        public void DropMessageNamingUnlinkedEndpoint()
        {
            // Arrange
            var harness = new LifecycleHarness();
            var block = new BindSourceBlock();
            harness.Configure(block, serviceProvider: BindHosts.Bare);
            harness.Send(block, LinkedInterfaces(SourceId, SinkId));

            // Act
            block.SendCommand(new InterfaceId(new LogicBlockId("nobody"), "Nowhere"), new BindLinkContract.Nudge(4));

            // Assert
            Assert.IsEmpty(harness.Published.OfType<FunctionInterfaceMessage<BindLinkContract.Nudge>>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.4")]
        public void SendNothingFromEndpointWithNoLinks()
        {
            // Arrange
            var harness = new LifecycleHarness();
            var block = new BindSinkBlock();
            harness.Configure(block, serviceProvider: BindHosts.Bare);

            // Act
            block.Endpoint!.SendStateUpdate(new BindLinkContract.Level(7));

            // Assert
            Assert.IsEmpty(harness.Published.OfType<FunctionInterfaceMessage<BindLinkContract.Level>>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.5")]
        public void AnswerRequestOnRespondingEndpointsOwnLinks()
        {
            // Arrange
            var sourceHarness = new LifecycleHarness();
            var source = new BindSourceBlock();
            sourceHarness.Configure(source, serviceProvider: BindHosts.Bare);
            sourceHarness.Send(source, LinkedInterfaces(SourceId, SinkId));
            var sinkHarness = new LifecycleHarness();
            var sink = new BindSinkBlock();
            sinkHarness.Configure(sink, serviceProvider: BindHosts.Bare);
            sinkHarness.Send(sink, LinkedInterfaces(SinkOf(sink), SourceId));

            // Act
            source.SendRequest(SinkId, new BindLinkContract.Poll(3));
            Relay(sourceHarness, sinkHarness, sink);
            Relay(sinkHarness, sourceHarness, source);

            // Assert
            CollectionAssert.AreEqual(new[] { new BindLinkContract.Reading(30) }, source.Readings);
            CollectionAssert.AreEqual(new[] { SinkOf(sink) }, source.Responders);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.5")]
        public void DropAnswerWhenResponderLacksLinkBack()
        {
            // Arrange
            var sourceHarness = new LifecycleHarness();
            var source = new BindSourceBlock();
            sourceHarness.Configure(source, serviceProvider: BindHosts.Bare);
            sourceHarness.Send(source, LinkedInterfaces(SourceId, SinkId));
            var sinkHarness = new LifecycleHarness();
            var sink = new BindSinkBlock();
            sinkHarness.Configure(sink, serviceProvider: BindHosts.Bare);

            // Act
            source.SendRequest(SinkId, new BindLinkContract.Poll(3));
            Relay(sourceHarness, sinkHarness, sink);

            // Assert — the sink answered, but its own endpoint has no link to answer over.
            CollectionAssert.AreEqual(new[] { new BindLinkContract.Poll(3) }, sink.Endpoint!.Polls);
            Assert.IsEmpty(sinkHarness.Published.OfType<FunctionInterfaceMessage<BindLinkContract.Reading>>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-006.6")]
        public void ReplaceEndpointLinksWhenSecondMapArrives()
        {
            // Arrange
            var harness = new LifecycleHarness();
            var block = new BindSourceBlock();
            harness.Configure(block, serviceProvider: BindHosts.Bare);
            harness.Send(block, LinkedInterfaces(SourceId, SinkId));

            // Act
            harness.Send(block, LinkedInterfaces(SourceId));

            // Assert
            Assert.IsEmpty(block.GetLinkedBindSinks());
        }

        /// <summary>The endpoint identifier a sink block's own component was bound under.</summary>
        private static InterfaceId SinkOf(BindSinkBlock block)
        {
            return new InterfaceId(new LogicBlockId("block-1"), block.BoundInterfaceIdentifiers().Single());
        }

        /// <summary>A link map naming one of the block's endpoints and the counterparts it reaches.</summary>
        private static SetLinkedInterfaces LinkedInterfaces(InterfaceId endpoint, params InterfaceId[] counterparts)
        {
            var links = counterparts.ToDictionary(counterpart => counterpart, IActorReference (counterpart) => new LifecycleHarness.NamedReference(counterpart.ToString()));
            return new SetLinkedInterfaces(new Dictionary<InterfaceId, Dictionary<InterfaceId, IActorReference>> { [endpoint] = links });
        }

        /// <summary>Hands every inter-block message one harness captured to the block behind the other.</summary>
        private static void Relay(LifecycleHarness from, LifecycleHarness to, LogicBlockBase target)
        {
            foreach (var message in from.Published.OfType<IFunctionInterfaceMessage>().ToList())
            {
                to.Send(target, message);
            }

            from.Context.Sent.Clear();
            from.Context.Log.Clear();
        }
    }
}