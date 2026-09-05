using System;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.TestKit.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     What <c>Build()</c> does to a block before a test touches it, and what each knob changes about
    ///     it. The block is a fixture; nothing here reaches a runtime, a broker, a device or the
    ///     development host. The five phases the builder drives are the block lifecycle's
    ///     (<c>docs/specs/block-lifecycle.md</c>) — what this suite pins is that the builder drives them,
    ///     in order, with the identity and the discovery it composes.
    /// </summary>
    [TestClass]
    public class LogicBlockTestContextBuilderShould
    {
        // The marker is internal to the SDK and granted to this kit alone, so a test reaches it by name
        // rather than by type — a consumer could not write this assertion, which is exactly what the
        // BuiltServiceProvider documentation had to stop claiming.
        private static readonly Type EmissionPolicyForceMarkerType = typeof(LogicBlockBase).Assembly.GetType("Vion.Dale.Sdk.Emission.EmissionPolicyForceMarker")!;

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.1")]
        public void DriveBlockThroughEveryPhaseInOrder()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<PhaseRecordingLogicBlock>();

            // Act
            block.CreateTestContext().WithPersistentValue(lb => lb.Restored, 7).Build();

            // Assert — Ready runs once initialization and the runtime-actor linking have both happened,
            // the restore after that, and Starting last
            CollectionAssert.AreEqual(new[] { "Ready", "Restore", "Starting" }, block.Phases);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.2")]
        public void InitializeBlockUnderFixedLogicBlockIdentity()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();

            // Act — the block announces its bound services at the end of initialization, carrying the
            // identity it was initialized under
            var context = block.CreateTestContext().WithoutAutoStart().Build();

            // Assert
            var announcements = context.GetSentMessagesOfTypePublic<BindLogicBlockServices>();
            Assert.IsNotEmpty(announcements);
            Assert.AreEqual(Constants.LogicBlockId, announcements[0].LogicBlockId.Id);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.2")]
        public void DiscoverServiceIdentifierFromBlockClassName()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();

            // Act
            var context = block.CreateTestContext().Build();
            block.Power = 1.0;

            // Assert — a property change routed to no service would carry no service identifier
            var changes = context.GetSentMessagesOfTypePublic<ServicePropertyValueChanged>();
            Assert.AreEqual(nameof(SampleLogicBlock), changes[0].ServiceIdentifier.Id);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.3")]
        public void RestorePersistentValueUnderKeyBlocksBindingsGiveIt()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<PersistentLogicBlock>();

            // Act
            block.CreateTestContext().WithPersistentValue(lb => lb.MaxPower, 42.0).Build();

            // Assert
            Assert.AreEqual(42.0, block.MaxPower);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.3")]
        public void RestoreSeveralPersistentValuesTogether()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<PersistentLogicBlock>();

            // Act
            block.CreateTestContext().WithPersistentValue(lb => lb.MaxPower, 42.0).WithPersistentValue(lb => lb.Mode, PersistentLogicBlock.OperatingMode.Manual).Build();

            // Assert
            Assert.AreEqual(42.0, block.MaxPower);
            Assert.AreEqual(PersistentLogicBlock.OperatingMode.Manual, block.Mode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.3")]
        public void StoreEnumerationPersistentValueAsItsIntegerForm()
        {
            // Arrange — the persistence system stores an enum as an int, so a restore that passed the
            // enum through would fail to bind rather than restoring the wrong value
            var block = LogicBlockTestHelper.Create<PersistentLogicBlock>();

            // Act
            block.CreateTestContext().WithPersistentValue(lb => lb.Mode, PersistentLogicBlock.OperatingMode.Manual).Build();

            // Assert
            Assert.AreEqual(PersistentLogicBlock.OperatingMode.Manual, block.Mode);
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.5")]
        public void StartBlockAndClearWhatStartProduced()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();

            // Act
            var context = block.CreateTestContext().Build();

            // Assert — a verify straight after Build counts only what the test then causes
            Assert.IsEmpty(context.GetSentMessagesOfTypePublic<object>());
            block.Power = 1.0;
            Assert.HasCount(1, context.GetSentMessagesOfTypePublic<object>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.5")]
        public void LeaveEarlierPhasesMessagesRecordedWhenStartSuppressed()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();

            // Act
            var context = block.CreateTestContext().WithoutAutoStart().Build();

            // Assert — the clear happens inside the start, so suppressing the start keeps what the
            // earlier phases produced; an initialization test is what those messages are for
            Assert.IsNotEmpty(context.GetSentMessagesOfTypePublic<BindLogicBlockServices>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.5")]
        public void PublishNoPropertyChangeWhileStartSuppressed()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var context = block.CreateTestContext().WithoutAutoStart().Build();

            // Act
            block.Power = 1.0;

            // Assert
            Assert.IsEmpty(context.GetSentMessagesOfTypePublic<ServicePropertyValueChanged>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-002.6")]
        public void ExposeServiceProviderItComposed()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var service = new object();

            // Act
            var context = block.CreateTestContext().WithServices(services => services.AddSingleton(service)).Build();

            // Assert
            Assert.IsNotNull(context.BuiltServiceProvider);
            Assert.AreSame(service, context.BuiltServiceProvider!.GetService<object>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.1")]
        public void ReturnItselfFromEveryKnob()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var builder = block.CreateTestContext();

            // Act / Assert
            Assert.AreSame(builder, builder.WithoutAutoStart());
            Assert.AreSame(builder, builder.WithEmissionPolicy(EmissionPolicyMode.Off));
            Assert.AreSame(builder, builder.WithTimeProvider(new FakeTimeProvider()));
            Assert.AreSame(builder, builder.WithServices(_ => { }));
            Assert.AreSame(builder, builder.WithPersistentValue(lb => lb.Power, 1.0));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.1")]
        [DataRow(false, DisplayName = "by default")]
        [DataRow(true, DisplayName = "turned off explicitly")]
        public void GateEmissionPolicyOffUnlessAsked(bool explicitlyOff)
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();
            var builder = block.CreateTestContext();

            // Act
            var context = explicitlyOff ? builder.WithEmissionPolicy(EmissionPolicyMode.Off).Build() : builder.Build();

            // Assert — the marker the block reads at initialization is what turns the policy on
            Assert.IsNull(context.BuiltServiceProvider!.GetService(EmissionPolicyForceMarkerType));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.1")]
        public void GateEmissionPolicyOnWhenBlocksDeclaredThrottlingAsked()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<SampleLogicBlock>();

            // Act
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Assert
            Assert.IsNotNull(context.BuiltServiceProvider!.GetService(EmissionPolicyForceMarkerType));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.3")]
        public void RefuseNullBlock()
        {
            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => new LogicBlockTestContextBuilder<SampleLogicBlock>(null!));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.3")]
        public void RefuseNullClock()
        {
            // Arrange
            var builder = LogicBlockTestHelper.Create<SampleLogicBlock>().CreateTestContext();

            // Act / Assert
            Assert.ThrowsExactly<ArgumentNullException>(() => builder.WithTimeProvider(null!));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.3")]
        public void RefuseMappingWhoseTargetClassCarriesSeveralContracts()
        {
            // Arrange — without an explicit contract argument the mapping would route to whichever
            // contract came first in metadata order, and the second would silently receive none
            var block = LogicBlockTestHelper.Create<MultiSenderLogicBlock>();
            var builder = block.CreateTestContext();

            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => builder.WithLogicInterfaceMapping(lb => lb, new InterfaceId("other-a", "IFakeContractA")));
            StringAssert.Contains(thrown.Message, nameof(MultiSenderLogicBlock));
            StringAssert.Contains(thrown.Message, "WithLogicInterfaceMapping<");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.3")]
        public void AcceptMappingNamingContractExplicitly()
        {
            // Arrange — spelling the contract out is what removes the ambiguity the guard refuses
            var builder = LogicBlockTestHelper.Create<MultiSenderLogicBlock>().CreateTestContext();

            // Act / Assert — the knob returning is the whole of it; a throw is the failure
            builder.WithLogicInterfaceMapping<IFakeContractA>(lb => lb, new InterfaceId("other-a", "IFakeContractA"))
                   .WithLogicInterfaceMapping<IFakeContractB>(lb => lb, new InterfaceId("other-b", "IFakeContractB"));
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.3")]
        public void RefuseMappingForContractBlockDoesNotImplement()
        {
            // Arrange
            var builder = LogicBlockTestHelper.Create<SampleLogicBlock>().CreateTestContext();

            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => builder.WithLogicInterfaceMapping<IFakeContractA>(new InterfaceId("other-a", "IFakeContractA")));
            StringAssert.Contains(thrown.Message, "does not implement interface");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.3")]
        public void RefuseSelectorOtherThanPropertyAccess()
        {
            // Arrange
            var builder = LogicBlockTestHelper.Create<SampleLogicBlock>().CreateTestContext();

            // Act / Assert
            var thrown = Assert.ThrowsExactly<ArgumentException>(() => builder.WithPersistentValue(lb => lb.ToString(), "x"));
            StringAssert.Contains(thrown.Message, "Expression must be a property access");
        }

        [TestMethod]
        [TestProperty("spec", "AC-TKIT-003.3")]
        public void RefuseInstantiationParameterNamingPropertyBlockDoesNotDeclare()
        {
            // Arrange
            var builder = LogicBlockTestHelper.Create<SampleLogicBlock>().CreateTestContext();

            // Act / Assert
            var thrown = Assert.ThrowsExactly<InvalidOperationException>(() => builder.WithInstantiationParameter(lb => lb.Power, 3.0).Build());
            StringAssert.Contains(thrown.Message, nameof(SampleLogicBlock.Power));
        }
    }
}