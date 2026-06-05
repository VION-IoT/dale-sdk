using System;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test
{
    /// <summary>
    ///     A logic block whose Ready() schedules a tick that re-schedules itself via
    ///     <see cref="LogicBlockBase.InvokeSynchronizedAfter" /> — the production pattern for a
    ///     periodic tick implemented without [Timer]. Exists to repro the unbounded
    ///     FlushPendingActions drain.
    /// </summary>
    public class SelfReschedulingLogicBlock : LogicBlockBase
    {
        private static readonly TimeSpan TickInterval = TimeSpan.FromSeconds(5);

        // volatile so a cooperative shutdown flag set from the test thread is visible on the
        // background drain thread, even if the buggy multi-pass loop is hot.
        private volatile bool _stopRescheduling;

        public bool StopRescheduling
        {
            get => _stopRescheduling;

            set => _stopRescheduling = value;
        }

        // Only the background thread mutates TickCount during the flush; the test thread reads
        // it only after Task.Wait, which provides the necessary memory barrier.
        public int TickCount { get; private set; }

        public SelfReschedulingLogicBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
            ScheduleNextTick();
        }

        private void OnTick()
        {
            TickCount++;
            if (!_stopRescheduling)
            {
                ScheduleNextTick();
            }
        }

        private void ScheduleNextTick()
        {
            InvokeSynchronizedAfter(OnTick, TickInterval);
        }
    }
}