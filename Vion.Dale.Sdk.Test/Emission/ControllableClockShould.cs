using System;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    /// <summary>
    ///     What makes a clock controllable, and therefore what turns the emission policy off by default.
    ///     The probe is structural rather than nominal so the SDK needs no reference to the test-only
    ///     clock package: any clock a test can wind forward is recognised by the method it offers.
    /// </summary>
    [TestClass]
    public class ControllableClockShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-001.4")]
        public void RecogniseClockOfferingAdvance()
        {
            // Arrange / Act / Assert
            Assert.IsTrue(ControllableClock.Detect(new FakeTimeProvider()));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-001.4")]
        public void RefuseClockOfferingNothingToAdvanceIt()
        {
            // Arrange / Act / Assert
            Assert.IsFalse(ControllableClock.Detect(TimeProvider.System));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-001.4")]
        public void RefuseClockOfferingAnotherMethodToAdvanceIt()
        {
            // Arrange — it moves, but not through the method the probe names.
            var moveable = new MoveableClock();

            // Act / Assert
            Assert.IsFalse(ControllableClock.Detect(moveable));
        }

        private sealed class MoveableClock : TimeProvider
        {
            public void MoveOn(TimeSpan delta)
            {
            }
        }
    }
}