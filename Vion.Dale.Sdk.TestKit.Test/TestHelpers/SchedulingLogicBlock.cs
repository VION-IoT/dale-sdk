using System;
using System.Collections.Generic;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit.Test.TestHelpers
{
    /// <summary>
    ///     A block whose only job is to put actions on the test context's queue at chosen deadlines and
    ///     record which of them ran. The recorded tags are what makes "the action never ran" and "the
    ///     action ran once" different observations rather than the same silence.
    /// </summary>
    public class SchedulingLogicBlock : LogicBlockBase
    {
        public SchedulingLogicBlock(ILogger logger) : base(logger)
        {
        }

        /// <summary>The tags of the actions that ran, in the order they ran.</summary>
        public List<string> Ran { get; } = [];

        public void Schedule(string tag, TimeSpan delay)
        {
            InvokeSynchronizedAfter(() => Ran.Add(tag), delay);
        }

        public void ScheduleImmediate(string tag)
        {
            InvokeSynchronized(() => Ran.Add(tag));
        }

        public void ScheduleThrowing(string tag, TimeSpan delay)
        {
            InvokeSynchronizedAfter(() =>
                                    {
                                        Ran.Add(tag);
                                        throw new InvalidOperationException("the scheduled action failed");
                                    },
                                    delay);
        }

        /// <summary>Schedules an action that itself schedules another, to exercise cascading.</summary>
        public void ScheduleCascading(string tag, TimeSpan delay, string nextTag, TimeSpan nextDelay)
        {
            InvokeSynchronizedAfter(() =>
                                    {
                                        Ran.Add(tag);
                                        Schedule(nextTag, nextDelay);
                                    },
                                    delay);
        }

        protected override void Ready()
        {
        }
    }
}
