using Moq;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.TestKit.Test
{
    [TestClass]
    public class VerifyServicePropertyEmittedShould
    {
        [TestMethod]
        public void CountAndAssertEmittedValue_PolicyOff()
        {
            // With the policy Off, "emitted" == "changed": one assignment produces exactly one
            // ServicePropertyValueChanged carrying the assigned value. VerifyServicePropertyEmitted
            // reads the same ServicePropertyValueChanged stream as VerifyServicePropertyChanged but
            // its name documents that it observes POST-policy emissions.
            var block = LogicBlockTestHelper.Create<ThrottledLogicBlock>();
            var ctx = block.CreateTestContext().Build();

            block.Power = 3.5;

            ctx.VerifyServicePropertyEmitted(lb => lb.Power, value => Assert.AreEqual(3.5, value));
        }

        [TestMethod]
        public void DefaultToTimesOnce()
        {
            // Like VerifyServicePropertyChanged, the default Times is Once().
            var block = LogicBlockTestHelper.Create<ThrottledLogicBlock>();
            var ctx = block.CreateTestContext().Build();

            block.Power = 1.0;

            ctx.VerifyServicePropertyEmitted(lb => lb.Power);
        }

        [TestMethod]
        public void HonorTimesNever_WhenNoEmission()
        {
            var block = LogicBlockTestHelper.Create<ThrottledLogicBlock>();
            var ctx = block.CreateTestContext().Build();

            // No assignment → zero emissions.
            ctx.VerifyServicePropertyEmitted(lb => lb.Power, times: Times.Never());
        }

        [TestMethod]
        public void OnlyCountTargetProperty()
        {
            // Two distinct properties: each verified independently, no cross-counting.
            var block = LogicBlockTestHelper.Create<TwoPropertyLogicBlock>();
            var ctx = block.CreateTestContext().Build();

            block.Power = 3.5;
            block.Rate = 9.0;

            ctx.VerifyServicePropertyEmitted(lb => lb.Power, times: Times.Once());
            ctx.VerifyServicePropertyEmitted(lb => lb.Rate, times: Times.Once());
        }
    }
}