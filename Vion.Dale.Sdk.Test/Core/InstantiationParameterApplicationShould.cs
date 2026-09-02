using System;
using System.Text.Json.Nodes;
using Vion.Contracts.Codec;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     How a configuration's <c>[InstantiationParameter]</c> values reach a block's properties, and what
    ///     the block does with a value it cannot use (<c>docs/specs/config-gating.md</c>). Fail-closed:
    ///     resolving inclusion gates against a value the operator did not choose is worse than not starting.
    /// </summary>
    [TestClass]
    public sealed class InstantiationParameterApplicationShould
    {
        private static readonly string[] TypeServices = [nameof(ParameterTypesBlock)];

        [TestMethod]
        [TestProperty("spec", "AC-GATE-002.8")]
        public void NameEveryFailingParameter()
        {
            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block,
                                                                                                  TypeServices,
                                                                                                  Parameter("Nonexistent", JsonValue.Create(1L)),
                                                                                                  Parameter(nameof(ParameterTypesBlock.Model), JsonValue.Create("Nonexistent")),
                                                                                                  Parameter(nameof(ParameterTypesBlock.PointCount), JsonValue.Create("two"))));

            StringAssert.Contains(failure.Message, "Nonexistent");
            StringAssert.Contains(failure.Message, nameof(ParameterTypesBlock.Model));
            StringAssert.Contains(failure.Message, nameof(ParameterTypesBlock.PointCount));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-002.9")]
        public void CarrySingleDecodeFailureAsInnerException()
        {
            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block,
                                                                                                  TypeServices,
                                                                                                  Parameter(nameof(ParameterTypesBlock.Model), JsonValue.Create("Nonexistent"))));

            Assert.IsInstanceOfType<PropertyValueDecodeException>(failure.InnerException);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-002.10")]
        public void ApplyNoValueWhenAnyParameterFails()
        {
            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block,
                                                                                    TypeServices,
                                                                                    Parameter(nameof(ParameterTypesBlock.PointCount), JsonValue.Create(3L)),
                                                                                    Parameter("Nonexistent", JsonValue.Create(1L))));

            Assert.AreEqual(1, block.PointCount);
        }

        private static SetLogicConfigurationPayload.InstantiationParameterValue Parameter(string identifier, JsonNode? value)
        {
            return new SetLogicConfigurationPayload.InstantiationParameterValue { Identifier = identifier, Value = value };
        }
    }
}
