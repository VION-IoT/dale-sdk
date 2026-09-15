using System;
using System.IO;
using System.Reflection;
using System.Reflection.Emit;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Reflection;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Configuration.Contract
{
    /// <summary>
    ///     How a contract's implementation is found. The contract types below are bound by nothing else in
    ///     this project — one has no implementation at all, one has two, and one is referenced by a dynamic
    ///     assembly mid-emission — so each answer is reachable without disturbing another suite's binding.
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
        [TestProperty("spec", "AC-BIND-008.1")]
        public void BindContractWhileDynamicAssemblyIsMidEmission()
        {
            // Arrange — a type defined but not yet created is the state a proxy generator leaves its shared dynamic
            // assembly in while it emits: the assembly already references the contract's own, and enumerating it
            // fails on the unfinished type. It is left unfinished for the rest of the process, as a generator mid-way
            // through an emission would leave it at the instant of a binding.
            var dynamicModule = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName(Guid.NewGuid().ToString()), AssemblyBuilderAccess.Run).DefineDynamicModule("Proxies");
            dynamicModule.DefineType("PendingProxy", TypeAttributes.Public).AddInterfaceImplementation(typeof(IMidEmissionContract));
            var block = new MidEmissionContractBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.IsInstanceOfType<MidEmissionContract>(block.Bound);
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

        [TestMethod]
        [TestProperty("spec", "AC-BIND-008.3")]
        public void RefuseContractWhereConsideredAssemblyCannotBeEnumerated()
        {
            // Arrange — three saved assemblies, two of them loaded: one declares the contract, and the other implements it
            // on a base type from the third, which is never loaded, so enumerating the implementation fails. Every name is
            // fresh, so no other binding in the process considers either loaded assembly. The contract exists only at run
            // time, which is why the factory is driven directly rather than through a block's configuration.
            var coreLibrary = typeof(object).Assembly;
            var declaringBuilder = new PersistedAssemblyBuilder(new AssemblyName($"Declaring{Guid.NewGuid():N}"), coreLibrary);
            var contractType = declaringBuilder.DefineDynamicModule("Declaring").DefineType("IRuntimeContract", TypeAttributes.Public | TypeAttributes.Interface | TypeAttributes.Abstract);
            contractType.CreateType();
            var absentBuilder = new PersistedAssemblyBuilder(new AssemblyName($"Absent{Guid.NewGuid():N}"), coreLibrary);
            var absentBase = absentBuilder.DefineDynamicModule("Absent").DefineType("AbsentBase", TypeAttributes.Public);
            absentBase.CreateType();
            var unenumerableName = $"Unenumerable{Guid.NewGuid():N}";
            var unenumerableBuilder = new PersistedAssemblyBuilder(new AssemblyName(unenumerableName), coreLibrary);
            var implementation = unenumerableBuilder.DefineDynamicModule("Unenumerable").DefineType("RuntimeContract", TypeAttributes.Public, absentBase);
            implementation.AddInterfaceImplementation(contractType);
            implementation.CreateType();
            var declaringImage = new MemoryStream();
            declaringBuilder.Save(declaringImage);
            var unenumerableImage = new MemoryStream();
            unenumerableBuilder.Save(unenumerableImage);
            var contract = Assembly.Load(declaringImage.ToArray()).GetType("IRuntimeContract", true)!;
            Assembly.Load(unenumerableImage.ToArray());
            var factory = new ContractFactory((_, _) => { }, null!, BindHosts.Bare);

            // Act / Assert
            var exception = Assert.Throws<AssemblyTypeLoadException>(() => factory.Create(contract, Guid.NewGuid().ToString()));
            StringAssert.Contains(exception.Message, unenumerableName);
            StringAssert.Contains(exception.Message, contract.FullName!);
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

        /// <summary>A block binding the contract a dynamic assembly is mid-way through emitting a type for.</summary>
        private sealed class MidEmissionContractBlock : LogicBlockBase
        {
            [ServiceProviderContractBinding(Identifier = "MidEmission")]
            public IMidEmissionContract? Bound { get; private set; }

            public MidEmissionContractBlock() : base(NullLogger.Instance)
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

    /// <summary>A contract type a dynamic assembly references while one ordinary class implements it.</summary>
    [ServiceProviderContractType("BindMidEmission")]
    public interface IMidEmissionContract
    {
    }

    /// <summary>The ordinary implementation of <see cref="IMidEmissionContract" />.</summary>
    public sealed class MidEmissionContract : LogicBlockContractBase, IMidEmissionContract
    {
        public override string ContractHandlerActorName { get; protected set; } = "MidEmissionHandler";

        public MidEmissionContract(string identifier, IActorContext actorContext) : base(identifier, actorContext)
        {
        }

        public override void HandleContractMessage(IContractMessage contractMessage)
        {
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