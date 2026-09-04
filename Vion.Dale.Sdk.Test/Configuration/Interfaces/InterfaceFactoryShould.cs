using System;
using System.Linq;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.CodeGeneration;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Configuration.Interfaces
{
    /// <summary>
    ///     How an endpoint's implementation is found, instantiated and registered — the conventions the
    ///     generator emits and the factory reads back, which no consumer writes and every consumer's rebuild
    ///     depends on. The three declarations below (an orphan sender interface, a twinned one and one whose
    ///     extension class lacks its registration method) are bound by nothing else in this project, so each
    ///     refusal is reachable without poisoning another suite's binding.
    /// </summary>
    [TestClass]
    public class InterfaceFactoryShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-BIND-005.1")]
        public void BuildEndpointFromGeneratedSenderOutsideSdkAssembly()
        {
            // Arrange — the sender class for IBindSink is generated into this test assembly, not the SDK's.
            var block = new BindSinkBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.HasCount(1, block.BoundInterfaceIdentifiers());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-005.2")]
        public void RefuseEndpointWithNoSenderImplementation()
        {
            // Arrange
            var block = new OrphanBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, nameof(IBindOrphanSender));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-005.2")]
        public void RefuseEndpointWithSeveralSenderImplementations()
        {
            // Arrange
            var block = new TwinBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, nameof(TwinSenderOne));
            StringAssert.Contains(exception.Message, nameof(TwinSenderTwo));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-005.3")]
        public void BindEndpointWithoutGeneratedExtensionClass()
        {
            // Arrange — IBindListener receives only, so the generator emits no extension class to register in.
            Assert.IsNull(typeof(IBindListener).Assembly.GetType($"{typeof(IBindListener).Namespace}.{nameof(IBindListener)}Extensions"));
            var block = new BindListenerBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            CollectionAssert.AreEquivalent(new[] { nameof(IBindListener) }, block.BoundInterfaceIdentifiers().ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-005.3")]
        public void RefuseEndpointWhoseExtensionClassCarriesNoRegistration()
        {
            // Arrange
            var block = new UnregisteredBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, "RegisterInstance");
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-005.4")]
        public void NameEndpointMemberAndBlockWhenBuildingFails()
        {
            // Arrange
            var block = new OrphanBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, OrphanBlock.EndpointIdentifier);
            StringAssert.Contains(exception.Message, nameof(IBindOrphan));
            StringAssert.Contains(exception.Message, typeof(OrphanBlock).FullName!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-005.4")]
        public void CarryUnderlyingReasonWhenBuildingFails()
        {
            // Arrange
            var block = new OrphanBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, "No valid implementation found");
        }

        /// <summary>A block binding the orphan endpoint.</summary>
        [LogicBlockInterfaceBinding(typeof(IBindOrphan), Identifier = EndpointIdentifier)]
        private sealed class OrphanBlock : LogicBlockBase, IBindOrphan
        {
            public const string EndpointIdentifier = "Orphan";

            public OrphanBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block binding the twinned endpoint.</summary>
        private sealed class TwinBlock : LogicBlockBase, IBindTwin
        {
            public TwinBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block binding the endpoint whose extension class carries no registration method.</summary>
        private sealed class UnregisteredBlock : LogicBlockBase, IBindNoRegister
        {
            public UnregisteredBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }
    }

    /// <summary>
    ///     The contract the three hand-written declarations below name, so they are not mistaken for roles of
    ///     a real one. It declares no message, which is why the generator emits no faces for its own roles.
    /// </summary>
    [LogicBlockContract(BetweenInterface = "IBindFactoryProbeLeft", AndInterface = "IBindFactoryProbeRight")]
    public static class BindFactoryProbeContract;

    /// <summary>A sender interface no concrete class implements — the factory's zero-implementation arm.</summary>
    public interface IBindOrphanSender : ILogicSenderInterface;

    /// <inheritdoc cref="IBindOrphanSender" />
    [LogicInterface(MatchingInterface = typeof(IBindOrphan), SenderInterface = typeof(IBindOrphanSender), ContractType = typeof(BindFactoryProbeContract))]
    public interface IBindOrphan : ILogicHandlerInterface;

    /// <summary>A sender interface two concrete classes implement — the factory's several-implementations arm.</summary>
    public interface IBindTwinSender : ILogicSenderInterface;

    /// <inheritdoc cref="IBindTwinSender" />
    [LogicInterface(MatchingInterface = typeof(IBindTwin), SenderInterface = typeof(IBindTwinSender), ContractType = typeof(BindFactoryProbeContract))]
    public interface IBindTwin : ILogicHandlerInterface;

    /// <summary>The first of the two implementations that make an endpoint ambiguous.</summary>
    public sealed class TwinSenderOne : LogicSenderInterfaceBase, IBindTwinSender
    {
        public TwinSenderOne(string identifier, IBindTwin implementation, Func<LogicBlockId> logicBlockId, IActorContext actorContext, ILogger logger) : base(identifier,
            typeof(IBindTwin),
            typeof(IBindTwin),
            logicBlockId,
            actorContext,
            logger)
        {
        }

        public override void HandleMessage(IFunctionInterfaceMessage functionInterfaceMessage)
        {
        }
    }

    /// <summary>The second of them.</summary>
    public sealed class TwinSenderTwo : LogicSenderInterfaceBase, IBindTwinSender
    {
        public TwinSenderTwo(string identifier, IBindTwin implementation, Func<LogicBlockId> logicBlockId, IActorContext actorContext, ILogger logger) : base(identifier,
            typeof(IBindTwin),
            typeof(IBindTwin),
            logicBlockId,
            actorContext,
            logger)
        {
        }

        public override void HandleMessage(IFunctionInterfaceMessage functionInterfaceMessage)
        {
        }
    }

    /// <summary>A sender interface whose one implementation is reachable, so only the registration can fail.</summary>
    public interface IBindNoRegisterSender : ILogicSenderInterface;

    /// <inheritdoc cref="IBindNoRegisterSender" />
    [LogicInterface(MatchingInterface = typeof(IBindNoRegister), SenderInterface = typeof(IBindNoRegisterSender), ContractType = typeof(BindFactoryProbeContract))]
    public interface IBindNoRegister : ILogicHandlerInterface;

    /// <inheritdoc cref="IBindNoRegisterSender" />
    public sealed class BindNoRegisterSender : LogicSenderInterfaceBase, IBindNoRegisterSender
    {
        public BindNoRegisterSender(string identifier, IBindNoRegister implementation, Func<LogicBlockId> logicBlockId, IActorContext actorContext, ILogger logger) :
            base(identifier,
                 typeof(IBindNoRegister),
                 typeof(IBindNoRegister),
                 logicBlockId,
                 actorContext,
                 logger)
        {
        }

        public override void HandleMessage(IFunctionInterfaceMessage functionInterfaceMessage)
        {
        }
    }

    /// <summary>
    ///     The type the factory looks up by naming convention, deliberately without the registration method
    ///     the generator would have emitted.
    /// </summary>
    public static class IBindNoRegisterExtensions
    {
        /// <summary>Occupies the convention's name and nothing else.</summary>
        public static int Unrelated(this IBindNoRegister implementation)
        {
            return implementation.GetHashCode();
        }
    }
}