using System;
using System.Collections.Immutable;
using System.Linq;
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
    }
}