using System;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Reflection;
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

    // emission knobs declared once on a [ServiceInterface] property; the impl carries none.
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

    // VisibleWhen on a [ServiceInterface] property, inherited/overridable like every other
    // presentation field.
    [ServiceInterface]
    public interface IVisibleWhenInterface
    {
        [ServiceProperty(Title = "Reading")]
        [Presentation(VisibleWhen = "Enabled == true")]
        double Reading { get; }
    }

    // Class inherits VisibleWhen from the interface (declares no [Presentation] of its own).
    public class VisibleWhenInheritedLb : LogicBlockBase, IVisibleWhenInterface
    {
        [ServiceProperty]
        public bool Enabled { get; set; }

        public VisibleWhenInheritedLb() : base(new Mock<ILogger>().Object)
        {
        }

        public double Reading { get; private set; }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    // Class overrides the interface's VisibleWhen with its own predicate.
    public class VisibleWhenOverrideLb : LogicBlockBase, IVisibleWhenInterface
    {
        [ServiceProperty]
        public bool Manual { get; set; }

        public VisibleWhenOverrideLb() : base(new Mock<ILogger>().Object)
        {
        }

        [ServiceProperty]
        [Presentation(VisibleWhen = "Manual == false")]
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
        [TestProperty("spec", "AC-INTRO-009.2")]
        public void EmitVisibleWhenFromPresentationDeclaration()
        {
            // Arrange

            // Act
            var property = typeof(VisibleWhenDirectLb).GetProperty(nameof(VisibleWhenDirectLb.PrimaryCurrentToWriteA))!;
            var pm = PropertyMetadataBuilder.Build(property,
                                                   new PrimitiveTypeRef(PrimitiveKind.Double),
                                                   ImmutableDictionary<string, TypeAnnotations>.Empty,
                                                   ServiceElementStream.Property);

            // Assert
            Assert.AreEqual("DirectMeasurement == false", pm.Presentation.VisibleWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-012.3")]
        public void CascadeVisibleWhenFromInterfaceWhenClassDeclaresNone()
        {
            // Arrange

            // Act
            var schemaSource = typeof(IVisibleWhenInterface).GetProperty(nameof(IVisibleWhenInterface.Reading))!;
            var presentationSource = typeof(VisibleWhenInheritedLb).GetProperty(nameof(VisibleWhenInheritedLb.Reading))!;

            var pm = PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                        presentationSource,
                                                        new PrimitiveTypeRef(PrimitiveKind.Double),
                                                        ImmutableDictionary<string, TypeAnnotations>.Empty,
                                                        ServiceElementStream.Property);

            // Assert
            Assert.AreEqual("Enabled == true", pm.Presentation.VisibleWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-012.3")]
        public void PreferClassVisibleWhenOverInterface()
        {
            // Arrange

            // Act
            var schemaSource = typeof(IVisibleWhenInterface).GetProperty(nameof(IVisibleWhenInterface.Reading))!;
            var presentationSource = typeof(VisibleWhenOverrideLb).GetProperty(nameof(VisibleWhenOverrideLb.Reading))!;

            var pm = PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                        presentationSource,
                                                        new PrimitiveTypeRef(PrimitiveKind.Double),
                                                        ImmutableDictionary<string, TypeAnnotations>.Empty,
                                                        ServiceElementStream.Property);

            // Assert
            Assert.AreEqual("Manual == false", pm.Presentation.VisibleWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-012.2")]
        [TestProperty("spec", "AC-INTRO-012.3")]
        public void MergePresentationFromInterfaceAndClassPerField()
        {
            // Arrange

            // Act
            var schemaSource = typeof(ITestInterfaceWithPresentation).GetProperty(nameof(ITestInterfaceWithPresentation.Power))!;
            var presentationSource = typeof(LbWithInterfacePresentation).GetProperty(nameof(LbWithInterfacePresentation.Power))!;

            var pm = PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                        presentationSource,
                                                        new PrimitiveTypeRef(PrimitiveKind.Double),
                                                        ImmutableDictionary<string, TypeAnnotations>.Empty,
                                                        ServiceElementStream.Property);

            // Class wins on DisplayName (it was explicitly set on the class).

            // Assert
            Assert.AreEqual("PV-Power", pm.Presentation.DisplayName);

            // Interface fills Group and Importance (class didn't set them).
            Assert.AreEqual(PropertyGroup.Status, pm.Presentation.Group);
            Assert.AreEqual("Primary", pm.Presentation.Importance);
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-012.3")]
        public void InheritEntirePresentationWhenClassDeclaresNone()
        {
            // Arrange

            // Act
            var schemaSource = typeof(ITestInterfaceWithPresentation).GetProperty(nameof(ITestInterfaceWithPresentation.Power))!;
            var presentationSource = typeof(LbWithoutClassPresentation).GetProperty(nameof(LbWithoutClassPresentation.Power))!;

            var pm = PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                        presentationSource,
                                                        new PrimitiveTypeRef(PrimitiveKind.Double),
                                                        ImmutableDictionary<string, TypeAnnotations>.Empty,
                                                        ServiceElementStream.Property);

            // Class declared no [Presentation], so all interface presentation cascades through.

            // Assert
            Assert.AreEqual(PropertyGroup.Status, pm.Presentation.Group);
            Assert.AreEqual("Primary", pm.Presentation.Importance);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.1")]
        public void ReportNonDefaultPolicy()
        {
            // Arrange
            var property = typeof(ThrottledLb).GetProperty(nameof(ThrottledLb.Voltage))!;

            // Act
            var metadata = ReportedPolicyOf(property, property);

            // Assert
            Assert.IsNotNull(metadata);
            Assert.AreEqual("1s", metadata!.MinInterval);
            Assert.AreEqual("0.1", metadata.MinChange);
            Assert.IsFalse(metadata.Immediate);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.2")]
        public void OmitDefaultPolicy()
        {
            // Arrange
            var property = typeof(ThrottledLb).GetProperty(nameof(ThrottledLb.Plain))!;

            // Act
            var metadata = ReportedPolicyOf(property, property);

            // Assert — every member carries a policy, so reporting the default one would say nothing.
            Assert.IsNull(metadata);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.4")]
        [DynamicData(nameof(SplitSourcePolicies))]
        public void ReportPolicyGateApplies(Type implementation, string? expectedInterval)
        {
            // Arrange — the interface declares 1s / 0.1; what the implementation declares beside it is
            // what decides, exactly as it does for the gate.
            var schemaSource = typeof(IThrottleViaInterface).GetProperty(nameof(IThrottleViaInterface.Reading))!;
            var presentationSource = implementation.GetProperty("Reading")!;

            // Act
            var metadata = ReportedPolicyOf(schemaSource, presentationSource);

            // Assert
            Assert.AreEqual(expectedInterval, metadata?.MinInterval);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.2")]
        public void OmitDefaultIntervalWrittenWithoutUnit()
        {
            // Arrange
            var property = typeof(ThrottledLb).GetProperty(nameof(ThrottledLb.DefaultSpelledBare))!;

            // Act
            var pm = PropertyMetadataBuilder.Build(property,
                                                   new PrimitiveTypeRef(PrimitiveKind.Double),
                                                   ImmutableDictionary<string, TypeAnnotations>.Empty,
                                                   ServiceElementStream.Property);

            // Assert
            Assert.IsNull(pm.Runtime.Throttle);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.5")]
        public void OmitEmptyDeadbandOnOtherwiseDefaultPolicy()
        {
            // Arrange
            var property = typeof(ThrottledLb).GetProperty(nameof(ThrottledLb.EmptyDeadband))!;

            // Act
            var pm = PropertyMetadataBuilder.Build(property,
                                                   new PrimitiveTypeRef(PrimitiveKind.Double),
                                                   ImmutableDictionary<string, TypeAnnotations>.Empty,
                                                   ServiceElementStream.Property);

            // Assert
            Assert.IsNull(pm.Runtime.Throttle);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.5")]
        public void ReportEmptyDeadbandAsNoneOnThrottledMember()
        {
            // Arrange
            var property = typeof(ThrottledLb).GetProperty(nameof(ThrottledLb.EmptyDeadbandOnAThrottledMember))!;

            // Act
            var pm = PropertyMetadataBuilder.Build(property,
                                                   new PrimitiveTypeRef(PrimitiveKind.Double),
                                                   ImmutableDictionary<string, TypeAnnotations>.Empty,
                                                   ServiceElementStream.Property);

            // Assert
            Assert.IsNotNull(pm.Runtime.Throttle);
            Assert.AreEqual("1s", pm.Runtime.Throttle!.MinInterval);
            Assert.IsNull(pm.Runtime.Throttle!.MinChange);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.3")]
        public void CarryEffectiveIntervalAlongsideImmediate()
        {
            // Arrange — the member sets Immediate and nothing else, so its interval is the default.
            var property = typeof(ThrottledLb).GetProperty(nameof(ThrottledLb.Pulse))!;

            // Act
            var metadata = ReportedPolicyOf(property, property);

            // Assert — the interval is carried even though it is the default, so a consumer renders the
            // badge without knowing what the default is.
            Assert.IsNotNull(metadata);
            Assert.IsTrue(metadata!.Immediate);
            Assert.AreEqual("250ms", metadata.MinInterval);
        }

        public static IEnumerable<object?[]> SplitSourcePolicies()
        {
            return new[]
                   {
                       // No attribute on the implementation, so the interface's knobs are reported.
                       new object?[] { typeof(ThrottleInheritedLb), "1s" },

                       // The implementation declares its own, which wins.
                       new object?[] { typeof(ThrottleImplOverrideLb), "2s" },

                       // The implementation declares a bare one: it owns the policy, and the default policy
                       // is not reported — the interface's knobs must not leak past it.
                       new object?[] { typeof(ThrottleImplBareLb), null },
                   };
        }

        private static ThrottleMetadata? ReportedPolicyOf(PropertyInfo schemaSource, PropertyInfo presentationSource)
        {
            return PropertyMetadataBuilder.BuildSplit(schemaSource,
                                                      presentationSource,
                                                      new PrimitiveTypeRef(PrimitiveKind.Double),
                                                      ImmutableDictionary<string, TypeAnnotations>.Empty,
                                                      ServiceElementStream.Property)
                                          .Runtime.Throttle;
        }

        private sealed class VisibleWhenDirectLb : LogicBlockBase
        {
            [ServiceProperty]
            public bool DirectMeasurement { get; set; }

            [ServiceProperty]
            [Presentation(Group = PropertyGroup.Configuration, VisibleWhen = "DirectMeasurement == false")]
            public double PrimaryCurrentToWriteA { get; set; }

            public VisibleWhenDirectLb() : base(new Mock<ILogger>().Object)
            {
            }

            protected override void Ready()
            {
            }

            protected override void Starting()
            {
            }
        }

        private sealed class ThrottledLb : LogicBlockBase
        {
            [ServiceProperty(MinInterval = "1s", MinChange = "0.1")]
            public double Voltage { get; private set; }

            [ServiceProperty]
            public double Plain { get; private set; }

            [ServiceProperty(Immediate = true)]
            public double Pulse { get; private set; }

            // The default interval written without its unit — a bare number is milliseconds.
            [ServiceProperty(MinInterval = "250")]
            public double DefaultSpelledBare { get; private set; }

            // An empty deadband: the gate reads it as unset, so the reported policy must too.
            [ServiceProperty(MinChange = "")]
            public double EmptyDeadband { get; private set; }

            [ServiceProperty(MinInterval = "1s", MinChange = "")]
            public double EmptyDeadbandOnAThrottledMember { get; private set; }

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

    // A dual-annotated member ([ServiceProperty] + [ServiceMeasuringPoint]) carrying a VisibleWhen predicate.
    public class DualAnnotatedVisibilityLb : LogicBlockBase
    {
        [ServiceProperty]
        public bool Enabled { get; set; }

        [ServiceProperty]
        [ServiceMeasuringPoint]
        [Presentation(VisibleWhen = "Enabled == true")]
        public double Power { get; private set; }

        public DualAnnotatedVisibilityLb() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    // One member, two streams, two different policies — plus a second member whose measuring point
    // declares no knobs beside a property that declares a slow interval.
    public class DualAnnotatedThrottleLb : LogicBlockBase
    {
        [ServiceProperty(MinInterval = "2s")]
        [ServiceMeasuringPoint(MinInterval = "500ms", MinChange = "1")]
        public double Power { get; private set; }

        [ServiceProperty(MinInterval = "2s")]
        [ServiceMeasuringPoint]
        public double Reading { get; private set; }

        public DualAnnotatedThrottleLb() : base(new Mock<ILogger>().Object)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
        }
    }

    [TestClass]
    public class LogicBlockIntrospectionOrderingShould
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-003.3")]
        public void EmitPropertiesInBaseToDerivedOrder()
        {
            // Arrange
            var block = new DerivedSortLb();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            var service = result.Services.Single();
            var propIds = service.Properties.Select(p => p.Identifier).ToList();

            // BaseSortLb declares BaseProp1, BaseProp2; DerivedSortLb adds DerivedProp1.
            // Expected order: base-class properties first (in declaration order), then derived.

            // Assert
            CollectionAssert.AreEqual(new[] { "BaseProp1", "BaseProp2", "DerivedProp1" }, propIds, $"Got order: {string.Join(", ", propIds)}");
        }

        [TestMethod]
        [TestProperty("spec", "AC-INTRO-006.1")]
        [TestProperty("spec", "AC-INTRO-006.4")]
        public void EmitVisibleWhenIntoBothDocumentsOfDualAnnotatedMember()
        {
            // Arrange
            // A member annotated with both [ServiceProperty] and [ServiceMeasuringPoint] rides the same
            // presentation node, so the predicate lands in BOTH sibling docs — the editor row and the
            // chart row hide coherently (spec §4).

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(new DualAnnotatedVisibilityLb(), _serviceProvider);
            var service = result.Services.Single();

            var property = service.Properties.Single(p => p.Identifier == "Power");

            // Assert
            Assert.AreEqual("Enabled == true", property.Presentation?["visibleWhen"]?.GetValue<string>());

            var measuringPoint = service.MeasuringPoints.Single(m => m.Identifier == "Power");
            Assert.AreEqual("Enabled == true", measuringPoint.Presentation?["visibleWhen"]?.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.1")]
        public void EmitThrottleNodeForInterfaceInheritedPolicy()
        {
            // Arrange — the knobs live on the [ServiceInterface] and the implementing property is bare.

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(new ThrottleInheritedLb(), _serviceProvider);

            // Assert — end to end, on the JSON a dashboard reads rather than the document it is built from.
            var throttle = result.Services.Single().Properties.Single(p => p.Identifier == "Reading").Runtime!["throttle"];
            Assert.IsNotNull(throttle);
            Assert.AreEqual("1s", throttle!["minInterval"]!.GetValue<string>());
            Assert.AreEqual("0.1", throttle["minChange"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.4")]
        public void EmitEachStreamsOwnThrottleNodeForDualAnnotatedMember()
        {
            // Arrange / Act
            var service = LogicBlockIntrospection.IntrospectLogicBlock(new DualAnnotatedThrottleLb(), _serviceProvider).Services.Single();

            // Assert
            var propertyThrottle = service.Properties.Single(p => p.Identifier == "Power").Runtime!["throttle"];
            var measuringPointThrottle = service.MeasuringPoints.Single(m => m.Identifier == "Power").Runtime!["throttle"];
            Assert.AreEqual("2s", propertyThrottle!["minInterval"]!.GetValue<string>());
            Assert.IsNull(propertyThrottle["minChange"]);
            Assert.AreEqual("500ms", measuringPointThrottle!["minInterval"]!.GetValue<string>());
            Assert.AreEqual("1", measuringPointThrottle["minChange"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-013.6")]
        public void OmitUnsetFieldsOfReportedPolicy()
        {
            // Arrange / Act
            var service = LogicBlockIntrospection.IntrospectLogicBlock(new DualAnnotatedThrottleLb(), _serviceProvider).Services.Single();

            // Assert — the member declares an interval and nothing else, so the node carries nothing else.
            var throttle = service.Properties.Single(p => p.Identifier == "Power").Runtime!["throttle"]!;
            Assert.AreEqual("2s", throttle["minInterval"]!.GetValue<string>());
            Assert.IsNull(throttle["minChange"]);
            Assert.IsNull(throttle["immediate"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.2")]
        public void OmitThrottleNodeOfStreamDeclaringNoKnobs()
        {
            // Arrange / Act
            var service = LogicBlockIntrospection.IntrospectLogicBlock(new DualAnnotatedThrottleLb(), _serviceProvider).Services.Single();

            // Assert
            Assert.AreEqual("2s", service.Properties.Single(p => p.Identifier == "Reading").Runtime!["throttle"]!["minInterval"]!.GetValue<string>());
            Assert.IsNull(service.MeasuringPoints.Single(m => m.Identifier == "Reading").Runtime?["throttle"]);
        }
    }
}