using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.Core
{
    [TestClass]
    public class ServiceMeasuringPointAttributeShould
    {
        [TestMethod]
        public void RoundTripDescription()
        {
            var attr = new ServiceMeasuringPointAttribute { Description = "Energy imported per bucket" };
            Assert.AreEqual("Energy imported per bucket", attr.Description);
        }

        [TestMethod]
        public void DefaultDescriptionToNull()
        {
            var attr = new ServiceMeasuringPointAttribute();
            Assert.IsNull(attr.Description);
        }

        [TestMethod]
        public void RoundTripKind()
        {
            var attr = new ServiceMeasuringPointAttribute { Kind = MeasuringPointKind.TotalIncreasing };
            Assert.AreEqual(MeasuringPointKind.TotalIncreasing, attr.Kind);
        }

        [TestMethod]
        public void DefaultKindToMeasurement()
        {
            var attr = new ServiceMeasuringPointAttribute();
            Assert.AreEqual(MeasuringPointKind.Measurement, attr.Kind);
        }
    }
}
