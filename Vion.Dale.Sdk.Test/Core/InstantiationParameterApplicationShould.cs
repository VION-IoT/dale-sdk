using System;
using System.Linq;
using System.Text.Json.Nodes;
using Vion.Contracts.Codec;
using Vion.Contracts.Events.CloudToMesh;
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
        private static readonly string[] StationServices = [nameof(GatedCountBlock), nameof(GatedCountBlock.Point1), "Point2", "Point3"];

        private static readonly string[] TypeServices = [nameof(ParameterTypesBlock)];

        [TestMethod]
        [TestProperty("spec", "AC-GATE-001.1")]
        public void ApplyValuesBeforeGatesResolve()
        {
            // The ordering is the whole point: the binders read these properties, so applying after Configure
            // would resolve every gate against the declared default instead of the configured value.

            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, StationServices, Parameter(nameof(GatedCountBlock.PointCount), JsonValue.Create(2L)));

            // Assert
            Assert.AreEqual(2, block.PointCount);
            CollectionAssert.Contains(harness.BoundServices().ToArray(), "Point2");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-001.2")]
        public void KeepDeclaredDefaultWhenNoValueSupplied()
        {
            // Arrange
            var block = new GatedCountBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, StationServices);

            // Assert
            Assert.AreEqual(1, block.PointCount);
            CollectionAssert.DoesNotContain(harness.BoundServices().ToArray(), "Point2");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-001.3")]
        public void ApplyLastOfRepeatedIdentifiers()
        {
            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block,
                              TypeServices,
                              Parameter(nameof(ParameterTypesBlock.PointCount), JsonValue.Create(3L)),
                              Parameter(nameof(ParameterTypesBlock.PointCount), JsonValue.Create(2L)));

            // Assert
            Assert.AreEqual(2, block.PointCount);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-001.4")]
        public void ApplyValuesWhateverAccessorShape()
        {
            // init is what the docs recommend and what the first consumer writes, but reflection does not
            // enforce it — a plain public setter carries a parameter just as well.

            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block,
                              TypeServices,
                              Parameter(nameof(ParameterTypesBlock.PointCount), JsonValue.Create(2L)),
                              Parameter(nameof(ParameterTypesBlock.Stage), JsonValue.Create(4L)));

            // Assert
            Assert.AreEqual(2, block.PointCount);
            Assert.AreEqual(4, block.Stage);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-002.1")]
        public void RefuseIdentifierThatNamesNoParameter()
        {
            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block, TypeServices, Parameter("Nonexistent", JsonValue.Create(1L))));

            StringAssert.Contains(failure.Message, "Nonexistent");
            StringAssert.Contains(failure.Message, typeof(ParameterTypesBlock).FullName!);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-002.2")]
        public void RefuseIdentifierSpelledInAnotherCase()
        {
            // Parameter identifiers are the cloud's translation keys, so two spellings must not name one
            // property.

            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block, TypeServices, Parameter("pointcount", JsonValue.Create(2L))));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-002.3")]
        public void NameIdentifierBlockAndReasonWhenValueWillNotDecode()
        {
            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block,
                                                                                                  TypeServices,
                                                                                                  Parameter(nameof(ParameterTypesBlock.PointCount), JsonValue.Create("two"))));

            StringAssert.Contains(failure.Message, nameof(ParameterTypesBlock.PointCount));
            StringAssert.Contains(failure.Message, typeof(ParameterTypesBlock).FullName!);
            StringAssert.Contains(failure.Message, "could not be decoded");
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-002.4")]
        [TestProperty("spec", "AC-GATE-002.5")]
        [TestProperty("spec", "AC-GATE-002.6")]
        [DataRow(nameof(ParameterTypesBlock.PointCount), 2.5, DisplayName = "a fractional number for an integer")]
        [DataRow(nameof(ParameterTypesBlock.PointCount), "2", DisplayName = "a numeric string for an integer")]
        [DataRow(nameof(ParameterTypesBlock.PointCount), 1L << 40, DisplayName = "an integer past the property type's range")]
        [DataRow(nameof(ParameterTypesBlock.Model), 1L, DisplayName = "an ordinal for an enum")]
        [DataRow(nameof(ParameterTypesBlock.Model), "Nonexistent", DisplayName = "an unknown enum member name")]
        [DataRow(nameof(ParameterTypesBlock.Region), 1L, DisplayName = "a number for a string")]
        public void RefuseValueOutsideSharedScalarGrammar(string identifier, object value)
        {
            // One encoding across cloud, dashboard, topology file and TestKit. A decoder that tolerated any
            // of these rows is where those four start to disagree.

            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block, TypeServices, Parameter(identifier, JsonValue.Create(value))));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-002.7")]
        public void RefuseNullForNonNullableParameter()
        {
            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act / Assert
            Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block, TypeServices, Parameter(nameof(ParameterTypesBlock.PointCount), null)));
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-002.7")]
        public void ApplyNullForNullableParameter()
        {
            // The half that makes a fail-closed gate over a null parameter reachable rather than theoretical.

            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, TypeServices, Parameter(nameof(ParameterTypesBlock.Region), null));

            // Assert
            Assert.IsNull(block.Region);
        }

        [TestMethod]
        [TestProperty("spec", "AC-GATE-010.6")]
        public void ApplyValueOutsideDeclaredBounds()
        {
            // Minimum and Maximum are presentation everywhere in this SDK, and a structural parameter is no
            // exception — the editor renders the range, the block does not police it.

            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act
            harness.Configure(block, TypeServices, Parameter(nameof(ParameterTypesBlock.PointCount), JsonValue.Create(99L)));

            // Assert
            Assert.AreEqual(99, block.PointCount);
        }

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
        [TestProperty("spec", "AC-GATE-002.9")]
        public void CarrySingleDecodeFailureBesideUnresolvedIdentifier()
        {
            // Two failures, one of them a decode — the decode rule that refused the value is still the only
            // actionable detail, and it is what an operator needs past the message text.

            // Arrange
            var block = new ParameterTypesBlock();
            var harness = new GatingHarness();

            // Act / Assert
            var failure = Assert.ThrowsExactly<InvalidOperationException>(() => harness.Configure(block,
                                                                                                  TypeServices,
                                                                                                  Parameter("Nonexistent", JsonValue.Create(1L)),
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