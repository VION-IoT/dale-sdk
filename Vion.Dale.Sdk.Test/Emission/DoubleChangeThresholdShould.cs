using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class DoubleChangeThresholdShould
    {
        [TestMethod]
        public void ExceedWhenDeltaIsAtThreshold()
        {
            IChangeThreshold<double> threshold = new DoubleChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(10.0, 12.0, "2"));
        }

        [TestMethod]
        public void ExceedWhenDeltaIsAboveThreshold()
        {
            IChangeThreshold<double> threshold = new DoubleChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(10.0, 13.5, "2"));
        }

        [TestMethod]
        public void NotExceedWhenDeltaIsBelowThreshold()
        {
            IChangeThreshold<double> threshold = new DoubleChangeThreshold();
            Assert.IsFalse(threshold.Exceeds(10.0, 11.0, "2"));
        }

        [TestMethod]
        public void ExceedRegardlessOfSignOfDelta()
        {
            IChangeThreshold<double> threshold = new DoubleChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(10.0, 7.0, "2"));
        }
    }
}