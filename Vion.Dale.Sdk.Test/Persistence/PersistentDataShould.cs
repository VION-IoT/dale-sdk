using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Persistence;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Persistence
{
    /// <summary>
    ///     What a block persists, under what keys, and what it does with a file written by an earlier release.
    ///     Every test drives the block through the messages that carry persistence — the configuration, the
    ///     restore, the stop and the snapshot request — so what is asserted is what the runtime's store would
    ///     actually be handed.
    ///     <para>
    ///         The store hands a persisted value back in its serialised form: it resolves a type from a name
    ///         and most compound names do not resolve, so the member's declared type is the only thing that
    ///         can read the value back. That is what the conversion rows below exercise.
    ///     </para>
    /// </summary>
    [TestClass]
    public sealed class PersistentDataShould
    {
        public enum SampleMode
        {
            Coarse,

            Fine,
        }

        private readonly LifecycleHarness _harness = new();

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-008.1")]
        [TestProperty("spec", "AC-LIFE-008.4")]
        public void PersistWritableServicePropertyAndOptInPropertyAndNestedOne()
        {
            // Arrange
            var block = new PersistingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);

            // Act
            var keys = SnapshotKeys(block);

            // Assert
            CollectionAssert.Contains(keys, "Service." + nameof(Meter.Power), "A writable service property is persisted unless its author excluded it.");
            CollectionAssert.Contains(keys, "_direct." + nameof(PersistingBlock.Retained), "So is a property the author marked persistent.");
            CollectionAssert.Contains(keys,
                                      "_direct." + nameof(PersistingBlock.Inner) + "." + nameof(InnerState.Deep),
                                      "So is a persistent property one level inside a class-typed property.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-008.1")]
        public void PersistNothingForExcludedServiceProperty()
        {
            // Arrange
            var block = new PersistingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);

            // Act
            var keys = SnapshotKeys(block);

            // Assert
            CollectionAssert.DoesNotContain(keys, "Service." + nameof(Meter.Excluded), "Opting a writable service property out is the deliberate act; persistence is the default.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-008.2")]
        public void PersistPropertyDeclaredPrivateOnBaseClass()
        {
            // Arrange
            var block = new DerivedPersistingBlock();
            _harness.ConfigureAndStart(block);

            // Act
            var keys = SnapshotKeys(block);

            // Assert
            CollectionAssert.Contains(keys,
                                      "_direct.PrivateOnBase",
                                      "A base class of blocks that keeps private state used to lose it on every restart, with no diagnostic anywhere.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-008.3")]
        public void PersistNothingForPropertyWithNoSetter()
        {
            // Arrange
            var block = new PersistingBlock();
            _harness.ConfigureAndStart(block);

            // Act
            var keys = SnapshotKeys(block);

            // Assert
            CollectionAssert.DoesNotContain(keys, "_direct." + nameof(PersistingBlock.ReadOnlyRetained), "There is nowhere to restore it to, so there is no point capturing it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-008.5")]
        public void ReplaceWholeSnapshotOnEverySave()
        {
            // Arrange
            var block = new PersistingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);
            block.Service.Power = 3.0;
            _harness.Send(block, new StopLogicBlockRequest());
            var afterFirstStop = SnapshotEntries(block).Count;

            // Act
            _harness.Send(block, new StartLogicBlockRequest());
            _harness.Send(block, new StopLogicBlockRequest());

            // Assert
            Assert.HasCount(afterFirstStop, SnapshotEntries(block), "The snapshot is replaced, not appended to, so a member a reconfiguration removed does not survive in it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-008.5")]
        public void CaptureRemainingMembersWhenOneCannotBeRead()
        {
            // Arrange
            var block = new UnreadableMemberBlock();
            _harness.ConfigureAndStart(block, ["Service"]);

            // Act
            var keys = SnapshotKeys(block);

            // Assert
            CollectionAssert.Contains(keys, "Service." + nameof(Meter.Power), "One member whose read throws must not cost the block every other member's persisted state.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-008.6")]
        public void CarryEntryAsKeyAndDeclaredTypeNameAndValue()
        {
            // Arrange
            var block = new PersistingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);
            block.Service.Power = 7.5;

            // Act
            var entry = SnapshotEntries(block).Single(candidate => candidate.Key == "Service." + nameof(Meter.Power));

            // Assert
            Assert.AreEqual(typeof(double).FullName, entry.TypeFullName, "The store writes the declared type's name beside the value and reads it back by it.");
            Assert.AreEqual(7.5, entry.Value, "And the value the member held when the snapshot was taken.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-008.7")]
        public void SaveOnceEveryMinuteWhileStarted()
        {
            // Arrange
            var block = new PersistingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);
            var due = _harness.Context.Scheduled.Single(entry => entry.Message.GetType().Name == "PeriodicPersistentDataSaveMessage");

            // Act
            _harness.Send(block, due.Message);

            // Assert
            Assert.AreEqual(TimeSpan.FromMinutes(1), due.Delay, "The cadence is what stands between a power cut and a gateway's last minute of state.");
            Assert.IsNotEmpty(_harness.Published.OfType<PersistentDataSnapshotChanged>(), "A save that runs hands the snapshot to the persistence manager.");
            Assert.HasCount(2,
                            _harness.Context.Scheduled.Where(entry => entry.Message.GetType().Name == "PeriodicPersistentDataSaveMessage"),
                            "And arms the next one, so the chain continues.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-008.7")]
        public void ArmNoFurtherSaveAfterStop()
        {
            // Arrange
            var block = new PersistingBlock();
            _harness.ConfigureAndStart(block, ["Service"]);
            var due = _harness.Context.Scheduled.Single(entry => entry.Message.GetType().Name == "PeriodicPersistentDataSaveMessage").Message;
            _harness.Send(block, new StopLogicBlockRequest());
            _harness.Context.Sent.Clear();

            // Act
            _harness.Send(block, due);

            // Assert
            Assert.IsEmpty(_harness.Published.OfType<PersistentDataSnapshotChanged>(), "A stopped block writes no snapshot.");
            Assert.HasCount(1,
                            _harness.Context.Scheduled.Where(entry => entry.Message.GetType().Name == "PeriodicPersistentDataSaveMessage"),
                            "And arms no further save — this is what retires the chain a stop leaves behind.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-004.3")]
        [DynamicData(nameof(SerialisedValues))]
        public void RestoreSerialisedValueIntoMembersOwnType(string key, string typeFullName, object serialised, object expected, string shape)
        {
            // Arrange
            var block = new RichMemberBlock();
            _harness.Configure(block, ["Service"]);

            // Act
            _harness.Send(block, new RestorePersistentDataRequest([new PersistentDataEntry(key, typeFullName, serialised)]));

            // Assert
            Assert.AreEqual(expected, block.Read(key), shape);
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-004.3")]
        public void RestoreImmutableArrayFromSerialisedValue()
        {
            // Arrange
            var block = new RichMemberBlock();
            _harness.Configure(block, ["Service"]);

            // Act
            _harness.Send(block,
                          new RestorePersistentDataRequest([
                              new PersistentDataEntry("_direct." + nameof(RichMemberBlock.Samples),
                                                      typeof(ImmutableArray<double>).FullName!,
                                                      JsonDocument.Parse("[1.5,2.5]").RootElement),
                          ]));

            // Assert
            CollectionAssert.AreEqual(new[] { 1.5, 2.5 },
                                      block.Samples.ToArray(),
                                      "The store cannot resolve a compound type's name, so a value like this arrived unconverted and threw inside the setter.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-004.3")]
        public void RestoreAlreadyTypedValueAsItIs()
        {
            // Arrange
            var block = new RichMemberBlock();
            _harness.Configure(block, ["Service"]);

            // Act
            _harness.Send(block, new RestorePersistentDataRequest([new PersistentDataEntry("_direct." + nameof(RichMemberBlock.Level), typeof(double).FullName!, 8.25)]));

            // Assert
            Assert.AreEqual(8.25, block.Level, "A value the store did manage to convert is taken as it is.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-004.4")]
        public void LeaveMemberAndCompleteRestoreWhenValueCannotBeConverted()
        {
            // Arrange
            var block = new RichMemberBlock();
            _harness.Configure(block, ["Service"]);
            block.SetLevel(4.0);

            // Act
            _harness.Send(block,
                          new RestorePersistentDataRequest([
                              new PersistentDataEntry("_direct." + nameof(RichMemberBlock.Level), typeof(double).FullName!, JsonDocument.Parse("\"not a number\"").RootElement),
                          ]));

            // Assert
            Assert.AreEqual(4.0, block.Level, "A schema change between releases must not stop a gateway from booting.");
            Assert.IsNotEmpty(_harness.Responses.OfType<RestorePersistentDataResponse>(), "The restore completes all the same.");
        }

        public static IEnumerable<object[]> SerialisedValues()
        {
            yield return
            [
                "_direct." + nameof(RichMemberBlock.Level), typeof(double).FullName!, JsonDocument.Parse("12.5").RootElement, 12.5d, "a number onto a double",
            ];
            yield return
            [
                "_direct." + nameof(RichMemberBlock.Optional), typeof(double?).FullName!, JsonDocument.Parse("4.5").RootElement, 4.5d, "a number onto a nullable double",
            ];
            yield return
            [
                "_direct." + nameof(RichMemberBlock.Mode), typeof(SampleMode).FullName!, JsonDocument.Parse("\"Fine\"").RootElement, SampleMode.Fine,
                "an enum member named as a string",
            ];
        }

        private List<PersistentDataEntry> SnapshotEntries(LogicBlockBase block)
        {
            _harness.Send(block, new StopLogicBlockRequest());
            _harness.Context.Responses.Clear();
            _harness.Send(block, new GetPersistentDataSnapshotRequest());
            return _harness.Responses.OfType<GetPersistentDataSnapshotResponse>().Last().PersistentDataValues;
        }

        private List<string> SnapshotKeys(LogicBlockBase block)
        {
            return SnapshotEntries(block).Select(entry => entry.Key).ToList();
        }

        /// <summary>The nested component a persistent property lives one level inside.</summary>
        public sealed class InnerState
        {
            [Persistent]
            public int Deep { get; set; }
        }

        /// <summary>A block carrying one of each thing persistence discovers.</summary>
        public sealed class PersistingBlock : LogicBlockBase
        {
            public Meter Service { get; } = new();

            [Persistent]
            public int Retained { get; set; }

            // Deliberately the shape DALE007 reports, so the runtime's own guard is provable for a block that
            // shipped past a suppressed diagnostic.
#pragma warning disable DALE007
            [Persistent]
            public int ReadOnlyRetained
            {
                get => 0;
            }
#pragma warning restore DALE007

            public InnerState Inner { get; } = new();

            public PersistingBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block whose base class keeps private persistent state.</summary>
        public class BasePersistingBlock : LogicBlockBase
        {
            [Persistent]
            private int PrivateOnBase { get; set; }

            public BasePersistingBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        public sealed class DerivedPersistingBlock : BasePersistingBlock
        {
        }

        /// <summary>A block one of whose members cannot be read when the snapshot is taken.</summary>
        public sealed class UnreadableMemberBlock : LogicBlockBase
        {
            public Meter Service { get; } = new();

            [Persistent]
            public int Unreadable
            {
                get => throw new InvalidOperationException("this member cannot be read");

                set => _ = value;
            }

            public UnreadableMemberBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block whose members are the shapes a store hands back unconverted.</summary>
        public sealed class RichMemberBlock : LogicBlockBase
        {
            public Meter Service { get; } = new();

            [Persistent]
            public double Level { get; set; }

            [Persistent]
            public double? Optional { get; set; }

            [Persistent]
            public SampleMode Mode { get; set; }

            [Persistent]
            public ImmutableArray<double> Samples { get; set; } = ImmutableArray<double>.Empty;

            public RichMemberBlock() : base(NullLogger.Instance)
            {
            }

            public void SetLevel(double level)
            {
                Level = level;
            }

            public object? Read(string key)
            {
                return key.EndsWith(nameof(Level), StringComparison.Ordinal) ? Level : key.EndsWith(nameof(Optional), StringComparison.Ordinal) ? Optional! : Mode;
            }

            protected override void Ready()
            {
            }
        }
    }
}