using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     Item D — shutdown-hang guard.
    ///     When the internal Configure() throws during InitializeLogicBlock, PersistentData is left
    ///     uninitialised because LogicBlockBase.InitializeLogicBlock runs Configure() (the four
    ///     declarative binders) BEFORE _persistentData.Initialize(). A subsequent
    ///     StopLogicBlockRequest must NOT throw "PersistentData not initialized" and must still
    ///     send StopLogicBlockResponse, otherwise the actor never acks stop and shutdown waits
    ///     out the full timeout.
    /// </summary>
    [TestClass]
    public sealed class LogicBlockShutdownGuardShould
    {
        /// <summary>
        ///     A nested service whose presence (a [ServiceProperty] member) makes
        ///     DeclarativeServiceBinder.BindPropertyBasedServices invoke the owning property's
        ///     getter via reflection.
        /// </summary>
        private sealed class ThrowingInnerService
        {
            [ServiceProperty]
            public int Value { get; set; }
        }

        /// <summary>
        ///     The Inner property's type carries a [ServiceProperty] member, so the declarative
        ///     service binder calls property.GetValue(this) during the internal Configure().
        ///     The getter throws, aborting Configure() before LogicBlockBase reaches
        ///     _persistentData.Initialize() — exactly the production failure scenario this guard
        ///     addresses. This compiles cleanly (no Dale analyzer inspects getter bodies).
        /// </summary>
        private sealed class FailingConfigureLogicBlock : LogicBlockBase
        {
            public FailingConfigureLogicBlock() : base(new Mock<ILogger>().Object)
            {
            }

            public ThrowingInnerService Inner => throw new InvalidOperationException("Simulated Configure() failure: property getter threw during declarative binding.");

            protected override void Ready()
            {
            }

            protected override void Starting()
            {
            }
        }

        /// <summary>
        ///     Minimal IActorContext that records RespondToSender messages so the test can assert
        ///     the StopLogicBlockResponse ack was produced.
        /// </summary>
        private sealed class RecordingActorContext : IActorContext
        {
            public List<object> Responses { get; } = [];

            public IReadOnlyDictionary<string, string>? Headers => null;

            public void SendTo(IActorReference target, object message, Dictionary<string, string>? headers = null)
            {
            }

            public void SendToSelf(object message)
            {
            }

            public void SendToSelfAfter(object message, TimeSpan delay)
            {
            }

            public void RespondToSender(object message)
            {
                Responses.Add(message);
            }

            public IActorReference LookupByName(string name)
            {
                return new RecordingActorReference();
            }
        }

        private sealed class RecordingActorReference : IActorReference
        {
        }

        /// <summary>
        ///     A minimal, fully valid logic block whose Configure() succeeds (no throwing members).
        ///     It exposes one writable [ServiceProperty] so a successful PersistentData.Initialize()
        ///     discovers one persistent entry — letting the happy-path test observe that
        ///     CreateSnapshot() actually ran on stop (the snapshot is non-empty).
        /// </summary>
        private sealed class HealthyLogicBlock : LogicBlockBase
        {
            public HealthyLogicBlock() : base(new Mock<ILogger>().Object)
            {
            }

            [ServiceProperty]
            public int Counter { get; set; }

            protected override void Ready()
            {
            }

            protected override void Starting()
            {
            }
        }

        [TestMethod]
        public void NotThrowAndStillAckStopWhenPersistentDataUninitialised()
        {
            var block = new FailingConfigureLogicBlock();
            var context = new RecordingActorContext();

            // InitializeLogicBlock drives the internal Configure() (the declarative binders).
            // The throwing getter aborts it, so PersistentData is left uninitialised — the exact
            // state the production bug occurs in.
            var initialize = new InitializeLogicBlock(
                "lb-1",
                "FailingConfigureLogicBlock",
                new Dictionary<string, ServiceIdentifier>(),
                new Dictionary<string, LogicBlockContractId>(),
                new Mock<IServiceProvider>().Object);

            // Pre-condition only: initialization must abort (Configure()'s reflection-invoked
            // getter throws) so PersistentData is left uninitialised. We don't pin the wrapper
            // exception type — the binder currently surfaces a TargetInvocationException from
            // PropertyInfo.GetValue, but the behaviour under test is the SUBSEQUENT stop
            // (no-throw + StopLogicBlockResponse), not how init fails. Any throw here satisfies
            // the precondition.
            Assert.Throws<Exception>(
                () => block.HandleMessageAsync(initialize, context).GetAwaiter().GetResult(),
                "Pre-condition: initialization must abort so PersistentData is left uninitialised.");

            context.Responses.Clear();

            // The actor still receives StopLogicBlockRequest during shutdown. Before the guard
            // this threw "PersistentData not initialized" and never sent StopLogicBlockResponse,
            // so the runtime waited out the full shutdown timeout.
            block.HandleMessageAsync(new StopLogicBlockRequest(), context).GetAwaiter().GetResult();

            Assert.IsTrue(
                context.Responses.Any(r => r is StopLogicBlockResponse),
                "StopLogicBlockRequest must send StopLogicBlockResponse even when PersistentData was never initialised.");
        }

        [TestMethod]
        public void NotThrowAndStillRespondToSnapshotRequestWhenPersistentDataUninitialised()
        {
            var block = new FailingConfigureLogicBlock();
            var context = new RecordingActorContext();

            // Same precondition as the StopLogicBlockRequest test: the throwing getter aborts
            // the internal Configure() so PersistentData is left uninitialised.
            var initialize = new InitializeLogicBlock(
                "lb-snap",
                "FailingConfigureLogicBlock",
                new Dictionary<string, ServiceIdentifier>(),
                new Dictionary<string, LogicBlockContractId>(),
                new Mock<IServiceProvider>().Object);

            Assert.Throws<Exception>(
                () => block.HandleMessageAsync(initialize, context).GetAwaiter().GetResult(),
                "Pre-condition: initialization must abort so PersistentData is left uninitialised.");

            context.Responses.Clear();

            // The runtime sends GetPersistentDataSnapshotRequest right after StopLogicBlockRequest
            // during teardown, awaiting an acknowledgement. Before the guard this threw
            // "PersistentData not initialized" and never sent GetPersistentDataSnapshotResponse,
            // so the runtime waited out the full shutdown timeout — one message later than the
            // StopLogicBlockRequest hang.
            block.HandleMessageAsync(new GetPersistentDataSnapshotRequest(), context).GetAwaiter().GetResult();

            var snapshotResponse = context.Responses.OfType<GetPersistentDataSnapshotResponse>().Single();
            Assert.IsNotNull(
                snapshotResponse.PersistentDataValues,
                "GetPersistentDataSnapshotRequest must respond with a (non-null) snapshot even when PersistentData was never initialised.");
            Assert.IsEmpty(
                snapshotResponse.PersistentDataValues,
                "An uninitialised PersistentData has no entries, so the snapshot must be empty.");
        }

        [TestMethod]
        public void StillSnapshotAndAckStopOnTheNormallyInitialisedPath()
        {
            var block = new HealthyLogicBlock();
            var context = new RecordingActorContext();

            // Configure() succeeds for this block, so InitializeLogicBlock runs through to
            // _persistentData.Initialize() — the normal (initialised) state.
            var initialize = new InitializeLogicBlock(
                "lb-healthy",
                "HealthyLogicBlock",
                new Dictionary<string, ServiceIdentifier>(),
                new Dictionary<string, LogicBlockContractId>(),
                new Mock<IServiceProvider>().Object);

            block.HandleMessageAsync(initialize, context).GetAwaiter().GetResult();

            block.HandleMessageAsync(new StopLogicBlockRequest(), context).GetAwaiter().GetResult();

            // (a) The stop must still be acked on the initialised path.
            Assert.IsTrue(
                context.Responses.Any(r => r is StopLogicBlockResponse),
                "StopLogicBlockRequest must send StopLogicBlockResponse on the normal initialised path.");

            // (b) The guard must NOT skip the snapshot on the initialised path. We observe the
            // snapshot via GetPersistentDataSnapshotRequest, which the handler answers with
            // GetPersistentDataSnapshotResponse(Id, _persistentData.GetCurrentSnapshot()).
            // On the uninitialised path GetCurrentSnapshot() would itself throw, so a returned
            // snapshot that contains the discovered [ServiceProperty] entry proves
            // CreateSnapshot() ran during StopLogicBlockRequest and the guard left normal
            // shutdown persistence intact.
            context.Responses.Clear();
            block.HandleMessageAsync(new GetPersistentDataSnapshotRequest(), context).GetAwaiter().GetResult();

            var snapshotResponse = context.Responses.OfType<GetPersistentDataSnapshotResponse>().Single();
            Assert.IsNotEmpty(
                snapshotResponse.PersistentDataValues,
                "Stop on the initialised path must still take a persistent-data snapshot (the discovered [ServiceProperty] must be captured).");
        }
    }
}
