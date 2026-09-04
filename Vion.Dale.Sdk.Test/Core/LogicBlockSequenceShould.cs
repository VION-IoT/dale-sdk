using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Persistence;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     The message sequence a logic block's actor receives, in the order the runtime sends it: the
    ///     runtime-actor link, the configuration, the linked-interface map, the restore, the start, the
    ///     writes and the republish while it runs, the stop, and the snapshot. Every test drives the block
    ///     through <c>HandleMessageAsync</c> against a recording context (<see cref="LifecycleHarness" />)
    ///     and reads the answers, publications and self-sends back — the block's whole observable surface
    ///     inside its actor.
    ///     <para>
    ///         The dispatcher, the timers and the vitals have suites of their own; what a gate, an emission
    ///         policy or a host does around this sequence belongs to their pages and is cited, not repeated.
    ///     </para>
    /// </summary>
    [TestClass]
    public sealed class LogicBlockSequenceShould
    {
        private readonly LifecycleHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-001.1")]
        public void IgnoreMessageItDoesNotHandle()
        {
            // Arrange
            var block = new BareBlock();
            _harness.Configure(block);

            // Act
            _harness.Send(block, "a message no arm handles");

            // Assert
            Assert.IsEmpty(_harness.Responses, "An unknown message is answered with silence rather than a fault, so a newer host can send one safely.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-001.2")]
        public void AnnounceBoundServicesToBothHandlers()
        {
            // Arrange
            var block = new ServiceBearingBlock();

            // Act
            _harness.Configure(block, ["Service"]);

            // Assert
            var recipients = _harness.Context.Sent.Where(sent => sent.Message is BindLogicBlockServices).Select(sent => sent.Target).ToList();
            CollectionAssert.AreEquivalent(new[] { "ServicePropertyHandler", "ServiceMeasuringPointHandler" },
                                           recipients,
                                           "One announcement reaches both handlers; each takes the half it needs.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-001.3")]
        public void KeepMessagesBlockSendsItselfOffPublishedVocabulary()
        {
            // Arrange — every public message type the SDK carries, read off the assembly rather than listed.
            var published = typeof(StartLogicBlockRequest).Assembly
                                                          .GetTypes()
                                                          .Where(type => type.IsPublic && type.Namespace == typeof(StartLogicBlockRequest).Namespace)
                                                          .Select(type => type.Name)
                                                          .ToList();

            // Act
            var block = new BareBlock();
            _harness.ConfigureAndStart(block);
            var selfSent = _harness.Context.Scheduled.Select(entry => entry.Message.GetType()).Distinct().ToList();

            // Assert
            Assert.IsNotEmpty(selfSent, "The block schedules something for itself, so there is something to check.");
            foreach (var type in selfSent)
            {
                Assert.IsFalse(type.IsPublic, $"{type.Name} is a message the block sends itself, so no host can construct one.");
                CollectionAssert.DoesNotContain(published, type.Name, $"{type.Name} must not appear in the published message vocabulary either.");
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-003.4")]
        public void DropEveryPublicationOfServiceWithNoIdentifierInConfiguration()
        {
            // Arrange — the block binds its service, and the configuration names none.
            var block = new ServiceBearingBlock();

            // Act
            _harness.ConfigureAndStart(block);

            // Assert
            var announcement = _harness.Published.OfType<BindLogicBlockServices>().First();
            Assert.IsEmpty(announcement.Properties, "A service with no identifier is omitted from the announcement, because there is no identifier to announce it under.");
            Assert.IsEmpty(_harness.Published.OfType<ServicePropertyValueChanged>(),
                           "And every publication of it is dropped for the instance's life — the initial publish at start included.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-001.4")]
        [TestProperty("spec", "AC-GATE-008.3")]
        public void DropContractMessageForContractThatWasNotBound()
        {
            // Arrange
            var block = new BareBlock();
            _harness.ConfigureAndStart(block);
            _harness.Context.Responses.Clear();
            _harness.Context.Sent.Clear();

            // Act
            _harness.Send(block, new ContractMessage<int>(new LogicBlockContractId("block-1", "NotBound"), 1));
            _harness.Send(block, new StopLogicBlockRequest());

            // Assert
            Assert.IsEmpty(_harness.Published, "A message to a contract the configuration left out reaches nobody.");
            Assert.IsNotEmpty(_harness.Responses.OfType<StopLogicBlockResponse>(),
                              "And the block is still handling messages afterwards, rather than having failed its handler on a missing key.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-002.1")]
        public void ConfigureBeforeAnnouncingAndBeforeReady()
        {
            // Arrange
            var block = new ServiceBearingBlock();

            // Act
            _harness.Configure(block, ["Service"]);

            // Assert — the announcement carries what the binders produced, so it can only follow them.
            var announcement = _harness.Published.OfType<BindLogicBlockServices>().First();
            Assert.IsNotEmpty(announcement.Properties, "The service announcement is built from the binders' output, so the phase runs before it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-002.2")]
        public void CarryIdentifierFromConfigurationMessage()
        {
            // Arrange
            var block = new ServiceBearingBlock();

            // Act
            _harness.Send(block, LifecycleHarness.RuntimeActors());
            _harness.Send(block, LifecycleHarness.Configuration("Meter", "meter-7", ["Service"]));

            // Assert
            var announcement = _harness.Published.OfType<BindLogicBlockServices>().First();
            Assert.AreEqual(new LogicBlockId("meter-7"), announcement.LogicBlockId, "The block announces itself under the identifier its configuration gave it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-002.3")]
        public void ConfigureWithoutVitalsCollectorOrClock()
        {
            // Arrange
            var block = new ServiceBearingBlock();

            // Act
            _harness.ConfigureAndStart(block, ["Service"]);

            // Assert
            Assert.IsNotEmpty(_harness.Responses.OfType<StartLogicBlockResponse>(),
                              "A bare host registers neither a vitals collector nor a clock, and a block configured against it still runs.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-002.4")]
        public void NameEarlierFailureOnSecondConfiguration()
        {
            // Arrange
            var block = new FailingConfigurationBlock();
            _harness.Send(block, LifecycleHarness.RuntimeActors());
            Assert.Throws<Exception>(() => _harness.Send(block, LifecycleHarness.Configuration()), "Pre-condition: the declarative binder must fail so the instance is spent.");

            // Act / Assert
            var refusal = Assert.ThrowsExactly<InvalidOperationException>(() => _harness.Send(block, LifecycleHarness.Configuration()));
            StringAssert.Contains(refusal.Message,
                                  "its configuration failed",
                                  "A refused retry names the original failure rather than pointing back at the configuration that just failed.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-003.1")]
        [TestProperty("spec", "AC-LIFE-003.2")]
        public void CompleteConfigurationOnceWhicheverArrivesSecond()
        {
            // Arrange
            var linkFirst = new BareBlock();
            var configurationFirst = new BareBlock();
            var linkFirstHarness = new LifecycleHarness();
            var configurationFirstHarness = new LifecycleHarness();

            // Act
            linkFirstHarness.Send(linkFirst, LifecycleHarness.RuntimeActors());
            linkFirstHarness.Send(linkFirst, LifecycleHarness.Configuration());

            configurationFirstHarness.Send(configurationFirst, LifecycleHarness.Configuration());
            configurationFirstHarness.Send(configurationFirst, LifecycleHarness.RuntimeActors());
            configurationFirstHarness.Send(configurationFirst, LifecycleHarness.RuntimeActors());

            // Assert
            Assert.AreEqual(1, linkFirst.ReadyCount, "The configuration completes inline when the runtime actors are already linked.");
            Assert.AreEqual(1, configurationFirst.ReadyCount, "It completes on the link when the configuration came first — once, however many links follow.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-003.3")]
        public void AnnounceBeforePublishingAnyValue()
        {
            // Arrange
            var block = new ServiceBearingBlock();

            // Act
            _harness.ConfigureAndStart(block, ["Service"]);

            // Assert
            var messages = _harness.Published;
            var announcement = messages.ToList().FindIndex(message => message is BindLogicBlockServices);
            var firstPublication = messages.ToList().FindIndex(message => message is ServicePropertyValueChanged);
            Assert.IsGreaterThanOrEqualTo(0, announcement, "The announcement is sent.");
            Assert.IsGreaterThan(announcement, firstPublication, "A handler dispatches a value's codec from the announcement, so a value published before it would be lost.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-003.5")]
        public void ApplyLinkedInterfaceMapThatArrivedBeforeConfiguration()
        {
            // Arrange — an interface-bearing block, its map sent before the configuration message.
            var block = new InterfaceBearingBlock();
            var map = new Dictionary<InterfaceId, Dictionary<InterfaceId, Vion.Dale.Sdk.Abstractions.IActorReference>>
                      {
                          [new InterfaceId("block-1",
                                           InterfaceBearingBlock
                                               .InterfaceIdentifier)] =
                              new()
                              {
                                  [new InterfaceId("peer", "Peer")] =
                                      new LifecycleHarness.NamedReference("peer"),
                              },
                      };

            // Act
            _harness.Send(block, new SetLinkedInterfaces(map));
            _harness.Send(block, LifecycleHarness.RuntimeActors());
            _harness.Send(block, LifecycleHarness.Configuration());

            // Assert
            Assert.IsNotEmpty(block.GetLinkedLifecyclePeers(),
                              "A map that arrived before the binders ran is held and applied at the configuration's tail, not warned away and lost.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-004.1")]
        public void AcknowledgeRestoreInEveryBlockState()
        {
            // Arrange
            var configured = new ServiceBearingBlock();
            _harness.Configure(configured, ["Service"]);
            var neverConfigured = new BareBlock();
            var secondHarness = new LifecycleHarness();

            // Act
            _harness.Send(configured, new RestorePersistentDataRequest([]));
            secondHarness.Send(neverConfigured, new RestorePersistentDataRequest([]));

            // Assert
            Assert.IsNotEmpty(_harness.Responses.OfType<RestorePersistentDataResponse>(), "A configured block acknowledges its restore.");
            Assert.IsNotEmpty(secondHarness.Responses.OfType<RestorePersistentDataResponse>(),
                              "So does one whose configuration never ran — the runtime waits on this answer and a silent block hangs its own reclamation.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-004.2")]
        [TestProperty("spec", "AC-LIFE-004.4")]
        public void ApplyEveryUsableEntryWhenOneCannotBeConverted()
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.Configure(block, ["Service"]);

            // Act
            _harness.Send(block,
                          new RestorePersistentDataRequest([
                              PersistedEntry.Of("Service.Power", "System.Double", JsonDocument.Parse("\"not a number\"").RootElement),
                              PersistedEntry.Of("Service.Count", "System.Int32", JsonDocument.Parse("7").RootElement),
                          ]));

            // Assert
            Assert.AreEqual(0.0, block.Service.Power, "The entry that could not be converted leaves its member as it was.");
            Assert.AreEqual(7, block.Service.Count, "The entry beside it is applied all the same.");
            Assert.IsNotEmpty(_harness.Responses.OfType<RestorePersistentDataResponse>(), "The restore completes.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-004.3")]
        [DataRow("Service.Power", "System.Double", "12.5", 12.5d, DisplayName = "a number onto a double")]
        [DataRow("Service.Count", "System.Int32", "3", 3, DisplayName = "a number onto an integer")]
        [DataRow("Service.Optional", "System.Nullable`1[[System.Double]]", "4.5", 4.5d, DisplayName = "a number onto a nullable double")]
        public void ConvertPersistedValueIntoMembersOwnType(string key, string typeFullName, string rawJson, object expected)
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.Configure(block, ["Service"]);

            // Act
            _harness.Send(block, new RestorePersistentDataRequest([PersistedEntry.Of(key, typeFullName, JsonDocument.Parse(rawJson).RootElement)]));

            // Assert
            var restored = key switch
            {
                "Service.Power" => (object)block.Service.Power,
                "Service.Count" => block.Service.Count,
                _ => block.Service.Optional!,
            };
            Assert.AreEqual(expected, restored, "The store cannot resolve most type names, so the member's declared type is what reads the value back.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-005.1")]
        public void StartByRunningHookThenPublishingThenArmingSaveThenAcknowledging()
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.Configure(block, ["Service"]);

            // Act
            _harness.Send(block, new StartLogicBlockRequest());

            // Assert
            Assert.IsNotEmpty(_harness.Published.OfType<ServicePropertyValueChanged>(), "Every bound member's value is published once the block is started.");
            Assert.IsNotEmpty(_harness.ScheduledOfKind("PeriodicPersistentDataSaveMessage"), "The periodic save is armed by the start, not by the configuration.");
            Assert.IsNotEmpty(_harness.Responses.OfType<StartLogicBlockResponse>(), "The start is acknowledged last.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-005.2")]
        public void LeaveBlockUnstartedAndUnacknowledgedWhenStartHookThrows()
        {
            // Arrange
            var block = new ThrowingHookBlock(ThrowingHookBlock.Hook.Starting);
            _harness.Configure(block);

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _harness.Send(block, new StartLogicBlockRequest()));
            Assert.IsEmpty(_harness.Responses.OfType<StartLogicBlockResponse>(), "A block that never started never acknowledges, which is how a host's start fails and names it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-005.3")]
        public void AcknowledgeSecondStartAndChangeNothing()
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);
            var savesAfterFirstStart = _harness.ScheduledOfKind("PeriodicPersistentDataSaveMessage").Count;

            // Act
            _harness.Send(block, new StartLogicBlockRequest());

            // Assert
            Assert.HasCount(savesAfterFirstStart,
                            _harness.ScheduledOfKind("PeriodicPersistentDataSaveMessage"),
                            "A second start must arm no second save chain: only a stop retires one, so they would accumulate for the life of the process.");
            Assert.HasCount(2, _harness.Responses.OfType<StartLogicBlockResponse>().ToList(), "It is acknowledged all the same — the runtime waits on the answer.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-005.4")]
        public void StartBlockAgainAfterItStopped()
        {
            // Arrange
            var block = new BareBlock();
            _harness.ConfigureAndStart(block);
            _harness.Send(block, new StopLogicBlockRequest());

            // Act
            _harness.Send(block, new StartLogicBlockRequest());

            // Assert
            Assert.AreEqual(2, block.StartingCount, "A restart runs the start hook again; the guard on a second start is about a block that is already running.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-009.1")]
        public void ApplyWriteAndAnswerWithValueAfterIt()
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.Configure(block, ["Service"]);

            // Act
            _harness.Send(block, new SetServicePropertyValueRequest(new ServiceIdentifier("Service"), nameof(Meter.Power), 42.0));

            // Assert
            var answer = _harness.Responses.OfType<SetServicePropertyValueResponse>().Single();
            Assert.AreEqual(42.0, block.Service.Power, "The write lands on the member whether or not the block is started.");
            Assert.AreEqual(42.0, answer.Value, "The answer carries the value the member holds after the write.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-009.2")]
        public void SendNoAnswerWhenWriteNamesServiceBlockDoesNotCarry()
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.Configure(block, ["Service"]);

            // Act
            _harness.Send(block, new SetServicePropertyValueRequest(new ServiceIdentifier("NoSuchService"), nameof(Meter.Power), 42.0));

            // Assert
            Assert.IsEmpty(_harness.Responses.OfType<SetServicePropertyValueResponse>(),
                           "There is no value to answer with, and the runtime publishes every answer it gets — so answering with nothing would report the member set to nothing.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-009.3")]
        public void PublishNothingWhenValueChangesBeforeStart()
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.Configure(block, ["Service"]);
            var publishedByConfiguration = _harness.Published.OfType<ServicePropertyValueChanged>().Count();

            // Act
            _harness.Send(block, new SetServicePropertyValueRequest(new ServiceIdentifier("Service"), nameof(Meter.Power), 5.0));

            // Assert
            Assert.AreEqual(publishedByConfiguration,
                            _harness.Published.OfType<ServicePropertyValueChanged>().Count(),
                            "A write before the start reaches the member and nothing else; the initial publish at start is what carries it outward.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-010.1")]
        [TestProperty("spec", "AC-LIFE-010.3")]
        public void StopByRunningHookWhileStartedThenClearingThenAcknowledging()
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);
            _harness.Context.Sent.Clear();
            _harness.Context.Responses.Clear();

            // Act
            _harness.Send(block, new StopLogicBlockRequest());

            // Assert
            var published = _harness.Published.ToList();
            var lastClear = published.FindLastIndex(message => message is ServicePropertyValueCleared);
            Assert.AreEqual(1, block.StoppingCount, "The stop hook runs once.");
            Assert.IsGreaterThanOrEqualTo(0, lastClear, "The retained publications are cleared.");
            Assert.IsNotEmpty(_harness.Responses.OfType<StopLogicBlockResponse>(), "The stop is acknowledged.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-010.2")]
        public void TakeSnapshotBeforeStopHookRuns()
        {
            // Arrange
            var block = new WritingOnStopBlock();
            _harness.ConfigureAndStart(block, ["Service"]);

            // Act
            _harness.Send(block, new StopLogicBlockRequest());
            _harness.Context.Responses.Clear();
            _harness.Send(block, new GetPersistentDataSnapshotRequest());

            // Assert
            var snapshot = _harness.Responses.OfType<GetPersistentDataSnapshotResponse>().Single();
            var power = snapshot.PersistentDataValues.Single(entry => entry.Key == "Service." + nameof(Meter.Power));
            Assert.AreEqual(0.0, power.Value, "The snapshot is taken before the hook, so a value the hook writes does not survive a restart.");
            Assert.AreEqual(WritingOnStopBlock.WrittenOnStop, block.Service.Power, "The hook did write it — the member holds it, the snapshot does not.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-010.4")]
        public void RunStopHookAndAcknowledgeForBlockThatNeverStarted()
        {
            // Arrange
            var neverStarted = new BareBlock();
            _harness.Configure(neverStarted);
            var neverConfigured = new FailingConfigurationBlock();
            var secondHarness = new LifecycleHarness();
            secondHarness.Send(neverConfigured, LifecycleHarness.RuntimeActors());
            Assert.Throws<Exception>(() => secondHarness.Send(neverConfigured, LifecycleHarness.Configuration()),
                                     "Pre-condition: the configuration must fail so the instance is spent.");

            // Act
            _harness.Send(neverStarted, new StopLogicBlockRequest());
            secondHarness.Send(neverConfigured, new StopLogicBlockRequest());

            // Assert
            Assert.AreEqual(1,
                            neverStarted.StoppingCount,
                            "A block that never started still gets its stop hook — it is the only place it can release what the ready hook acquired.");
            Assert.AreEqual(1, neverConfigured.StoppingCount, "So does one whose configuration failed.");
            Assert.IsNotEmpty(secondHarness.Responses.OfType<StopLogicBlockResponse>(), "Both acknowledge.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-010.5")]
        public void FinishStopAndAcknowledgeThenReportWhenStopHookThrows()
        {
            // Arrange
            var block = new ThrowingHookBlock(ThrowingHookBlock.Hook.Stopping);
            _harness.ConfigureAndStart(block);

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => _harness.Send(block, new StopLogicBlockRequest()),
                                                            "The failure is reported rather than swallowed, so a host's health surface can name the block.");
            Assert.IsNotEmpty(_harness.Responses.OfType<StopLogicBlockResponse>(), "It is reported after the acknowledgement, so the shutdown is not held up by it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-010.6")]
        public void AcknowledgeSecondStopAndChangeNothing()
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);
            _harness.Send(block, new StopLogicBlockRequest());

            // Act
            _harness.Send(block, new StopLogicBlockRequest());

            // Assert
            Assert.AreEqual(1, block.StoppingCount, "A stop hook that disposed a client must not be asked to dispose it twice.");
            Assert.HasCount(2, _harness.Responses.OfType<StopLogicBlockResponse>().ToList(), "Both stops are acknowledged.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-011.1")]
        public void AnswerSnapshotRequestWithCopyOfSnapshotStopTook()
        {
            // Arrange
            var block = new ServiceBearingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);
            block.Service.Count = 5;
            _harness.Send(block, new StopLogicBlockRequest());

            // Act
            _harness.Send(block, new GetPersistentDataSnapshotRequest());
            _harness.Send(block, new GetPersistentDataSnapshotRequest());

            // Assert
            var snapshots = _harness.Responses.OfType<GetPersistentDataSnapshotResponse>().ToList();
            Assert.AreEqual(new LogicBlockId("block-1"), snapshots[0].LogicBlockId, "The answer names the block it came from.");
            Assert.AreNotSame(snapshots[0].PersistentDataValues, snapshots[1].PersistentDataValues, "Each answer is a copy, so a later capture cannot mutate what a caller holds.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-011.2")]
        public void AnswerSnapshotRequestWithNothingWhenPersistenceNeverInitialised()
        {
            // Arrange
            var block = new FailingConfigurationBlock();
            _harness.Send(block, LifecycleHarness.RuntimeActors());
            Assert.Throws<Exception>(() => _harness.Send(block, LifecycleHarness.Configuration()), "Pre-condition: the configuration must fail before persistence is initialised.");
            _harness.Context.Responses.Clear();

            // Act
            _harness.Send(block, new GetPersistentDataSnapshotRequest());

            // Assert
            var snapshot = _harness.Responses.OfType<GetPersistentDataSnapshotResponse>().Single();
            Assert.IsEmpty(snapshot.PersistentDataValues, "The runtime waits on this answer during teardown, so it is always given — empty where there is nothing to give.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-012.1")]
        public void WithholdPersistedValuesAndLinksAndOutboundPathFromReadyHook()
        {
            // Arrange
            var block = new ObservingReadyBlock();

            // Act
            _harness.Configure(block, ["Service"]);

            // Assert
            Assert.AreEqual(0.0, block.PowerSeenInReady, "A member read in the ready hook holds its declared default: the restore is a later message.");
            Assert.IsEmpty(_harness.Published.OfType<ServicePropertyValueChanged>(),
                           "The hook wrote a member, and nothing was published: the block is not started yet, so the write reaches the member and no further.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-012.2")]
        public void MakeRestoredValuesReadableInStartHook()
        {
            // Arrange
            var block = new ObservingStartBlock();
            _harness.Configure(block, ["Service"]);
            _harness.Send(block, new RestorePersistentDataRequest([PersistedEntry.Of("Service." + nameof(Meter.Power), "System.Double", 11.0)]));

            // Act
            _harness.Send(block, new StartLogicBlockRequest());

            // Assert
            Assert.AreEqual(11.0, block.PowerSeenInStarting, "The start hook runs after the restore, so a member read there holds the value the host restored.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-012.3")]
        public void KeepBlockStartedForWholeOfStopHook()
        {
            // Arrange
            var block = new WritingOnStopBlock();
            _harness.ConfigureAndStart(block, ["Service"]);
            _harness.Context.Sent.Clear();

            // Act
            _harness.Send(block, new StopLogicBlockRequest());

            // Assert
            Assert.IsNotEmpty(_harness.Published.OfType<ServicePropertyValueChanged>(),
                              "The block is still started inside its stop hook, so the final value the hook writes is published rather than dropped.");
        }

        /// <summary>A block whose stop hook writes a member, so the snapshot's ordering is observable.</summary>
        private sealed class WritingOnStopBlock : LogicBlockBase
        {
            public const double WrittenOnStop = 99.0;

            public Meter Service { get; } = new();

            public WritingOnStopBlock() : base(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            protected override void Stopping()
            {
                Service.Power = WrittenOnStop;
            }
        }

        /// <summary>A block that records what its ready hook could see.</summary>
        private sealed class ObservingReadyBlock : LogicBlockBase
        {
            public Meter Service { get; } = new();

            public double PowerSeenInReady { get; private set; }

            public ObservingReadyBlock() : base(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
                PowerSeenInReady = Service.Power;
                Service.Count = 1;
            }
        }

        /// <summary>A block that records what its start hook could see.</summary>
        private sealed class ObservingStartBlock : LogicBlockBase
        {
            public Meter Service { get; } = new();

            public double PowerSeenInStarting { get; private set; }

            public ObservingStartBlock() : base(Microsoft.Extensions.Logging.Abstractions.NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }

            protected override void Starting()
            {
                PowerSeenInStarting = Service.Power;
            }
        }
    }
}