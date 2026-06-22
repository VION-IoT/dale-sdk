using System;
using Vion.Dale.Sdk.Emission;

namespace Vion.Dale.Sdk.Test.Emission
{
    [TestClass]
    public class OfferResultShould
    {
        [TestMethod]
        public void ExposeEmitWithEmitAction()
        {
            var result = OfferResult.Emit;

            Assert.AreEqual(EmitAction.Emit, result.Action);
        }

        [TestMethod]
        public void ExposeDropWithDropAction()
        {
            var result = OfferResult.Drop;

            Assert.AreEqual(EmitAction.Drop, result.Action);
        }

        [TestMethod]
        public void CarryTheDeadlineForHold()
        {
            var deadline = new DateTimeOffset(2026,
                                              6,
                                              22,
                                              10,
                                              0,
                                              0,
                                              TimeSpan.Zero);

            var result = OfferResult.Hold(deadline);

            Assert.AreEqual(EmitAction.Hold, result.Action);
            Assert.AreEqual(deadline, result.Deadline);
        }
    }
}