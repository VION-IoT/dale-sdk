using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class NumericChangeThresholdsShould
    {
        [TestMethod]
        public void FloatExceedsAtThreshold()
        {
            IChangeThreshold<float> threshold = new FloatChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(1.0f, 1.5f, "0.5"));
            Assert.IsFalse(threshold.Exceeds(1.0f, 1.25f, "0.5"));
        }

        [TestMethod]
        public void FloatExceedsRegardlessOfSignOfDelta()
        {
            IChangeThreshold<float> threshold = new FloatChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(1.5f, 1.0f, "0.5"));
        }

        [TestMethod]
        public void DecimalExceedsAtThreshold()
        {
            IChangeThreshold<decimal> threshold = new DecimalChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(100m, 90m, "10"));
            Assert.IsFalse(threshold.Exceeds(100m, 95m, "10"));
        }

        [TestMethod]
        public void DecimalExceedsRegardlessOfSignOfDelta()
        {
            IChangeThreshold<decimal> threshold = new DecimalChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(90m, 80m, "10"));
        }

        [TestMethod]
        public void Int32ExceedsAtThreshold()
        {
            IChangeThreshold<int> threshold = new Int32ChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(10, 15, "5"));
            Assert.IsFalse(threshold.Exceeds(10, 13, "5"));
        }

        [TestMethod]
        public void Int32ExceedsRegardlessOfSignOfDelta()
        {
            IChangeThreshold<int> threshold = new Int32ChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(15, 10, "5"));
        }

        [TestMethod]
        public void Int64ExceedsAtThreshold()
        {
            IChangeThreshold<long> threshold = new Int64ChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(1_000L, 1_100L, "100"));
            Assert.IsFalse(threshold.Exceeds(1_000L, 1_050L, "100"));
        }

        [TestMethod]
        public void Int64ExceedsRegardlessOfSignOfDelta()
        {
            IChangeThreshold<long> threshold = new Int64ChangeThreshold();
            Assert.IsTrue(threshold.Exceeds(1_100L, 1_000L, "100"));
        }

        [TestMethod]
        public void Int64ExceedsAcrossOppositeSignExtremes()
        {
            // long.MinValue → long.MaxValue delta overflows Int64 arithmetic; double delta is safe.
            Assert.IsTrue(new Int64ChangeThreshold().Exceeds(long.MinValue, long.MaxValue, "1"));
        }
    }
}