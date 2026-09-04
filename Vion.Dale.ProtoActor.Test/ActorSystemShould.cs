using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.ProtoActor.Test.TestHelpers;
using Vion.Dale.Sdk;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Diagnostics;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using IActorSystem = Vion.Dale.Sdk.Abstractions.IActorSystem;

namespace Vion.Dale.ProtoActor.Test
{
    /// <summary>
    ///     The actor system a host composes: how an actor is spawned, what its scope owns, and what the two
    ///     waits — acknowledgement and termination — guarantee a caller who is driving a lifecycle step.
    ///     <para>
    ///         Every test here runs over a real actor system on the real clock, because the claims are about
    ///         delivery: an acknowledgement reaching a waiter, a duplicate name being refused, a scope being
    ///         disposed. <c>testing-conventions.md</c> section 16 governs the waiting — a
    ///         <see cref="SemaphoreSlim" /> or the wait's own task, never a sleep. Where a timeout is the
    ///         subject, its <em>expiry</em> is the assertion, which is the shape load can only make more likely
    ///         to hold.
    ///     </para>
    /// </summary>
    [TestClass]
    public sealed class ActorSystemShould
    {
        private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

        private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(200);

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-015.1")]
        public async Task SpawnFromFactoryAndFromType()
        {
            // Arrange
            await using var host = new PipelineHost();

            // Act
            var fromFactory = host.System.CreateRootActorFor(() => new SilentReceiver(), "spawn_factory");
            var fromRuntimeType = host.System.CreateRootActorFromDi(typeof(SilentReceiver), "spawn_runtime_type");
            var fromCompileTimeType = host.System.CreateRootActorFromDi<SilentReceiver>("spawn_compile_time_type");

            // Assert
            var names = host.System.FindByName(new Regex("^spawn_")).Count;
            Assert.AreEqual(3, names, "Each of the three spawns must register an actor under the name it was given.");
            Assert.IsNotNull(fromFactory);
            Assert.IsNotNull(fromRuntimeType);
            Assert.IsNotNull(fromCompileTimeType);
            await Task.CompletedTask;
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-015.2")]
        public async Task DisposeResolvedDependencyWhenActorTerminates()
        {
            // Arrange
            await using var host = new PipelineHost();
            var actor = host.System.CreateRootActorFromDi<DependentReceiver>("scope_owner");

            // Act
            await host.System.StopActorsAndWaitAsync([actor], Generous);
            await host.Disposals.WaitAsync(Generous);

            // Assert
            Assert.AreEqual(1,
                            Volatile.Read(ref host.Tracker.DisposeCount),
                            "A receiver's dependency is resolved from a per-actor scope, so the actor's termination must reclaim it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-015.3")]
        public async Task ConstructReceiverFreshPerActorWhateverItsRegisteredLifetime()
        {
            // Arrange
            await using var host = new PipelineHost();

            // Act
            host.System.CreateRootActorFromDi<SingletonRegisteredReceiver>("singleton_registered_1");
            host.System.CreateRootActorFromDi<SingletonRegisteredReceiver>("singleton_registered_2");

            // Assert
            Assert.AreEqual(2,
                            Volatile.Read(ref SingletonRegisteredReceiver.Constructions),
                            "A receiver is constructed for its actor rather than resolved, so registering it as a singleton yields one instance per actor all the same.");
            await Task.CompletedTask;
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-015.4")]
        public async Task LeaveReceiverUndisposedWhenActorTerminates()
        {
            // Arrange
            await using var host = new PipelineHost();
            var actor = host.System.CreateRootActorFromDi<SelfDisposingReceiver>("self_disposing");

            // Act
            await host.System.StopActorsAndWaitAsync([actor], Generous);
            await host.System.ShutdownAsync();

            // Assert
            Assert.AreEqual(0,
                            Volatile.Read(ref SelfDisposingReceiver.Disposals),
                            "The scope owns the receiver's dependencies and not the receiver, so a receiver implementing IDisposable is never disposed and its stop hook is its only release point.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-015.5")]
        public async Task RefuseSpawnUnderNameAlreadyTaken()
        {
            // Arrange
            await using var host = new PipelineHost();
            host.System.CreateRootActorFromDi<SilentReceiver>("taken");

            // Act / Assert
            Assert.ThrowsExactly<Proto.ProcessNameExistException>(() => host.System.CreateRootActorFromDi<SilentReceiver>("taken"),
                                                                  "Two actors under one name would share a mailbox, so the second spawn must be refused.");
            await Task.CompletedTask;
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-016.1")]
        public async Task ReturnEveryAcknowledgementWhenEveryActorAnswers()
        {
            // Arrange
            await using var host = new PipelineHost();
            var actor1 = host.System.CreateRootActorFromDi<AcknowledgingReceiver>("ack_1");
            var actor2 = host.System.CreateRootActorFromDi<AcknowledgingReceiver>("ack_2");

            // Act
            var acknowledgements =
                await host.System.SendAndWaitForAcknowledgementAsync<StopLogicBlockRequest, StopLogicBlockResponse>([actor1, actor2], new StopLogicBlockRequest(), Generous);

            // Assert
            CollectionAssert.AreEquivalent(new[] { actor1, actor2 },
                                           acknowledgements.Keys.ToArray(),
                                           "The wait returns one acknowledgement per actor asked, keyed by the reference the caller passed.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-016.2")]
        public async Task CompleteImmediatelyWithoutActors()
        {
            // Arrange
            await using var host = new PipelineHost();

            // Act
            var acknowledgements = await host.System.SendAndWaitForAcknowledgementAsync<StopLogicBlockRequest, StopLogicBlockResponse>([], new StopLogicBlockRequest(), Short);
            await host.System.StopActorsAndWaitAsync([], Short);

            // Assert
            Assert.IsEmpty(acknowledgements, "A host with no blocks must boot and stop without waiting on anything.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-016.3")]
        public async Task FailAcknowledgementWaitNamingHowManyDidNotAnswer()
        {
            // Arrange
            await using var host = new PipelineHost();
            var answering = host.System.CreateRootActorFromDi<AcknowledgingReceiver>("timeout_answering");
            var silent = host.System.CreateRootActorFromDi<SilentReceiver>("timeout_silent");

            // Act / Assert
            var timeout = await Assert.ThrowsExactlyAsync<TimeoutException>(async () =>
                                                                                await host.System
                                                                                          .SendAndWaitForAcknowledgementAsync<StopLogicBlockRequest, StopLogicBlockResponse>([
                                                                                                  answering, silent,
                                                                                                                                                                             ],
                                                                                              new StopLogicBlockRequest(),
                                                                                              Short));
            StringAssert.Contains(timeout.Message, "1 actor(s)", "The count tells an operator whether one block is stuck or all of them.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-016.4")]
        public async Task RefuseNegativeTimeoutOnAcknowledgementWait()
        {
            // Arrange
            await using var host = new PipelineHost();
            var silent = host.System.CreateRootActorFromDi<SilentReceiver>("negative_ack");

            // Act / Assert
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () =>
                                                                             await host.System.SendAndWaitForAcknowledgementAsync<StopLogicBlockRequest, StopLogicBlockResponse>([
                                                                                     silent,
                                                                                                                                                                                 ],
                                                                                 new StopLogicBlockRequest(),
                                                                                 TimeSpan.FromSeconds(-1)),
                                                                         "A negative timeout arms no clock, so it must be refused rather than waited on forever.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-016.4")]
        public async Task RefuseNegativeTimeoutOnTerminationWait()
        {
            // Arrange
            await using var host = new PipelineHost();
            var silent = host.System.CreateRootActorFromDi<SilentReceiver>("negative_stop");

            // Act / Assert
            await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(async () => await host.System.StopActorsAndWaitAsync([silent], TimeSpan.FromSeconds(-1)),
                                                                         "The termination wait arms its timeout the same way, so it refuses a negative span the same way.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-016.4")]
        public async Task ExpireImmediatelyOnZeroTimeout()
        {
            // Arrange
            await using var host = new PipelineHost();
            var silent1 = host.System.CreateRootActorFromDi<SilentReceiver>("zero_ack");
            var silent2 = host.System.CreateRootActorFromDi<SilentReceiver>("zero_stop");

            // Act / Assert
            await Assert.ThrowsExactlyAsync<TimeoutException>(async () =>
                                                                  await host.System.SendAndWaitForAcknowledgementAsync<StopLogicBlockRequest, StopLogicBlockResponse>([silent1],
                                                                      new StopLogicBlockRequest(),
                                                                      TimeSpan.Zero));
            await Assert.ThrowsExactlyAsync<TimeoutException>(async () => await host.System.StopActorsAndWaitAsync([silent2], TimeSpan.Zero));
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-016.5")]
        public async Task IgnoreRepeatedAcknowledgementFromOneActor()
        {
            // Arrange
            await using var host = new PipelineHost();
            var doubleAnswering = host.System.CreateRootActorFromDi<DoubleAcknowledgingReceiver>("repeat_double");
            var silent = host.System.CreateRootActorFromDi<SilentReceiver>("repeat_silent");

            // Act / Assert
            var timeout =
                await Assert.ThrowsExactlyAsync<TimeoutException>(async () =>
                                                                      await host.System.SendAndWaitForAcknowledgementAsync<StopLogicBlockRequest, StopLogicBlockResponse>([
                                                                              doubleAnswering, silent,
                                                                                                                                                                          ],
                                                                          new StopLogicBlockRequest(),
                                                                          Short),
                                                                  "One actor answering twice must not stand in for an actor that never answered.");
            StringAssert.Contains(timeout.Message, "1 actor(s)", "Exactly the silent actor is still outstanding when the wait expires.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-016.6")]
        public async Task CompleteTerminationWaitForEveryActorAsked()
        {
            // Arrange
            await using var host = new PipelineHost();
            var actor1 = host.System.CreateRootActorFromDi<SilentReceiver>("terminate_1");
            var actor2 = host.System.CreateRootActorFromDi<SilentReceiver>("terminate_2");

            // Act
            await host.System.StopActorsAndWaitAsync([actor1, actor2], Generous);

            // Assert
            Assert.IsEmpty(host.System.FindByName(new Regex("^terminate_")), "Every actor the wait returned for must be gone from the registry.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-016.7")]
        public async Task CompleteTerminationWaitForAbsentActor()
        {
            // Arrange
            await using var host = new PipelineHost();
            var live = host.System.CreateRootActorFromDi<SilentReceiver>("gone_live");
            await host.System.StopActorsAndWaitAsync([live], Generous);

            // Act
            await host.System.StopActorsAndWaitAsync([host.System.LookupByName("gone_never_spawned")], Generous);
            await host.System.StopActorsAndWaitAsync([live], Generous);

            // Assert — reaching here is the behaviour: a registry-driven stop must not hang on a stale name.
            Assert.IsEmpty(host.System.FindByName(new Regex("^gone_")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-017.1")]
        public async Task MintReferenceForNameThatWasNeverSpawned()
        {
            // Arrange
            await using var host = new PipelineHost();

            // Act
            var reference = host.System.LookupByName("never_spawned");

            // Assert
            Assert.IsNotNull(reference, "A reference is minted from a name alone, so a block links to its handler before that handler is known to exist.");
            Assert.IsEmpty(host.System.FindByName(new Regex("^never_spawned$")), "The registry has no such actor — the reference addresses dead letters.");
            await Task.CompletedTask;
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-017.2")]
        public async Task ListActorsMatchingNamePattern()
        {
            // Arrange
            await using var host = new PipelineHost();
            host.System.CreateRootActorFromDi<SilentReceiver>(LogicBlockUtils.CreateLogicBlockName("Meter", "find-1"));
            host.System.CreateRootActorFromDi<SilentReceiver>(LogicBlockUtils.CreateLogicBlockName("Meter", "find-2"));
            host.System.CreateRootActorFromDi<SilentReceiver>("not_a_logic_block");

            // Act
            var found = host.System.FindByName(new Regex("^" + LogicBlockUtils.LogicBlockPrefix));

            // Assert
            Assert.HasCount(2, found, "A host discovers the blocks to stop by scanning the registry for the logic-block prefix.");
            await Task.CompletedTask;
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-015.6")]
        public async Task DeliverNeitherNullMessageNorFrameworkLifecycleMessageToReceiver()
        {
            // Arrange
            await using var host = new PipelineHost();
            var receiver = new RecordingReceiver();
            var actor = host.System.CreateRootActorFor(() => receiver, "system_messages");

            // Act — the spawn and the stop each raise a framework lifecycle message on the same mailbox.
            host.System.SendTo(actor, new StopLogicBlockRequest());
            await receiver.Delivered.WaitAsync(Generous);
            await host.System.StopActorsAndWaitAsync([actor], Generous);

            // Assert
            CollectionAssert.AreEqual(new[] { typeof(StopLogicBlockRequest) },
                                      receiver.Received.Select(message => message.GetType()).ToArray(),
                                      "A receiver sees the messages its own vocabulary defines and none of the framework's own lifecycle traffic.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-017.3")]
        public async Task RunActorWithoutOptionalSeams()
        {
            // Arrange — the actor system alone, so none of the six optional registrations is present.
            var services = new ServiceCollection();
            services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
            services.AddProtoActorSystem();
            services.AddTransient<RecordingReceiver>();
            await using var provider = services.BuildServiceProvider();
            var system = provider.GetRequiredService<IActorSystem>();
            var receiver = new RecordingReceiver();

            // Act
            var actor = system.CreateRootActorFor(() => receiver, "seamless");
            system.SendTo(actor, new StopLogicBlockRequest());
            await receiver.Delivered.WaitAsync(Generous);
            await system.StopActorsAndWaitAsync([actor], Generous);

            // Assert
            Assert.IsNotEmpty(receiver.Received, "Every seam is optional, so a host that registers none of them still spawns, delivers and terminates.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-018.1")]
        public async Task RegisterEverySpawnedActorWithVitalsCore()
        {
            // Arrange
            await using var host = new PipelineHost();
            var blockName = LogicBlockUtils.CreateLogicBlockName("Meter", "vitals-1");
            host.System.CreateRootActorFromDi<RecordingReceiver>(blockName);
            host.System.CreateRootActorFromDi<RecordingReceiver>("runtime_handler");

            // Act
            var snapshot = host.Provider.GetRequiredService<IRuntimeDiagnostics>().Snapshot();

            // Assert
            var block = snapshot.Single(vitals => vitals.ActorName == blockName);
            var runtime = snapshot.Single(vitals => vitals.ActorName == "runtime_handler");
            Assert.AreEqual(ActorCategory.LogicBlock, block.Identity!.Category, "An actor whose name carries the logic-block prefix is a logic block.");
            Assert.AreEqual(ActorCategory.Runtime, runtime.Identity!.Category, "Every other actor belongs to the runtime.");
            await Task.CompletedTask;
        }

        private sealed class DisposalTracker
        {
            public int DisposeCount;
        }

        /// <summary>A transient dependency the per-actor scope owns — the stand-in for a per-block protocol client.</summary>
        private sealed class ScopedResource : IDisposable
        {
            private readonly SemaphoreSlim _disposals;

            private readonly DisposalTracker _tracker;

            public ScopedResource(DisposalTracker tracker, SemaphoreSlim disposals)
            {
                _tracker = tracker;
                _disposals = disposals;
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _tracker.DisposeCount);
                _disposals.Release();
            }
        }

        private sealed class DependentReceiver : IActorReceiver
        {
            public DependentReceiver(ScopedResource resource)
            {
                _ = resource;
            }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                return Task.CompletedTask;
            }
        }

        private sealed class SingletonRegisteredReceiver : IActorReceiver
        {
            public static int Constructions;

            public SingletonRegisteredReceiver()
            {
                Interlocked.Increment(ref Constructions);
            }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                return Task.CompletedTask;
            }
        }

        private sealed class SelfDisposingReceiver : IActorReceiver, IDisposable
        {
            public static int Disposals;

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                return Task.CompletedTask;
            }

            public void Dispose()
            {
                Interlocked.Increment(ref Disposals);
            }
        }

        /// <summary>
        ///     One composed host per test — the two registrations a consumer composes, a silent logger, and the
        ///     receivers this suite spawns. A fresh provider per test keeps actor names from colliding across
        ///     tests that reuse one.
        /// </summary>
        private sealed class PipelineHost : IAsyncDisposable
        {
            public ServiceProvider Provider { get; }

            public IActorSystem System { get; }

            public DisposalTracker Tracker { get; } = new();

            public SemaphoreSlim Disposals { get; } = new(0);

            public PipelineHost()
            {
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
                services.AddDaleSdk();
                services.AddProtoActorSystem();
                services.AddSingleton(Tracker);
                services.AddSingleton(Disposals);
                services.AddTransient<ScopedResource>();
                services.AddTransient<DependentReceiver>();
                services.AddSingleton<SingletonRegisteredReceiver>();
                services.AddTransient<SelfDisposingReceiver>();
                services.AddTransient<SilentReceiver>();
                services.AddTransient<AcknowledgingReceiver>();
                services.AddTransient<DoubleAcknowledgingReceiver>();
                services.AddTransient<RecordingReceiver>();
                Provider = services.BuildServiceProvider();
                System = Provider.GetRequiredService<IActorSystem>();
            }

            public ValueTask DisposeAsync()
            {
                return Provider.DisposeAsync();
            }
        }
    }
}