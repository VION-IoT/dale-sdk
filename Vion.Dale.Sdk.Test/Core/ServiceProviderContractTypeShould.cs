using System.Reflection;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.DigitalIo.Output;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class ServiceProviderContractTypeShould
    {
        [TestMethod]
        public void DefaultConsumersToZeroOrMore()
        {
            var attr = new ServiceProviderContractTypeAttribute("Sample");

            Assert.AreEqual(LinkMultiplicity.ZeroOrMore, attr.Consumers);
        }

        [TestMethod]
        public void CapDigitalOutputConsumersToZeroOrOne()
        {
            var attr = typeof(IDigitalOutput).GetCustomAttribute<ServiceProviderContractTypeAttribute>()!;

            Assert.AreEqual(LinkMultiplicity.ZeroOrOne, attr.Consumers);
        }

        [TestMethod]
        public void CapAnalogOutputConsumersToZeroOrOne()
        {
            var attr = typeof(IAnalogOutput).GetCustomAttribute<ServiceProviderContractTypeAttribute>()!;

            Assert.AreEqual(LinkMultiplicity.ZeroOrOne, attr.Consumers);
        }
    }
}
