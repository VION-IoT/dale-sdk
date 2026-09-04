using System;
using System.Linq;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Test.TestHelpers;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Test.Configuration.Interfaces
{
    /// <summary>
    ///     Which members offer an inter-block endpoint, under which identity, and which declarations are
    ///     refused because no walk can read them. The endpoints a block bound are read from the block's own
    ///     seam rather than by reflection (<c>testing-conventions.md</c> section 7); what the introspection
    ///     document then reports of them is <c>docs/specs/introspection.md</c>'s.
    /// </summary>
    [TestClass]
    public class InterfaceBindingShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-BIND-001.1")]
        public void BindEndpointFromClassImplementedInterface()
        {
            // Arrange
            var block = new BindSourceBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            CollectionAssert.AreEquivalent(new[] { nameof(IBindSource) }, block.BoundInterfaceIdentifiers().ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-001.1")]
        public void BindEndpointFromPublicPropertyType()
        {
            // Arrange
            var block = new BindSinkBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            CollectionAssert.AreEquivalent(new[] { $"{nameof(BindSinkBlock.Endpoint)}_{nameof(IBindSink)}" }, block.BoundInterfaceIdentifiers().ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-001.2")]
        public void BindEndpointOfMarkedTypeCarryingNoBindingAttribute()
        {
            // Arrange — the property carries no [LogicBlockInterfaceBinding] at all.
            var block = new BindSinkBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.HasCount(1, block.BoundInterfaceIdentifiers());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-001.3")]
        public void RefuseClassBindingNamingUnimplementedInterface()
        {
            // Arrange
            var block = new WrongClassBindingBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, nameof(IBindSink));
            StringAssert.Contains(exception.Message, nameof(WrongClassBindingBlock));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-001.3")]
        public void RefusePropertyBindingNamingUnimplementedInterface()
        {
            // Arrange
            var block = new WrongPropertyBindingBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, nameof(IBindSource));
            StringAssert.Contains(exception.Message, nameof(WrongPropertyBindingBlock.Endpoint));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-001.3")]
        public void RefuseInterfaceBindingOnNonPublicProperty()
        {
            // Arrange
            var block = new NonPublicBindingBlock();

            // Act / Assert
            var exception = Assert.Throws<InvalidOperationException>(() => new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare));
            StringAssert.Contains(exception.Message, "Endpoint");
            StringAssert.Contains(exception.Message, "not public");
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-004.1")]
        public void BindOneEndpointPerImplementedInterfaceOfOneMember()
        {
            // Arrange
            var block = new BindBothBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            CollectionAssert.AreEquivalent(new[] { $"{nameof(BindBothBlock.Endpoint)}_{nameof(IBindSource)}", $"{nameof(BindBothBlock.Endpoint)}_{nameof(IBindSink)}" },
                                           block.BoundInterfaceIdentifiers().ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-004.2")]
        public void BindEndpointFromGetOnlyProperty()
        {
            // Arrange — BindSinkBlock's Endpoint has no setter at all.
            var block = new BindSinkBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.HasCount(1, block.BoundInterfaceIdentifiers());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-004.3")]
        public void OfferNoEndpointFromNonPublicPropertyCarryingNoBinding()
        {
            // Arrange — a private helper whose type happens to implement a logic interface. It carries no
            // binding attribute, so the refusal of AC-BIND-001.3 does not reach it and the public-only walk
            // is the whole of what decides.
            var block = new PrivateHelperBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.IsEmpty(block.BoundInterfaceIdentifiers());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-004.3")]
        public void BindEndpointFromPublicPropertyDeclaredOnBaseClass()
        {
            // Arrange — the walk that decides "public" and the walk that reads the attributes are two
            // GetProperties calls with different flags, so an inherited public property has to be the same
            // member to both of them.
            var block = new DerivedEndpointBlock();

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            CollectionAssert.AreEquivalent(new[] { BaseEndpointBlock.EndpointIdentifier }, block.BoundInterfaceIdentifiers().ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-004.4")]
        public void BindNoEndpointForUngatedNullComponent()
        {
            // Arrange
            var block = new NullableSinkBlock(false);

            // Act
            new LifecycleHarness().Configure(block, serviceProvider: BindHosts.Bare);

            // Assert
            Assert.IsEmpty(block.BoundInterfaceIdentifiers());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-004.4")]
        public void DescribeEndpointOfUngatedNullComponentInDefinitionView()
        {
            // Arrange
            var block = new NullableSinkBlock(false);

            // Act
            var definition = LogicBlockIntrospection.IntrospectLogicBlock(block, BindHosts.Bare);

            // Assert
            CollectionAssert.AreEquivalent(new[] { NullableSinkBlock.EndpointIdentifier }, definition.Interfaces.Select(endpoint => endpoint.Identifier).ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-004.5")]
        public void ApplyDeclaredMetadataPerInterfaceOfOneMember()
        {
            // Arrange
            var block = new BindBothBlock();

            // Act
            var definition = LogicBlockIntrospection.IntrospectLogicBlock(block, BindHosts.Bare);

            // Assert
            var annotated = definition.Interfaces.Single(endpoint => endpoint.Identifier == $"{nameof(BindBothBlock.Endpoint)}_{nameof(IBindSource)}");
            Assert.AreEqual("The source half", annotated.Annotations["DefaultName"]);
            var plain = definition.Interfaces.Single(endpoint => endpoint.Identifier == $"{nameof(BindBothBlock.Endpoint)}_{nameof(IBindSink)}");
            Assert.IsFalse(plain.Annotations.ContainsKey("DefaultName"));
        }

        /// <summary>A block whose class-level binding names an interface the class does not implement.</summary>
        [LogicBlockInterfaceBinding(typeof(IBindSink), Identifier = "NeverMinted")]
        private sealed class WrongClassBindingBlock : LogicBlockBase, IBindSource
        {
            public WrongClassBindingBlock() : base(NullLogger.Instance)
            {
            }

            public void HandleStateUpdate(InterfaceId functionId, BindLinkContract.Level response)
            {
            }

            public void HandleResponse(InterfaceId functionId, BindLinkContract.Reading response)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block whose property binding names an interface the property's type does not implement.</summary>
        private sealed class WrongPropertyBindingBlock : LogicBlockBase
        {
            [LogicBlockInterfaceBinding(typeof(IBindSource), Identifier = "NeverMinted")]
            public BindSinkComponent Endpoint { get; } = new();

            public WrongPropertyBindingBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block whose interface binding sits on a property the public walk never sees.</summary>
        private sealed class NonPublicBindingBlock : LogicBlockBase
        {
            [LogicBlockInterfaceBinding(typeof(IBindSink), Identifier = "NeverMinted")]
            private BindSinkComponent Endpoint { get; } = new();

            public NonPublicBindingBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
                _ = Endpoint;
            }
        }

        /// <summary>A block holding an interface-bearing component privately and declaring no binding on it.</summary>
        private sealed class PrivateHelperBlock : LogicBlockBase
        {
            private BindSinkComponent Helper { get; } = new();

            public PrivateHelperBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
                _ = Helper;
            }
        }

        /// <summary>A base declaring the public bound property, as a block family shares its components.</summary>
        private abstract class BaseEndpointBlock : LogicBlockBase
        {
            public const string EndpointIdentifier = "Inherited";

            [LogicBlockInterfaceBinding(typeof(IBindSink), Identifier = EndpointIdentifier)]
            public BindSinkComponent Endpoint { get; } = new();

            protected BaseEndpointBlock() : base(NullLogger.Instance)
            {
            }
        }

        /// <summary>The block a host instantiates, whose endpoint is declared one level up.</summary>
        private sealed class DerivedEndpointBlock : BaseEndpointBlock
        {
            protected override void Ready()
            {
            }
        }

        /// <summary>A block whose one property offers both halves of the link contract.</summary>
        private sealed class BindBothBlock : LogicBlockBase
        {
            [LogicBlockInterfaceBinding(typeof(IBindSource), DefaultName = "The source half")]
            public BindBothComponent Endpoint { get; } = new();

            public BindBothBlock() : base(NullLogger.Instance)
            {
            }

            protected override void Ready()
            {
            }
        }

        /// <summary>A block whose component may be absent, with no inclusion gate over it.</summary>
        private sealed class NullableSinkBlock : LogicBlockBase
        {
            public const string EndpointIdentifier = "Sink";

            [LogicBlockInterfaceBinding(typeof(IBindSink), Identifier = EndpointIdentifier)]
            public BindSinkComponent? Endpoint { get; }

            public NullableSinkBlock(bool present) : base(NullLogger.Instance)
            {
                Endpoint = present ? new BindSinkComponent() : null;
            }

            protected override void Ready()
            {
            }
        }
    }
}