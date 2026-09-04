using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Configuration.Contract
{
    /// <summary>
    ///     Which properties offer a service-provider contract endpoint, what the binder constructs there, and
    ///     which declarations it refuses because no walk can read them. The contract instance is read off the
    ///     block's own property, which is where the binder puts it.
    /// </summary>
    [TestClass]
    public class ContractBindingShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-BIND-001.1")]
        [TestProperty("spec", "AC-BIND-007.1")]
        public void BindContractForPropertyOfMarkedType()
        {
            // Arrange
            var block = new BindContractBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.IsInstanceOfType<BindProbeContract>(block.Probe);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-001.2")]
        public void BindContractForPropertyCarryingNoBindingAttribute()
        {
            // Arrange
            var block = new UnannotatedContractBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.IsInstanceOfType<BindProbeContract>(block.Probe);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-001.3")]
        public void RefuseContractBindingOnPropertyOfUnmarkedType()
        {
            // Arrange
            var block = new UnmarkedTypeBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, nameof(UnmarkedTypeBlock.Slot));
            StringAssert.Contains(exception.Message, nameof(IUnmarkedContract));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-007.1")]
        public void BindContractForNonPublicProperty()
        {
            // Arrange
            var block = new HiddenContractBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.IsInstanceOfType<BindProbeContract>(block.BoundProbe());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-007.2")]
        public void RefuseContractPropertyWithNoSetter()
        {
            // Arrange
            var block = new ReadOnlyContractBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, nameof(ReadOnlyContractBlock.Probe));
            StringAssert.Contains(exception.Message, "no setter");
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-007.2")]
        public void BindContractPropertyWithPrivateSetterOnBaseClass()
        {
            // Arrange
            var block = new DerivedContractBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.IsInstanceOfType<BindProbeContract>(block.Inherited);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-007.3")]
        public void ConstructContractWithIdentifierAndActorContext()
        {
            // Arrange
            var block = new BindContractBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.AreEqual(BindContractBlock.ContractIdentifier, ((BindProbeContract)block.Probe!).Identifier);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-007.3")]
        public void ResolveFurtherContractArgumentsFromBlockServiceProvider()
        {
            // Arrange
            var host = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                              .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                              .AddSingleton(new ContractDependency(42))
                                              .BuildServiceProvider();
            var block = new DependentContractBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: host);

            // Assert
            Assert.AreEqual(42, ((DependentContract)block.Dependent!).DependencyValue);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-007.4")]
        public void ApplyDeclaredContractMetadata()
        {
            // Arrange
            var block = new BindContractBlock();

            // Act
            var definition = LogicBlockIntrospection.IntrospectLogicBlock(block, BindHosts.Bare);

            // Assert
            var contract = definition.Contracts.Single(candidate => candidate.Identifier == BindContractBlock.ContractIdentifier);
            Assert.AreEqual("The probe", contract.Annotations["DefaultName"]);
            CollectionAssert.AreEqual(new[] { "io", "probe" }, (System.Collections.Generic.List<string>)contract.Annotations["Tags"]);
        }

        /// <summary>A block whose contract property carries no binding attribute at all.</summary>
        private sealed class UnannotatedContractBlock : LogicBlockBase
        {
            public IBindProbe? Probe { get; private set; }

            public UnannotatedContractBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block whose binding attribute sits on a property of a type carrying no contract marker.</summary>
        private sealed class UnmarkedTypeBlock : LogicBlockBase
        {
            [ServiceProviderContractBinding(Identifier = "NeverBound")]
            public IUnmarkedContract? Slot { get; private set; }

            public UnmarkedTypeBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block whose contract property is not part of its public surface.</summary>
        private sealed class HiddenContractBlock : LogicBlockBase
        {
            [ServiceProviderContractBinding(Identifier = "Hidden")]
            private IBindProbe? Probe { get; set; }

            public HiddenContractBlock() : base(NullLogger.Instance)
            {
            }

            /// <summary>A method, not a property: a contract-typed property here would need a setter (DALE001).</summary>
            public IBindProbe? BoundProbe()
            {
                return Probe;
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block whose contract property declares no setter at all.</summary>
        private sealed class ReadOnlyContractBlock : LogicBlockBase
        {
            // DALE001 refuses this declaration at compile time, which is the guard the runtime refusal
            // mirrors; the fixture exists to reach the runtime one.
#pragma warning disable DALE001
            [ServiceProviderContractBinding(Identifier = "ReadOnly")]
            public IBindProbe? Probe
            {
                get => null;
            }
#pragma warning restore DALE001
            public ReadOnlyContractBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A base class holding the contract property, so the setter is not inherited by reflection.</summary>
        private abstract class BaseContractBlock : LogicBlockBase
        {
            [ServiceProviderContractBinding(Identifier = "Inherited")]
            public IBindProbe? Inherited { get; private set; }

            protected BaseContractBlock() : base(NullLogger.Instance)
            {
            }
        }

        /// <inheritdoc cref="BaseContractBlock" />
        private sealed class DerivedContractBlock : BaseContractBlock
        {
            protected override void Ready()
            {
            }
        }

        /// <summary>A block binding a contract whose constructor needs more than the binder supplies.</summary>
        private sealed class DependentContractBlock : LogicBlockBase
        {
            [ServiceProviderContractBinding(Identifier = "Dependent")]
            public IDependentContract? Dependent { get; private set; }

            public DependentContractBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }
    }

    /// <summary>Something a contract's constructor needs and only the block's service provider has.</summary>
    /// <param name="Value">The value the test reads back off the constructed contract.</param>
    public sealed record ContractDependency(int Value);

    /// <summary>A contract whose constructor takes a dependency beyond the binder's two arguments.</summary>
    [ServiceProviderContractType("BindDependent")]
    public interface IDependentContract
    {
    }

    /// <inheritdoc cref="IDependentContract" />
    public sealed class DependentContract : LogicBlockContractBase, IDependentContract
    {
        public override string ContractHandlerActorName { get; protected set; } = "DependentHandler";

        public int DependencyValue { get; }

        public DependentContract(string identifier, IActorContext actorContext, ContractDependency dependency) : base(identifier, actorContext)
        {
            DependencyValue = dependency.Value;
        }

        public override void HandleContractMessage(IContractMessage contractMessage)
        {
        }
    }
}