using System;
using System.Threading;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     A block that refuses a write from inside its setter. The service binder's set runs inside the actor,
    ///     so the throw is caught by the middleware and the block never replies — the hollow acknowledgement a
    ///     write's safety window exists to catch. Nothing else in the repository produces one.
    /// </summary>
    [LogicBlock(Name = "Rejecting write")]
    public class RejectingWriteBlock : LogicBlockBase
    {
        /// <summary>The message the setter throws with.</summary>
        public const string RefusalMessage = "this block refuses this write";

        private int _rejected;

        [ServiceProperty(Title = "Rejected")]
        public int Rejected
        {
            get => _rejected;

            set
            {
                // The first publish (the initial state at start) must succeed, or the block never comes up and
                // the test would be measuring a failed start instead of a rejected write.
                if (value != 0)
                {
                    throw new InvalidOperationException(RefusalMessage);
                }

                _rejected = value;
            }
        }

        public RejectingWriteBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     A block whose timer handler occupies the actor for far longer than a short quiescence budget, so the
    ///     stepper's exact predicate (every mailbox drained AND no handler in flight) cannot hold while the
    ///     handler runs. The one shape that reaches the stepper's never-settles backstop without waiting out
    ///     its default.
    /// </summary>
    [LogicBlock(Name = "Slow handler")]
    public class SlowHandlerBlock : LogicBlockBase
    {
        [ServiceMeasuringPoint(Title = "Ticks")]
        public int Ticks { get; private set; }

        public SlowHandlerBlock(ILogger logger) : base(logger)
        {
        }

        [Timer(1)]
        public void OnTick()
        {
            // Deliberately blocking, not awaited: the in-flight monitor counts a handler that is executing, and
            // this is what "executing" has to look like for the barrier to keep waiting.
            Thread.Sleep(TimeSpan.FromSeconds(3));
            Ticks++;
        }

        protected override void Ready()
        {
        }
    }

    /// <summary>
    ///     A block whose stop hook blocks for far longer than a short stop-sequence budget. Every wait in the
    ///     teardown sequence goes through the registered clock, so on a stepped host only the real-time
    ///     backstop can end a teardown this block is part of.
    /// </summary>
    [LogicBlock(Name = "Slow stopping")]
    public sealed class SlowStoppingBlock : LogicBlockBase
    {
        [ServiceProperty(Title = "Value")]
        public int Value { get; set; }

        public SlowStoppingBlock(ILogger logger) : base(logger)
        {
        }

        protected override void Ready()
        {
        }

        protected override void Stopping()
        {
            Thread.Sleep(TimeSpan.FromSeconds(30));
        }
    }
}