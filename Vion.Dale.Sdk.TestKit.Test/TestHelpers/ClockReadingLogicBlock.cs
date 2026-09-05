using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Time.Testing;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test.TestHelpers
{
    /// <summary>
    ///     Records the virtual instant each of its scheduled actions observed, which is what makes "the
    ///     clock was set to this action's own deadline" observable from inside the action.
    /// </summary>
    public class ClockReadingLogicBlock : LogicBlockBase
    {
        private readonly TimeProvider _timeProvider;

        public List<DateTime> ObservedInstants { get; } = [];

        public ClockReadingLogicBlock(ILogger logger, TimeProvider timeProvider) : base(logger)
        {
            _timeProvider = timeProvider;
        }

        public void ScheduleClockRead(TimeSpan delay)
        {
            InvokeSynchronizedAfter(() => ObservedInstants.Add(_timeProvider.GetUtcNow().UtcDateTime), delay);
        }

        /// <summary>
        ///     Schedules an action that consumes virtual time of its own, the way the Modbus TCP fake's
        ///     response delay does. When it consumes more than the advance asked for, the clock ends up past
        ///     the target the advance would otherwise land on.
        /// </summary>
        public void ScheduleClockConsumer(TimeSpan delay, TimeSpan consumed)
        {
            InvokeSynchronizedAfter(() => ((FakeTimeProvider)_timeProvider).Advance(consumed), delay);
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     Schedules an action that re-enters the driver that dispatched it, which is the shape both
    ///     reentrancy guards refuse.
    /// </summary>
    public class ReentrantLogicBlock : LogicBlockBase
    {
        public ReentrantLogicBlock(ILogger logger) : base(logger)
        {
        }

        public void ScheduleReentrantAdvance(LogicBlockTestContext<ReentrantLogicBlock> context)
        {
            InvokeSynchronized(() => context.AdvanceTime(TimeSpan.FromSeconds(1)));
        }

        public void ScheduleReentrantFlush(LogicBlockTestContext<ReentrantLogicBlock> context)
        {
            InvokeSynchronized(context.FlushPendingActions);
        }

        protected override void Ready()
        {
        }
    }
}