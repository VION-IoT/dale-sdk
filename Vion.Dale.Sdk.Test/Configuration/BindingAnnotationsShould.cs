using System.Collections.Generic;
using System.Linq;
using Vion.Contracts.Conventions;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Introspection;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Configuration
{
    /// <summary>
    ///     Which of a binding's declared values reach the introspection annotations. What the keys mean and
    ///     how the document reports them is <c>docs/specs/introspection.md</c>'s; this suite pins which values
    ///     a binding puts there, on both metadata bags, since the two families carry the same four rules.
    /// </summary>
    [TestClass]
    public class BindingAnnotationsShould
    {
        private static IEnumerable<object[]> EmptyMetaData
        {
            get
            {
                yield return [new ContractMetaData { DefaultName = string.Empty, Tags = [] }];
                yield return [new FunctionInterfaceMetaData { DefaultName = string.Empty, Tags = [] }];
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-013.1")]
        [DynamicData(nameof(EmptyMetaData))]
        public void OmitEmptyDefaultNameAndTags(object metaData)
        {
            // Arrange / Act
            var annotations = AnnotationsOf(metaData);

            // Assert
            Assert.IsEmpty(annotations);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-013.1")]
        public void EmitDeclaredDefaultNameAndTags()
        {
            // Arrange
            var metaData = new ContractMetaData { DefaultName = "The probe", Tags = ["io"] };

            // Act
            var annotations = metaData.Annotations;

            // Assert
            Assert.AreEqual("The probe", annotations[nameof(ContractMetaData.DefaultName)]);
            CollectionAssert.AreEqual(new[] { "io" }, (List<string>)annotations[nameof(ContractMetaData.Tags)]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-013.2")]
        [DataRow(LinkMultiplicity.ExactlyOne, LogicBlockWiringConventions.ExactlyOne, DisplayName = "required and single")]
        [DataRow(LinkMultiplicity.ZeroOrOne, LogicBlockWiringConventions.ZeroOrOne, DisplayName = "optional and single")]
        [DataRow(LinkMultiplicity.OneOrMore, LogicBlockWiringConventions.OneOrMore, DisplayName = "required and many")]
        public void EmitNonDefaultMultiplicityAsSharedToken(LinkMultiplicity multiplicity, string expectedToken)
        {
            // Arrange
            var metaData = new FunctionInterfaceMetaData { Multiplicity = multiplicity };

            // Act
            var annotations = metaData.Annotations;

            // Assert
            Assert.AreEqual(expectedToken, annotations[LogicBlockWiringConventions.MultiplicityAnnotationKey]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-013.2")]
        public void OmitUnconstrainedMultiplicity()
        {
            // Arrange
            var metaData = new FunctionInterfaceMetaData { Multiplicity = LinkMultiplicity.ZeroOrMore };

            // Act
            var annotations = metaData.Annotations;

            // Assert
            Assert.IsFalse(annotations.ContainsKey(LogicBlockWiringConventions.MultiplicityAnnotationKey));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-013.3")]
        [DataRow("Mode == \"on\"", DisplayName = "a declared predicate")]
        [DataRow("", DisplayName = "a declared but empty predicate")]
        public void EmitDeclaredInclusionPredicate(string includedWhen)
        {
            // Arrange
            var metaData = new ContractMetaData { IncludedWhen = includedWhen };

            // Act
            var annotations = metaData.Annotations;

            // Assert
            Assert.AreEqual(includedWhen, annotations[LogicBlockWiringConventions.IncludedWhenAnnotationKey]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-013.3")]
        public void OmitInclusionPredicateOfUngatedBinding()
        {
            // Arrange
            var metaData = new ContractMetaData { IncludedWhen = null };

            // Act
            var annotations = metaData.Annotations;

            // Assert
            Assert.IsFalse(annotations.ContainsKey(LogicBlockWiringConventions.IncludedWhenAnnotationKey));
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-013.4")]
        public void EmitHandlerActorNameOfEveryContract()
        {
            // Arrange
            var block = new BindContractBlock();

            // Act
            var definition = LogicBlockIntrospection.IntrospectLogicBlock(block, BindHosts.Bare);

            // Assert
            var contract = definition.Contracts.Single();
            Assert.AreEqual(nameof(BindProbeHandler), contract.Annotations[ServiceProviderContractAnnotations.ContractHandlerActorName]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-013.4")]
        public void EmitDevelopmentOnlyFlagOnlyForDevelopmentContract()
        {
            // Arrange
            var block = new BindProviderFaceBlock();

            // Act
            var definition = LogicBlockIntrospection.IntrospectLogicBlock(block, BindHosts.Bare);

            // Assert
            var face = definition.Contracts.Single(contract => contract.Identifier == BindProviderFaceBlock.FaceIdentifier);
            Assert.IsTrue((bool)face.Annotations[ServiceProviderContractAnnotations.DevelopmentOnly]);
            var ordinary = definition.Contracts.Single(contract => contract.Identifier == BindProviderFaceBlock.ProbeIdentifier);
            Assert.IsFalse(ordinary.Annotations.ContainsKey(ServiceProviderContractAnnotations.DevelopmentOnly));
        }

        private static Dictionary<string, object> AnnotationsOf(object metaData)
        {
            return metaData switch
            {
                ContractMetaData contract => contract.Annotations,
                FunctionInterfaceMetaData endpoint => endpoint.Annotations,
                _ => throw new System.ArgumentOutOfRangeException(nameof(metaData)),
            };
        }
    }
}