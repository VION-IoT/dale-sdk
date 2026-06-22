using System;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class ChangeThresholdAdapterShould
    {
        [TestMethod]
        public void UnboxBoxedValuesAndDelegateTrue()
        {
            IChangeThresholdAdapter adapter = new ChangeThresholdAdapter<double>(new DoubleChangeThreshold());
            object last = 10.0;
            object candidate = 12.0;
            Assert.IsTrue(adapter.Exceeds(last, candidate, "2"));
        }

        [TestMethod]
        public void UnboxBoxedValuesAndDelegateFalse()
        {
            IChangeThresholdAdapter adapter = new ChangeThresholdAdapter<double>(new DoubleChangeThreshold());
            object last = 10.0;
            object candidate = 11.0;
            Assert.IsFalse(adapter.Exceeds(last, candidate, "2"));
        }

        [TestMethod]
        public void DelegateForTimeSpanThroughAdapter()
        {
            IChangeThresholdAdapter adapter = new ChangeThresholdAdapter<TimeSpan>(new TimeSpanChangeThreshold());
            object last = TimeSpan.FromSeconds(1);
            object candidate = TimeSpan.FromSeconds(3);
            Assert.IsTrue(adapter.Exceeds(last, candidate, "2s"));
        }
    }
}
