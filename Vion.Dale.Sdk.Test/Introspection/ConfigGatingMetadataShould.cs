using System;
using System.Linq;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Vion.Contracts.Conventions;
using Vion.Contracts.Introspection;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Introspection
{
    /// <summary>
    ///     What config-time gating puts on the wire: a gated member's predicate and an
    ///     `[InstantiationParameter]`'s schema and runtime nodes (<c>docs/specs/config-gating.md</c>). The
    ///     shapes asserted here are the ones a packed artifact carries to the cloud.
    /// </summary>
    [TestClass]
    public sealed class ConfigGatingMetadataShould
    {
        private readonly IServiceProvider _serviceProvider = new ServiceCollection().AddSingleton<ILoggerFactory>(NullLoggerFactory.Instance)
                                                                                    .AddSingleton(typeof(ILogger<>), typeof(NullLogger<>))
                                                                                    .BuildServiceProvider();

        [TestMethod]
        [TestProperty("spec", "AC-GATE-010.1")]
        public void ReportComponentServiceGate()
        {
            // Arrange
            var block = new GatedCountBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.AreEqual("PointCount >= 2", result.Services.Single(service => service.Identifier == "Point2").IncludedWhen);
            Assert.IsNull(result.Services.Single(service => service.Identifier == nameof(GatedCountBlock.Point1)).IncludedWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-010.2")]
        public void ReportContractBindingGateAsAnnotation()
        {
            // Arrange
            var block = new GatedContractBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var contract = result.Contracts.Single(binding => binding.Identifier == nameof(GatedContractBlock.Point2Output));
            Assert.AreEqual("PointCount >= 2", contract.Annotations[LogicBlockWiringConventions.IncludedWhenAnnotationKey]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-010.2")]
        public void ReportInterfaceBindingGateAsAnnotation()
        {
            // Arrange
            var block = new GatedInterfaceBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var binding = result.Interfaces.Single(iface => iface.Identifier.StartsWith(nameof(GatedInterfaceBlock.Probe), StringComparison.Ordinal));
            Assert.AreEqual("Count >= 2", binding.Annotations[LogicBlockWiringConventions.IncludedWhenAnnotationKey]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-010.3")]
        [TestProperty("spec", "AC-GATE-010.6")]
        public void ReportInstantiationParameterAsReadOnlyWithItsBounds()
        {
            // The parameter deliberately carries a public setter so the platform can apply the configured
            // value; the forced wire flag is what stops the dashboard offering it as runtime state.

            // Arrange
            var block = new GatedCountBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var schema = RootProperty(result, nameof(GatedCountBlock), nameof(GatedCountBlock.PointCount)).Schema!;
            Assert.IsTrue(schema["readOnly"]!.GetValue<bool>());
            Assert.AreEqual(1.0, schema["minimum"]!.GetValue<double>());
            Assert.AreEqual(3.0, schema["maximum"]!.GetValue<double>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-010.4")]
        public void ReportInstantiationParameterMarkerAndDeclaredDefault()
        {
            // Arrange
            var block = new GatedCountBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var runtime = RootProperty(result, nameof(GatedCountBlock), nameof(GatedCountBlock.PointCount)).Runtime!;
            Assert.IsTrue(runtime["instantiationParameter"]!.GetValue<bool>());
            Assert.AreEqual(1L, runtime["default"]!.GetValue<long>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-010.4")]
        public void ReportEnumParameterDefaultAsMemberName()
        {
            // The same JSON-scalar form a gate's evaluation context uses, so a client resolving gates against
            // the reported default compares against what the predicate names.

            // Arrange
            var block = new ParameterTypesBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var runtime = RootProperty(result, nameof(ParameterTypesBlock), nameof(ParameterTypesBlock.Model)).Runtime!;
            Assert.AreEqual(nameof(StationModel.Bricco), runtime["default"]!.GetValue<string>());
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-010.5")]
        public void ReportNullDefaultForParameterDeclaredWithout()
        {
            // Arrange
            var block = new ParameterTypesBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var runtime = RootProperty(result, nameof(ParameterTypesBlock), nameof(ParameterTypesBlock.Reserve)).Runtime!;
            Assert.IsTrue(runtime["instantiationParameter"]!.GetValue<bool>());
            Assert.IsNull(runtime["default"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-007.8")]
        public void ReportGatedInterfaceBindingHoldingNull()
        {
            // The mirror of a null service-bearing component, which the definition view omits because its
            // members are read off the instance. An endpoint's identity is the property name and the
            // interface type, both known without one — and a client that cannot see the endpoint cannot see
            // the gate that removes it either.

            // Arrange
            var block = new NullInterfaceComponentBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var binding = result.Interfaces.Single(iface => iface.Identifier.StartsWith(nameof(NullInterfaceComponentBlock.Probe), StringComparison.Ordinal));
            Assert.AreEqual("Count >= 2", binding.Annotations[LogicBlockWiringConventions.IncludedWhenAnnotationKey]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.9")]
        public void ReportEmptyGateOnComponentService()
        {
            // Arrange
            var block = new EmptyGateBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            Assert.AreEqual(string.Empty, result.Services.Single(service => service.Identifier == nameof(EmptyGateBlock.Point2)).IncludedWhen);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.9")]
        public void ReportEmptyGateOnContractBinding()
        {
            // Arrange
            var block = new EmptyGateBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var contract = result.Contracts.Single(binding => binding.Identifier == nameof(EmptyGateBlock.Point2Output));
            Assert.AreEqual(string.Empty, contract.Annotations[LogicBlockWiringConventions.IncludedWhenAnnotationKey]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-005.9")]
        public void ReportEmptyGateOnInterfaceBinding()
        {
            // Arrange
            var block = new EmptyGateBlock();

            // Act
            var result = LogicBlockIntrospection.IntrospectLogicBlock(block, _serviceProvider);

            // Assert
            var binding = result.Interfaces.Single(iface => iface.Identifier.StartsWith(nameof(EmptyGateBlock.Probe), StringComparison.Ordinal));
            Assert.AreEqual(string.Empty, binding.Annotations[LogicBlockWiringConventions.IncludedWhenAnnotationKey]);
        }

        private static LogicBlockIntrospectionResult.ServicePropertyInfo RootProperty(LogicBlockIntrospectionResult result, string serviceIdentifier, string propertyIdentifier)
        {
            return result.Services.Single(service => service.Identifier == serviceIdentifier).Properties.Single(property => property.Identifier == propertyIdentifier);
        }
    }
}