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

            var pm = PropertyMetadataBuilder.BuildSplit(
                schemaSource,
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

            var pm = PropertyMetadataBuilder.BuildSplit(
                schemaSource,
                presentationSource,
                new PrimitiveTypeRef(PrimitiveKind.Double),
                ImmutableDictionary<string, TypeAnnotations>.Empty);

            // Class declared no [Presentation], so all interface presentation cascades through.
            Assert.AreEqual(PropertyGroup.Status, pm.Presentation.Group);
            Assert.AreEqual("Primary", pm.Presentation.Importance);
        }
    }

    public abstract class BaseSortLb : LogicBlockBase
    {
        protected BaseSortLb() : base(new Mock<ILogger>().Object)
        {
        }

        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Status)]
        public double BaseProp1 { get; set; }

        [ServiceProperty]
        [Presentation(Group = PropertyGroup.Status)]
        public double BaseProp2 { get; set; }

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
        private readonly IServiceProvider _serviceProvider = new ServiceCollection()
                                                             .AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
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
            CollectionAssert.AreEqual(
                new[] { "BaseProp1", "BaseProp2", "DerivedProp1" },
                propIds,
                $"Got order: {string.Join(", ", propIds)}");
        }
    }
}
