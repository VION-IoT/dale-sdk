using System;
using System.Collections.Immutable;
using System.Linq;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;

namespace Vion.Dale.Sdk.Test.Introspection
{
    public interface ITestInterfaceWithPresentation
    {
        [ServiceProperty(Title = "Power")]
        [Presentation(Group = PropertyGroup.Status, Importance = Importance.Primary)]
        double Power { get; }
    }

    public class LbWithInterfacePresentation : LogicBlockBase, ITestInterfaceWithPresentation
    {
        public LbWithInterfacePresentation() : base(new Mock<ILogger>().Object)
        {
        }

        [Presentation(DisplayName = "PV-Power")]
        public double Power { get; private set; }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    public class LbWithoutClassPresentation : LogicBlockBase, ITestInterfaceWithPresentation
    {
        public LbWithoutClassPresentation() : base(new Mock<ILogger>().Object)
        {
        }

        public double Power { get; private set; }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    // DF-35: emission knobs declared once on a [ServiceInterface] property; the impl carries none.
    [ServiceInterface]
    public interface IThrottleViaInterface
    {
        [ServiceProperty(MinInterval = "1s", MinChange = "0.1")]
        double Reading { get; }
    }

    public class ThrottleInheritedLb : LogicBlockBase, IThrottleViaInterface
    {
        public ThrottleInheritedLb() : base(new Mock<ILogger>().Object)
        {
        }

        // Bare impl — no [ServiceProperty]; the emission knobs live only on the interface.
        public double Reading { get; private set; }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    public class ThrottleImplOverrideLb : LogicBlockBase, IThrottleViaInterface
    {
        public ThrottleImplOverrideLb() : base(new Mock<ILogger>().Object)
        {
        }

        // Impl declares its own (non-default) policy — it must win over the interface's.
        [ServiceProperty(MinInterval = "2s")]
        public double Reading { get; private set; }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    public class ThrottleImplBareLb : LogicBlockBase, IThrottleViaInterface
    {
        public ThrottleImplBareLb() : base(new Mock<ILogger>().Object)
        {
        }

        // Impl declares a bare (default-policy) [ServiceProperty]: it owns the policy (default), so no
        // chip is surfaced — exactly as the gate uses the impl's default, not the interface's knobs.
        [ServiceProperty]
        public double Reading { get; private set; }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    [TestClass]
    public class PropertyMetadataBuilderShould
    {
        [TestMethod]
        public void MergePresentationFromInterfaceAndClassPerField()
        {
            var schemaSource = typeof(ITestInterfaceWithPresentation).GetProperty(nameof(ITestInterfaceWithPresentation.Power))!;
            var presentationSource = typeof(LbWithInterfacePresentation).GetProperty(nameof(LbWithInterfacePresentation.Power))!;

            var pm = PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                        presentationSource,
                                                        new PrimitiveTypeRef(PrimitiveKind.Double),
                                                        ImmutableDictionary<string, TypeAnnotations>.Empty);

            // Class wins on DisplayName (it was explicitly set on the class).
            Assert.AreEqual("PV-Power", pm.Presentation.DisplayName);

            // Interface fills Group and Importance (class didn't set them).
            Assert.AreEqual(PropertyGroup.Status, pm.Presentation.Group);
            Assert.AreEqual("Primary", pm.Presentation.Importance);
        }

        [TestMethod]
        public void InheritEntirePresentationWhenClassDeclaresNone()
        {
            var schemaSource = typeof(ITestInterfaceWithPresentation).GetProperty(nameof(ITestInterfaceWithPresentation.Power))!;
            var presentationSource = typeof(LbWithoutClassPresentation).GetProperty(nameof(LbWithoutClassPresentation.Power))!;

            var pm = PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                        presentationSource,
                                                        new PrimitiveTypeRef(PrimitiveKind.Double),
                                                        ImmutableDictionary<string, TypeAnnotations>.Empty);

            // Class declared no [Presentation], so all interface presentation cascades through.
            Assert.AreEqual(PropertyGroup.Status, pm.Presentation.Group);
            Assert.AreEqual("Primary", pm.Presentation.Importance);
        }

        [TestMethod]
        public void PopulateThrottleRuntimeForANonDefaultPolicy()
        {
            var voltage = typeof(ThrottledLb).GetProperty(nameof(ThrottledLb.Voltage))!;
            var pm = PropertyMetadataBuilder.BuildSplit(voltage, voltage, new PrimitiveTypeRef(PrimitiveKind.Double), ImmutableDictionary<string, TypeAnnotations>.Empty);

            Assert.IsNotNull(pm.Runtime.Throttle);
            Assert.AreEqual("1s", pm.Runtime.Throttle!.MinInterval);
            Assert.AreEqual("0.1", pm.Runtime.Throttle!.MinChange);
            Assert.IsFalse(pm.Runtime.Throttle!.Immediate);
        }

        [TestMethod]
        public void OmitThrottleRuntimeForTheDefaultPolicy()
        {
            var plain = typeof(ThrottledLb).GetProperty(nameof(ThrottledLb.Plain))!;
            var pm = PropertyMetadataBuilder.BuildSplit(plain, plain, new PrimitiveTypeRef(PrimitiveKind.Double), ImmutableDictionary<string, TypeAnnotations>.Empty);

            Assert.IsNull(pm.Runtime.Throttle);
        }

        [TestMethod]
        public void InheritThrottleRuntimeFromTheInterfaceWhenImplDeclaresNoEmissionAttribute()
        {
            // DF-35: the impl property carries no [ServiceProperty], the knobs live on the interface.
            // The runtime gate inherits them (DF-33); introspection must surface them too so the UI chip
            // renders for the §8.12 DRY pattern.
            var schemaSource = typeof(IThrottleViaInterface).GetProperty(nameof(IThrottleViaInterface.Reading))!;
            var presentationSource = typeof(ThrottleInheritedLb).GetProperty(nameof(ThrottleInheritedLb.Reading))!;

            var pm = PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                        presentationSource,
                                                        new PrimitiveTypeRef(PrimitiveKind.Double),
                                                        ImmutableDictionary<string, TypeAnnotations>.Empty);

            Assert.IsNotNull(pm.Runtime.Throttle);
            Assert.AreEqual("1s", pm.Runtime.Throttle!.MinInterval);
            Assert.AreEqual("0.1", pm.Runtime.Throttle!.MinChange);
        }

        [TestMethod]
        public void PreferImplThrottleOverTheInterfaceWhenBothDeclareKnobs()
        {
            // Impl owns its own [ServiceProperty(2s)] — it wins over the interface's 1s, mirroring the gate.
            var schemaSource = typeof(IThrottleViaInterface).GetProperty(nameof(IThrottleViaInterface.Reading))!;
            var presentationSource = typeof(ThrottleImplOverrideLb).GetProperty(nameof(ThrottleImplOverrideLb.Reading))!;

            var pm = PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                        presentationSource,
                                                        new PrimitiveTypeRef(PrimitiveKind.Double),
                                                        ImmutableDictionary<string, TypeAnnotations>.Empty);

            Assert.IsNotNull(pm.Runtime.Throttle);
            Assert.AreEqual("2s", pm.Runtime.Throttle!.MinInterval);
        }

        [TestMethod]
        public void OmitThrottleWhenImplDeclaresADefaultPolicyEvenIfTheInterfaceHasKnobs()
        {
            // The impl declares a bare (default) [ServiceProperty], so it owns the policy — and the default
            // policy is not surfaced. The interface's knobs must NOT leak through (the gate uses the impl's
            // default, not the interface). Guards against a naive "impl ?? interface" that would show "1s/0.1".
            var schemaSource = typeof(IThrottleViaInterface).GetProperty(nameof(IThrottleViaInterface.Reading))!;
            var presentationSource = typeof(ThrottleImplBareLb).GetProperty(nameof(ThrottleImplBareLb.Reading))!;

            var pm = PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                        presentationSource,
                                                        new PrimitiveTypeRef(PrimitiveKind.Double),
                                                        ImmutableDictionary<string, TypeAnnotations>.Empty);

            Assert.IsNull(pm.Runtime.Throttle);
        }

