using System;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     A member carrying both <c>[ServiceProperty]</c> and <c>[ServiceMeasuringPoint]</c> publishes to
    ///     two streams, and each stream is gated from the knobs on its own attribute — the property's
    ///     knobs never reach the measuring point, and a measuring point declaring none falls to the
    ///     attribute defaults rather than borrowing the property's.
    /// </summary>
    [TestClass]
    public class DualAnnotatedEmissionShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.1")]
        public void ThrottleEachStreamOnItsOwnInterval()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<TwoIntervalBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Act
            context.AdvanceTime(TimeSpan.FromMilliseconds(250));
            block.SetPower(1.0);
            context.AdvanceTime(TimeSpan.FromMilliseconds(250));
            block.SetPower(2.0);

            // Assert
            context.VerifyServiceMeasuringPointEmitted(lb => lb.Power, times: Times.Exactly(2));
            context.VerifyServicePropertyEmitted(lb => lb.Power, times: Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.2")]
        public void ApplyDefaultIntervalToStreamDeclaringNoKnobs()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<BareMeasuringPointBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Act
            context.AdvanceTime(TimeSpan.FromMilliseconds(250));
            block.SetReading(1.0);

            // Assert
            context.VerifyServiceMeasuringPointEmitted(lb => lb.Reading, times: Times.Once());
            context.VerifyServicePropertyEmitted(lb => lb.Reading, times: Times.Never());
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.2")]
        public void KeepInterfacePropertyIntervalOffMeasuringPoint()
        {
            // Arrange — the interface assigns 5s to the property stream only; the implementation is bare.
            var block = LogicBlockTestHelper.Create<InterfaceDualBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Act
            context.AdvanceTime(TimeSpan.FromMilliseconds(250));
            block.SetLevel(1.0);
            context.AdvanceTime(TimeSpan.FromMilliseconds(250));
            block.SetLevel(2.0);

            // Assert — the measuring point runs at the SDK's 250 ms, not the property's 5 s.
            context.VerifyServiceMeasuringPointEmitted(lb => lb.Level, times: Times.Exactly(2));
        }

        // Both streams declared on the interface, only one of them with a knob.
        [ServiceInterface]
        public interface IDualLevelService
        {
            [ServiceProperty(MinInterval = "5s")]
            [ServiceMeasuringPoint]
            double Level { get; }
        }

        private sealed class InterfaceDualBlock : LogicBlockBase, IDualLevelService
        {
            public double Level { get; private set; }

            public InterfaceDualBlock(ILogger logger) : base(logger)
            {
            }

            public void SetLevel(double value)
            {
                Level = value;
            }

            protected override void Ready()
            {
            }
        }

        // One sensed value, two streams, two different intervals: the measuring point moves four times
        // faster than the property.
        private sealed class TwoIntervalBlock : LogicBlockBase
        {
            [ServiceProperty(MinInterval = "2s")]
            [ServiceMeasuringPoint(MinInterval = "250ms")]
            public double Power { get; private set; }

            public TwoIntervalBlock(ILogger logger) : base(logger)
            {
            }

            public void SetPower(double value)
            {
                Power = value;
            }

            protected override void Ready()
            {
            }
        }

        // The measuring point declares no knobs at all, next to a property that declares a slow interval.
        private sealed class BareMeasuringPointBlock : LogicBlockBase
        {
            [ServiceProperty(MinInterval = "2s")]
            [ServiceMeasuringPoint]
            public double Reading { get; private set; }

            public BareMeasuringPointBlock(ILogger logger) : base(logger)
            {
            }

            public void SetReading(double value)
            {
                Reading = value;
            }

            protected override void Ready()
            {
            }
        }
    }
}