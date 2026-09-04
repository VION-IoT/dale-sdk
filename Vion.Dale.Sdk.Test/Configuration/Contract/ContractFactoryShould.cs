using System;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Configuration.Contract
{
    /// <summary>
    ///     How a contract's implementation is found. The two contract types below are bound by nothing else in
    ///     this project — one has no implementation at all and one has two — so each answer is reachable
    ///     without disturbing another suite's binding.
    /// </summary>
    [TestClass]
    public class ContractFactoryShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-BIND-008.1")]
        public void BindOneImplementationWhereSeveralAreLoaded()
        {
            // Arrange
            var block = new AmbiguousContractBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert — which of the two is an enumeration order, so the promise is that exactly one is bound.
            Assert.IsInstanceOfType<LogicBlockContractBase>(block.Ambiguous);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-008.2")]
        public void RefuseContractWithNoLoadedImplementation()
        {
            // Arrange
            var block = new UnimplementedContractBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, typeof(IUnimplementedContract).FullName!);
            StringAssert.Contains(exception.Message, UnimplementedContractBlock.ContractIdentifier);
        }

        /// <summary>A block binding the contract two types implement.</summary>
        private sealed class AmbiguousContractBlock : LogicBlockBase
        {
            [ServiceProviderContractBinding(Identifier = "Ambiguous")]
            public IAmbiguousContract? Ambiguous { get; private set; }

            public AmbiguousContractBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block binding the contract nothing implements.</summary>
        private sealed class UnimplementedContractBlock : LogicBlockBase
        {
            public const string ContractIdentifier = "Unimplemented";

            [ServiceProviderContractBinding(Identifier = ContractIdentifier)]
            public IUnimplementedContract? Missing { get; private set; }

            public UnimplementedContractBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }
    }

    /// <summary>A contract type no concrete class implements.</summary>
    [ServiceProviderContractType("BindUnimplemented")]
    public interface IUnimplementedContract
    {
    }

    /// <summary>A contract type two differently named concrete classes implement.</summary>
    [ServiceProviderContractType("BindAmbiguous")]
    public interface IAmbiguousContract
    {
    }

    /// <summary>The first of the two implementations that make a contract's pick an enumeration order.</summary>
    public sealed class AmbiguousContractOne : LogicBlockContractBase, IAmbiguousContract
    {
        public override string ContractHandlerActorName { get; protected set; } = "AmbiguousHandler";

        public AmbiguousContractOne(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public override void HandleContractMessage(IContractMessage contractMessage)
        {
        }
    }

    /// <summary>The second of them.</summary>
    public sealed class AmbiguousContractTwo : LogicBlockContractBase, IAmbiguousContract
    {
        public override string ContractHandlerActorName { get; protected set; } = "AmbiguousHandler";

        public AmbiguousContractTwo(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public override void HandleContractMessage(IContractMessage contractMessage)
        {
        }
    }
}