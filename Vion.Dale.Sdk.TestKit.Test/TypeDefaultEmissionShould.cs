using System;
using Microsoft.Extensions.Logging;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    // A summary whose counter moves on every update, declaring its own slow default the way the SDK's
    // diagnostics summaries do. Every value assigned below differs, so the dedup floor never holds one back
    // and the interval is the only brake.
    [DefaultMinInterval("30s")]
    public readonly record struct RunStatistics(long Cycles);

#pragma warning disable DALE036 // "fast" is not a duration: the type's default must fail the block's start.
    [DefaultMinInterval("fast")]
    public readonly record struct UnreadableStatistics(long Cycles);
#pragma warning restore DALE036

    [ServiceInterface]
    public interface IRunStatisticsService
    {
        [ServiceProperty(MinInterval = "5s")]
        RunStatistics Reported { get; set; }
    }

    /// <summary>
    ///     A value type that declares <see cref="DefaultMinIntervalAttribute" /> sets the interval of a member no
    ///     attribute assigns one to, on either stream and through a nullable; an interval assigned on the member or
    ///     on its interface wins over it. Every case assigns a new value every 20 ms for a minute of virtual time —
    ///     4 a second at the SDK's 250 ms, one per 30 s under the type's default.
    /// </summary>
    [TestClass]
    public class TypeDefaultEmissionShould
    {
        private const int AssignmentsInOneMinute = 3000;

        private static readonly TimeSpan AssignEvery = TimeSpan.FromMilliseconds(20);

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.6")]
        public void TakeValueTypeIntervalWhenNoneAssigned()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<RunStatisticsBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Act
            for (var cycle = 1; cycle <= AssignmentsInOneMinute; cycle++)
            {
                block.Statistics = new RunStatistics(cycle);
                context.AdvanceTime(AssignEvery);
            }

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Statistics, times: Times.Exactly(2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.6")]
        public void TakeUnderlyingTypeIntervalForNullableMember()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<RunStatisticsBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Act
            for (var cycle = 1; cycle <= AssignmentsInOneMinute; cycle++)
            {
                block.MaybeStatistics = new RunStatistics(cycle);
                context.AdvanceTime(AssignEvery);
            }

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.MaybeStatistics, times: Times.Exactly(2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.6")]
        public void TakeValueTypeIntervalForMeasuringPoint()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<RunStatisticsBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Act
            for (var cycle = 1; cycle <= AssignmentsInOneMinute; cycle++)
            {
                block.SetSampled(new RunStatistics(cycle));
                context.AdvanceTime(AssignEvery);
            }

            // Assert
            context.VerifyServiceMeasuringPointEmitted(lb => lb.Sampled, times: Times.Exactly(2));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.6")]
        public void TakeInterfaceIntervalOverValueTypeDefault()
        {
            // Arrange — the implementing property is bare; the interface assigns 5s, not the type's 30s.
            var block = LogicBlockTestHelper.Create<InterfaceRunStatisticsBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Act
            for (var cycle = 1; cycle <= AssignmentsInOneMinute; cycle++)
            {
                block.Reported = new RunStatistics(cycle);
                context.AdvanceTime(AssignEvery);
            }

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.Reported, times: Times.Exactly(12));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-002.7")]
        public void KeepAssignedSdkIntervalOverValueTypeDefault()
        {
            // Arrange — the member assigns 250ms, the value the SDK would use anyway.
            var block = LogicBlockTestHelper.Create<RunStatisticsBlock>();
            var context = block.CreateTestContext().WithEmissionPolicy(EmissionPolicyMode.FromAttributes).Build();

            // Act
            for (var cycle = 1; cycle <= AssignmentsInOneMinute; cycle++)
            {
                block.FastStatistics = new RunStatistics(cycle);
                context.AdvanceTime(AssignEvery);
            }

            // Assert
            context.VerifyServicePropertyEmitted(lb => lb.FastStatistics, times: Times.Exactly(240));
        }

        [TestMethod]
        [TestProperty("spec", "AC-EMIT-003.3")]
        [TestProperty("spec", "AC-EMIT-003.5")]
        public void RefuseToStartWhenValueTypeIntervalNotDuration()
        {
            // Arrange
            var block = LogicBlockTestHelper.Create<UnreadableStatisticsBlock>();

            // Act / Assert — the member assigns nothing, so the type's unreadable interval is the one the gate
            // would use.
            var rejection = Assert.ThrowsExactly<FormatException>(() => block.CreateTestContext().Build());
            StringAssert.Contains(rejection.Message, nameof(UnreadableStatisticsBlock.Statistics));
            StringAssert.Contains(rejection.Message, nameof(UnreadableStatisticsBlock));
            StringAssert.Contains(rejection.Message, "fast");
            StringAssert.Contains(rejection.Message, $"'{nameof(UnreadableStatistics)}'");
        }

        private sealed class RunStatisticsBlock : LogicBlockBase
        {
            [ServiceProperty]
            public RunStatistics Statistics { get; set; }

            [ServiceProperty]
            public RunStatistics? MaybeStatistics { get; set; }

            [ServiceProperty(MinInterval = "250ms")]
            public RunStatistics FastStatistics { get; set; }

            [ServiceMeasuringPoint]
            public RunStatistics Sampled { get; private set; }

            public RunStatisticsBlock(ILogger logger) : base(logger)
            {
            }

            public void SetSampled(RunStatistics value)
            {
                Sampled = value;
            }

            protected override void Ready()
            {
            }
        }

        private sealed class InterfaceRunStatisticsBlock : LogicBlockBase, IRunStatisticsService
        {
            public InterfaceRunStatisticsBlock(ILogger logger) : base(logger)
            {
            }

            public RunStatistics Reported { get; set; }

            protected override void Ready()
            {
            }
        }

        private sealed class UnreadableStatisticsBlock : LogicBlockBase
        {
            [ServiceProperty]
            public UnreadableStatistics Statistics { get; set; }

            public UnreadableStatisticsBlock(ILogger logger) : base(logger)
            {
            }

            protected override void Ready()
            {
            }
        }
    }
}