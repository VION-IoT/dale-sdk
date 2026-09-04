using System;
using System.Linq;
using System.Reflection;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Test.TestHelpers;

namespace Vion.Dale.Sdk.Test.Core
{
    /// <summary>
    ///     One multiplicity vocabulary across both binding attributes and the contract type, and the default
    ///     that keeps an unannotated declaration unconstrained. The SDK declares and never enforces, so what
    ///     is pinned here is the vocabulary and the defaults rather than any effect on a link.
    /// </summary>
    [TestClass]
    public class LinkMultiplicityShould
    {
        private static System.Collections.Generic.IEnumerable<object[]> UnannotatedDeclarations
        {
            get
            {
                yield return [new LogicBlockInterfaceBindingAttribute(typeof(IBindSink)).Multiplicity];
                yield return [new ServiceProviderContractBindingAttribute().Multiplicity];
                yield return [new ServiceProviderContractTypeAttribute("Sample").Consumers];
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-014.1")]
        public void OfferOneVocabularyOfFourValues()
        {
            // Arrange / Act
            var names = Enum.GetNames<LinkMultiplicity>().OrderBy(name => name);

            // Assert
            CollectionAssert.AreEqual(new[] { "ExactlyOne", "OneOrMore", "ZeroOrMore", "ZeroOrOne" }, names.ToList());
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-014.2")]
        [DynamicData(nameof(UnannotatedDeclarations))]
        public void LeaveUnannotatedDeclarationUnconstrained(LinkMultiplicity declared)
        {
            // Arrange / Act / Assert
            Assert.AreEqual(LinkMultiplicity.ZeroOrMore, declared);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-014.1")]
        public void CarryConsumerSideMultiplicityOfInterfaceBinding()
        {
            // Arrange / Act
            var binding = new LogicBlockInterfaceBindingAttribute(typeof(IBindSink)) { Multiplicity = LinkMultiplicity.ExactlyOne };

            // Assert
            Assert.AreEqual(LinkMultiplicity.ExactlyOne, binding.Multiplicity);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-014.1")]
        public void CarryConsumerSideMultiplicityOfContractBinding()
        {
            // Arrange / Act
            var binding = typeof(BindContractBlock).GetProperty(nameof(BindContractBlock.Probe))!.GetCustomAttribute<ServiceProviderContractBindingAttribute>();

            // Assert
            Assert.IsNotNull(binding);
            Assert.AreEqual(LinkMultiplicity.ExactlyOne, binding.Multiplicity);
        }

        [TestMethod]
        [TestProperty("spec", "AC-BIND-014.1")]
        [DataRow(typeof(IDigitalOutput), DisplayName = "a digital output accepts one writer")]
        [DataRow(typeof(IAnalogOutput), DisplayName = "an analog output accepts one writer")]
        public void CarryProviderSideAcceptanceOfContractType(Type contractType)
        {
            // Arrange / Act
            var declaration = contractType.GetCustomAttribute<ServiceProviderContractTypeAttribute>();

            // Assert
            Assert.IsNotNull(declaration);
            Assert.AreEqual(LinkMultiplicity.ZeroOrOne, declaration.Consumers);
        }
    }
}