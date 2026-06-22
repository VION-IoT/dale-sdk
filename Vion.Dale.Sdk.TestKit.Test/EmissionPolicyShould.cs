using System;
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

        [TestMethod]
        public void EmitLeadingEdgeThenHoldUnderForcedPolicy()
        {
            // Force policy ON (override the FakeTimeProvider clock-off). The first change emits
            // immediately; further changes inside MinInterval are held (no second emit yet).
            var block = LogicBlockTestHelper.Create<ThrottledBlock>();
            var ctx = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // The initial publish at start seeds the throttler (leading edge) at virtual T0, then is
            // cleared by the builder. Advance past the interval so the first user write below is a
            // fresh leading edge rather than a same-instant hold.
            ctx.AdvanceTime(TimeSpan.FromMilliseconds(250));

            block.Voltage = 1.0; // leading edge -> emit
            block.Voltage = 2.0; // within 250ms -> held
            block.Voltage = 3.0; // within 250ms -> held (latest wins)

            ctx.VerifyServicePropertyEmitted(lb => lb.Voltage, times: Times.Once());
        }
    }
}
