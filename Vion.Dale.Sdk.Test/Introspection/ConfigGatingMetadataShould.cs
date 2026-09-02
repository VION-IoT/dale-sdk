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
    }
}
