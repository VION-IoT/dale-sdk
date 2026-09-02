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
    ///     The emission gate acting on <see cref="SensorBlock" />'s read-only readings. The gate is off by
    ///     default under the TestKit's clock, so every test forces it on with
    ///     <c>WithEmissionPolicy(EmissionPolicyMode.FromAttributes)</c>. Emission policy governs the
    ///     outbound direction, so the gated members are read-only: a test drives them by writing the plain
    ///     <see cref="SensorBlock.Setpoint" /> input, which is always forwarded, and running
    ///     <c>OnTick</c>. The value published at start is cleared by the builder, so the first change
    ///     below is a leading edge.
    /// </summary>
    public class SensorBlockShould
    {
        public SensorBlockShould()
        {
            _context = _block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();
        }

        private readonly SensorBlock _block = LogicBlockTestHelper.Create<SensorBlock>();

        private readonly LogicBlockTestContext<SensorBlock> _context;

        [Fact]
        public void EmitBothStreamsOfDualAnnotatedMember()
        {
            // Arrange / Act — ten ticks across ten virtual seconds.
            for (var tick = 0; tick < 10; tick++)
            {
                _block.OnTick();
                _context.AdvanceTime(TimeSpan.FromSeconds(1));
            }

            // Assert — one sensed value feeds both streams, and neither suppresses the other.
            var asProperty = _context.GetSentMessagesOfTypePublic<ServicePropertyValueChanged>().Count(m => m.PropertyIdentifier == nameof(SensorBlock.Power));
            var asMeasuringPoint = _context.GetSentMessagesOfTypePublic<ServiceMeasuringPointValueChanged>().Count(m => m.MeasuringPointIdentifier == nameof(SensorBlock.Power));
            Assert.True(asProperty > 0, $"the property stream must emit; got {asProperty}");
            Assert.True(asMeasuringPoint > 0, $"the measuring-point stream must emit; got {asMeasuringPoint}");

            // The 500 ms measuring point cannot yet emit MORE often than the 2 s property here: this
            // project references a published Vion.Dale.Sdk package, and in the packages released so far a
            // dual-annotated member's measuring point borrowed the property's knobs. That each stream
            // gates on its own attribute is proven in-repo by
            // Vion.Dale.Sdk.TestKit.Test/DualAnnotatedEmissionShould; tighten this to a strict > once the
            // version bump after that fix's release lands.
            Assert.True(asMeasuringPoint >= asProperty, $"got mp={asMeasuringPoint}, prop={asProperty}");
        }

        [Fact]
        public void EmitImmediateMemberOnEveryTickWhileThrottlingItsNeighbour()
        {
            // Arrange / Act — twelve ticks across three virtual seconds.
            for (var tick = 0; tick < 12; tick++)
            {
                _block.OnTick();
                _context.AdvanceTime(TimeSpan.FromMilliseconds(250));
            }

            // Assert — LiveTick sets Immediate, so every tick reaches the handler; Temperature is
            // throttled to 2 s with a deadband, so the same twelve ticks coalesce.
            _context.VerifyServiceMeasuringPointEmitted(lb => lb.LiveTick, times: Times.Exactly(12));
            _context.VerifyServiceMeasuringPointEmitted(lb => lb.Temperature, times: Times.AtMost(4));
        }

        [Fact]
        public void EmitPhaseCurrentsClearingTheirCustomDeadband()
        {
            // Arrange
            _block.Setpoint = 10.0;
            _block.OnTick();

            // Act — each phase moves 0.3.
            _block.Setpoint = 10.3;
            _block.OnTick();

            // Assert
            _context.VerifyServiceMeasuringPointEmitted(lb => lb.PhaseCurrents, times: Times.Exactly(2));
        }

        [Fact]
        public void EmitReadingClearingItsDeadband()
        {
            // Arrange
            _block.Setpoint = 26.0;
            _block.OnTick();

            // Act — a move of 0.8.
            _block.Setpoint = 26.8;
            _block.OnTick();

            // Assert
            _context.VerifyServiceMeasuringPointEmitted(lb => lb.Reading, times: Times.Exactly(2));
        }

        [Fact]
        public void SuppressPhaseCurrentsInsideTheirCustomDeadband()
        {
            // Arrange — the deadband for ThreePhase is declared in the example's own assembly.
            _block.Setpoint = 10.0;
            _block.OnTick();

            // Act — each phase moves 0.1 against a deadband of 0.25.
            _block.Setpoint = 10.1;
            _block.OnTick();

            // Assert
            _context.VerifyServiceMeasuringPointEmitted(lb => lb.PhaseCurrents, times: Times.Once());
        }

        [Fact]
        public void SuppressReadingInsideItsDeadband()
        {
            // Arrange
            _block.Setpoint = 26.0;
            _block.OnTick();

            // Act — a move of 0.2 against a deadband of 0.5.
            _block.Setpoint = 26.2;
            _block.OnTick();

            // Assert
            _context.VerifyServiceMeasuringPointEmitted(lb => lb.Reading, times: Times.Once());
        }

        [Fact]
        public void SuppressUnchangedReading()
        {
            // Arrange
            _block.Setpoint = 40.0;

            // Act — the setpoint does not move between ticks, so the second reading carries no news.
            _block.OnTick();
            _block.OnTick();

            // Assert
            _context.VerifyServiceMeasuringPointEmitted(lb => lb.Reading, times: Times.Once());
        }
    }
}