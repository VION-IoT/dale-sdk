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
    ///     Proves the RFC 0004 emission gate acts on <see cref="SensorBlock" />'s read-only readings. The TestKit's
    ///     <c>WithEmissionPolicy(EmissionPolicyMode.FromAttributes)</c> forces the policy on under the fake clock
    ///     (it is off by default for deterministic tests). The emission policy governs the OUTBOUND direction, so
    ///     the gated members are read-only; the tests drive them by writing the plain <see cref="SensorBlock.Setpoint" />
    ///     input (always forwarded) and running <c>OnTick</c>, then count post-gate emissions. The seed value is
    ///     uncounted; the first distinct post-seed change is the leading-edge emit.
    /// </summary>
    public class SensorBlockShould
    {
        private static LogicBlockTestContext<SensorBlock> WithPolicyOn(out SensorBlock block)
        {
            block = LogicBlockTestHelper.Create<SensorBlock>();
            return block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();
        }

        [Fact]
        public void DropAnUnchangedReadingViaTheDedupFloor()
        {
            var ctx = WithPolicyOn(out var block);

            block.Setpoint = 40.0;
            block.OnTick(); // Reading = 40 -> emit (1)
            block.OnTick(); // Reading = 40 again (setpoint unchanged) -> dedup floor drops it

            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.Reading, times: Times.Once());
        }

        [Fact]
        public void DropThePhaseCurrentsBelowTheCustomDeadband()
        {
            var ctx = WithPolicyOn(out var block);

            block.Setpoint = 10.0;
            block.OnTick(); // PhaseCurrents = (10,10,10) -> emit (1)
            block.Setpoint = 10.1;
            block.OnTick(); // each phase Δ0.1 < 0.25 -> dropped by the custom threshold

            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.PhaseCurrents, times: Times.Once());
        }

        [Fact]
        public void DropTheReadingWhenTheSetpointMovesBelowTheDeadband()
        {
            var ctx = WithPolicyOn(out var block);

            block.Setpoint = 26.0;
            block.OnTick(); // Reading = 26 -> leading-edge emit
            block.Setpoint = 26.2;
            block.OnTick(); // Reading = 26.2, Δ vs last-emitted 26 = 0.2 < 0.5 -> dropped

            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.Reading, times: Times.Once());
        }

        [Fact]
        public void EmitThePhaseCurrentsWhenClearingTheCustomDeadband()
        {
            var ctx = WithPolicyOn(out var block);

            block.Setpoint = 10.0;
            block.OnTick(); // emit (1)
            block.Setpoint = 10.3;
            block.OnTick(); // each phase Δ0.3 >= 0.25 -> emit (2)

            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.PhaseCurrents, times: Times.Exactly(2));
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
        public void EmitTheReadingWhenTheSetpointClearsTheDeadband()
        {
            var ctx = WithPolicyOn(out var block);

            block.Setpoint = 26.0;
            block.OnTick(); // emit (1)
            block.Setpoint = 26.8;
            block.OnTick(); // Δ0.8 >= 0.5 -> emit (2)

            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.Reading, times: Times.Exactly(2));
        }

        [Fact]
        public void ThrottleTheTemperatureWhileImmediateEmitsEveryTick()
        {
            var ctx = WithPolicyOn(out var block);

            // Drive 12 ticks across 3 virtual seconds (250 ms apart).
            for (var i = 0; i < 12; i++)
            {
                block.OnTick();
                ctx.AdvanceTime(TimeSpan.FromMilliseconds(250));
            }

            // LiveTick is Immediate -> every tick emits.
            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.LiveTick, times: Times.Exactly(12));

            // Temperature is throttled to 2 s (+ Δ0.5) -> the 12 ticks coalesce to far fewer emissions.
            ctx.VerifyServiceMeasuringPointEmitted(lb => lb.Temperature, times: Times.AtMost(4));
        }
    }
}