using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class LogicBlockInterfaceBindingShould
    {
        private interface ISample
        {
        }

        [TestMethod]
        public void DefaultMultiplicityToZeroOrMore()
        {
            var attr = new LogicBlockInterfaceBindingAttribute(typeof(ISample));

            Assert.AreEqual(LinkMultiplicity.ZeroOrMore, attr.Multiplicity);
        }

        [TestMethod]
        public void CarryExplicitMultiplicity()
        {
            var attr = new LogicBlockInterfaceBindingAttribute(typeof(ISample))
                       {
                           Multiplicity = LinkMultiplicity.ExactlyOne,
                       };

            Assert.AreEqual(LinkMultiplicity.ExactlyOne, attr.Multiplicity);
        }
    }
}
