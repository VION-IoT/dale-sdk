using System;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class ControllableClockShould
    {
        [TestMethod]
        public void DetectFakeTimeProviderAsControllable()
        {
            // FakeTimeProvider exposes a public instance Advance(TimeSpan) returning void.
            Assert.IsTrue(ControllableClock.Detect(new FakeTimeProvider()));
        }

        [TestMethod]
        public void NotDetectTheSystemClockAsControllable()
        {
            Assert.IsFalse(ControllableClock.Detect(TimeProvider.System));
        }
    }
}