        [TestMethod]
        public void CarryImmediateAndTheEffectiveDefaultIntervalInThrottleRuntime()
        {
            var pulse = typeof(ThrottledLb).GetProperty(nameof(ThrottledLb.Pulse))!;
            var pm = PropertyMetadataBuilder.BuildSplit(pulse, pulse, new PrimitiveTypeRef(PrimitiveKind.Double), ImmutableDictionary<string, TypeAnnotations>.Empty);

            Assert.IsNotNull(pm.Runtime.Throttle);
            Assert.IsTrue(pm.Runtime.Throttle!.Immediate);

            // The effective interval is carried even though it is the default — the consumer needs no
            // knowledge of the 250ms default to render a complete badge.
            Assert.AreEqual("250ms", pm.Runtime.Throttle!.MinInterval);
        }

        private sealed class ThrottledLb : LogicBlockBase
        {
            [ServiceProperty(MinInterval = "1s", MinChange = "0.1")]
            public double Voltage { get; private set; }

            [ServiceProperty]
            public double Plain { get; private set; }

            [ServiceProperty(Immediate = true)]
            public double Pulse { get; private set; }

            public ThrottledLb() : base(new Mock<ILogger>().Object)
            {
            }

            protected override void Ready()
            {
            }

            protected override void Starting()
            {
            }
        }
    }

