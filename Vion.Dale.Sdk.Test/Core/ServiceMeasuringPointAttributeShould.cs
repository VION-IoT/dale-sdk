using System;
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

        [TestMethod]
        public void MirrorCanonicalContractsMeasuringPointKindExactly()
        {
            // The SDK-Core enum is a mirror of the canonical wire enum in Vion.Contracts.TypeRef.
            // PropertyMetadataBuilder casts SDK->contracts by integer value, so the member
            // name->value maps must stay byte-identical or the x-kind wire token drifts.
            var sdkNames = Enum.GetNames<Vion.Dale.Sdk.Core.MeasuringPointKind>();
            var contractNames = Enum.GetNames<Vion.Contracts.TypeRef.MeasuringPointKind>();

            CollectionAssert.AreEquivalent(contractNames, sdkNames);

            foreach (var name in contractNames)
            {
                var sdkValue = (int)Enum.Parse<Vion.Dale.Sdk.Core.MeasuringPointKind>(name);
                var contractValue = (int)Enum.Parse<Vion.Contracts.TypeRef.MeasuringPointKind>(name);
                Assert.AreEqual(contractValue, sdkValue, $"Value drift for member '{name}'");
            }
        }

        [TestMethod]
        public void UseSdkCoreMeasuringPointKindForKindProperty()
        {
            var kindProperty = typeof(ServiceMeasuringPointAttribute).GetProperty("Kind");
            Assert.IsNotNull(kindProperty);
            Assert.AreEqual(typeof(Vion.Dale.Sdk.Core.MeasuringPointKind), kindProperty!.PropertyType);
        }
    }
}
