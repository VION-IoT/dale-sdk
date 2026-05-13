using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class ServicePropertyAttributeShould
    {
        [TestMethod]
        public void RoundTripDescription()
        {
            var attr = new ServicePropertyAttribute { Description = "Power flowing right now" };
            Assert.AreEqual("Power flowing right now", attr.Description);
        }

        [TestMethod]
        public void DefaultDescriptionToNull()
        {
            var attr = new ServicePropertyAttribute();
            Assert.IsNull(attr.Description);
        }

        [TestMethod]
        public void RoundTripWriteOnly()
        {
            var attr = new ServicePropertyAttribute { WriteOnly = true };
            Assert.IsTrue(attr.WriteOnly);
        }

        [TestMethod]
        public void DefaultWriteOnlyToFalse()
        {
            var attr = new ServicePropertyAttribute();
            Assert.IsFalse(attr.WriteOnly);
        }
    }
}
