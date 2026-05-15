using System.Reflection;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    public interface ITestDigitalOutput
    {
    }

    public class TestLb
    {
        [ServiceProviderContractBinding(Identifier = "Button")]
        public ITestDigitalOutput Button { get; set; } = null!;

        [ServiceProviderContractBinding(
            Identifier = "LED",
            DefaultName = "Status-LED",
            Multiplicity = LinkMultiplicity.ZeroOrOne,
            Tags = new[] { "output", "indicator" })]
        public ITestDigitalOutput Led { get; set; } = null!;

        [ServiceProviderContractBinding]
        public ITestDigitalOutput Defaulted { get; set; } = null!;
    }

    [TestClass]
    public class ServiceProviderContractBindingShould
    {
        [TestMethod]
        public void CarryIdentifier()
        {
            var attr = typeof(TestLb).GetProperty(nameof(TestLb.Button))!
                .GetCustomAttribute<ServiceProviderContractBindingAttribute>();
            Assert.IsNotNull(attr);
            Assert.AreEqual("Button", attr.Identifier);
        }

        [TestMethod]
        public void CarryAllNamedArguments()
        {
            var attr = typeof(TestLb).GetProperty(nameof(TestLb.Led))!
                .GetCustomAttribute<ServiceProviderContractBindingAttribute>()!;
            Assert.AreEqual("LED", attr.Identifier);
            Assert.AreEqual("Status-LED", attr.DefaultName);
            Assert.AreEqual(LinkMultiplicity.ZeroOrOne, attr.Multiplicity);
            Assert.HasCount(2, attr.Tags);
        }

        [TestMethod]
        public void DefaultMultiplicityToZeroOrMore()
        {
            var attr = typeof(TestLb).GetProperty(nameof(TestLb.Defaulted))!
                .GetCustomAttribute<ServiceProviderContractBindingAttribute>()!;
            Assert.AreEqual(LinkMultiplicity.ZeroOrMore, attr.Multiplicity);
            Assert.HasCount(0, attr.Tags);
        }
    }
}