    public abstract class BaseSortLb : LogicBlockBase
    {
        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Status)]
        public double BaseProp1 { get; set; }

        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Status)]
        public double BaseProp2 { get; set; }

        protected BaseSortLb() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    public class DerivedSortLb : BaseSortLb
    {
        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Status)]
        public double DerivedProp1 { get; set; }
    }

    [TestClass]
    public class LogicBlockIntrospectionOrderingShould
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        [TestMethod]
        public void EmitPropertiesInBaseToDerivedOrder()
        {
            var block = new DerivedSortLb();
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var service = result.Services.Single();
            var propIds = service.Properties.Select(p => p.Identifier).ToList();

            // BaseSortLb declares BaseProp1, BaseProp2; DerivedSortLb adds DerivedProp1.
            // Expected order: base-class properties first (in declaration order), then derived.
            CollectionAssert.AreEqual(new[] { "BaseProp1", "BaseProp2", "DerivedProp1" }, propIds, $"Got order: {string.Join(", ", propIds)}");
        }

        [TestMethod]
        public void EmitRuntimeThrottleNodeForAnInterfaceInheritedPolicy()
        {
            // DF-35 end-to-end: the block's throttle knobs live on the [ServiceInterface]; the impl carries
            // only a bare property. Introspection must emit the runtime.throttle JSON node the DevHost/cloud
            // UI chip reads — not null (the reported symptom: "the RUNTIME panel shows null").
            var result = LogicBlockIntrospection.IntrospectLogicBlock(new ThrottleInheritedLb(), _serviceProvider);

            var reading = result.Services.Single().Properties.Single(p => p.Identifier == "Reading");

            Assert.IsNotNull(reading.Runtime, "runtime node must be present");
            var throttle = reading.Runtime!["throttle"];
            Assert.IsNotNull(throttle, "runtime.throttle must be emitted for an interface-inherited policy");
            Assert.AreEqual("1s", throttle!["minInterval"]!.GetValue<string>());
            Assert.AreEqual("0.1", throttle["minChange"]!.GetValue<string>());
        }
    }
}