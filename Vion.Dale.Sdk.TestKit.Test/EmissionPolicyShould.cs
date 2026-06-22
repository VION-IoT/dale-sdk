using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    [TestClass]
    public class EmissionPolicyShould
    {
        // A block with a default-policy (250ms) throttled property.
        private sealed class ThrottledBlock : LogicBlockBase
        {
            [ServiceProperty(MinInterval = "250ms")]
            public double Voltage { get; set; }

            public ThrottledBlock(ILogger logger) : base(logger)
            {
            }

            protected override void Ready()
            {
            }
        }

        [TestMethod]
        public void NotThrottleUnderFakeClockWithoutOverride()
        {
            // TestKit hosts a FakeTimeProvider (controllable clock) and no marker => policy OFF.
            // Every distinct change must reach the handler as raw INPC (today's behaviour).
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var ctx = block.CreateTestContext().Build();

            block.Voltage = 1.0;
            block.Voltage = 2.0;
            block.Voltage = 3.0;

            ctx.VerifyServicePropertyChanged(lb => lb.Voltage, times: Times.Exactly(3));
        }
    }
}
