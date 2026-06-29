using System;
using System.Linq;
using Moq;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.TestKit;
using Vion.Examples.Emission.LogicBlocks;
using Xunit;

namespace Vion.Examples.Emission.Test
{
    /// <summary>
    ///     Proves the RFC 0004 emission gate acts on <see cref="SensorBlock" />'s declarations. The TestKit's
    ///     <c>WithEmissionPolicy(EmissionPolicyMode.FromAttributes)</c> forces the policy on under the fake clock
    ///     (it is off by default for deterministic tests), so <c>AdvanceTime</c> drives throttling and
    ///     <c>VerifyServicePropertyEmitted</c> counts post-gate emissions. The seed value is uncounted; the first
    ///     distinct post-seed change is the leading-edge emit.
    /// </summary>
    public class SensorBlockShould
    {
        private static LogicBlockTestContext<SensorBlock> WithPolicyOn(out SensorBlock block)
        {
            block = LogicBlockTestHelper.Create<SensorBlock>();
            return block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();
        }

        [Fact]
        public void DropAnEqualSetpointViaTheDedupFloor()
        {
            var ctx = WithPolicyOn(out var block);

            block.Setpoint = 40.0; // emit (1)
            block.Setpoint = 40.0; // equal -> always-on dedup floor drops it

            ctx.VerifyServicePropertyEmitted(lb => lb.Setpoint, times: Times.Once());
        }

        [Fact]
        public void DropCurrentChangesBelowTheCustomDeadband()
        {
            var ctx = WithPolicyOn(out var block);

            block.Current = new ThreePhase(10.0, 10.0, 10.0); // Δ vs seed (0,0,0) -> emit (1)
            block.Current = new ThreePhase(10.1, 10.0, 10.0); // Δ0.1 < 0.25 -> dropped

            ctx.VerifyServicePropertyEmitted(lb => lb.Current, times: Times.Once());
        }

        [Fact]
        public void DropSetpointChangesBelowTheDeadband()
        {
            var ctx = WithPolicyOn(out var block);

            block.Setpoint = 26.0; // Δ vs seed 25.0 = 1.0 >= 0.5 -> leading-edge emit
            block.Setpoint = 26.2; // Δ vs last-emitted 26.0 = 0.2 < 0.5 -> dropped

            ctx.VerifyServicePropertyEmitted(lb => lb.Setpoint, times: Times.Once());
        }

        [Fact]
        public void EmitCurrentChangesThatClearTheCustomDeadband()
        {
            var ctx = WithPolicyOn(out var block);

            block.Current = new ThreePhase(10.0, 10.0, 10.0); // emit (1)
            block.Current = new ThreePhase(10.3, 10.0, 10.0); // Δ0.3 >= 0.25 -> emit (2)

            ctx.VerifyServicePropertyEmitted(lb => lb.Current, times: Times.Exactly(2));
        }

        [Fact]
        public void EmitSetpointChangesThatClearTheDeadband()
        {
            var ctx = WithPolicyOn(out var block);

            block.Setpoint = 26.0; // emit (1)
            block.Setpoint = 26.8; // Δ0.8 >= 0.5 -> emit (2)

            ctx.VerifyServicePropertyEmitted(lb => lb.Setpoint, times: Times.Exactly(2));
        }

        [Fact]
        public void EmitThePowerStreamsIndependently()
        {
            var ctx = WithPolicyOn(out var block);

            // Drive 10 ticks across 10 virtual seconds. Power is dual-annotated: the property stream is
            // throttled to 2 s, the measuring-point stream to 500 ms (+ Δ1).
            for (var i = 0; i < 10; i++)
            {
                block.OnTick();
                ctx.AdvanceTime(TimeSpan.FromSeconds(1));
            }

            var propertyEmits = ctx.GetSentMessagesOfTypePublic<ServicePropertyValueChanged>().Count(m => m.PropertyIdentifier == nameof(SensorBlock.Power));
            var measuringPointEmits = ctx.GetSentMessagesOfTypePublic<ServiceMeasuringPointValueChanged>().Count(m => m.MeasuringPointIdentifier == nameof(SensorBlock.Power));

            // The #104 fix: a dual-annotated member feeds BOTH streams — neither suppresses the other.
            Assert.True(propertyEmits > 0, $"property stream should emit; got {propertyEmits}");
            Assert.True(measuringPointEmits > 0, $"measuring-point stream should emit; got {measuringPointEmits}");

            // The streams throttle independently: the faster 500 ms stream emits at least as often as the 2 s one.
            Assert.True(measuringPointEmits >= propertyEmits,
                        $"measuring-point (500 ms) should emit at least as often as property (2 s); got mp={measuringPointEmits}, prop={propertyEmits}");
        }

        [Fact]
        public void ThrottleTheEchoWhileImmediateEmitsEveryChange()
        {
            var ctx = WithPolicyOn(out var block);

            // Drive 12 distinct value changes across 3 virtual seconds (250 ms apart).
            for (var i = 0; i < 12; i++)
            {
                block.OnTick();
                ctx.AdvanceTime(TimeSpan.FromMilliseconds(250));
            }

            // LiveTick is Immediate -> every change emits.
            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.LiveTick, times: Times.Exactly(12));

            // ThrottledEcho is throttled to 3 s -> the 12 rapid changes coalesce to far fewer.
            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.ThrottledEcho, times: Times.AtMost(3));
        }
    }
}