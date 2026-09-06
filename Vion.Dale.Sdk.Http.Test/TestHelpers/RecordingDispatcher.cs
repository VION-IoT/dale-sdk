using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Http.Test.TestHelpers
{
    /// <summary>
    ///     A dispatcher that queues what the package hands it and runs it only when the test drains — the
    ///     honest shape of the self-send a real block gets (`LogicBlockBase.InvokeSynchronized` sends the
    ///     action to the actor and returns). Running a callback inline instead puts it inside the package's
    ///     own catch, which reads the opposite of production for anything a callback throws.
    /// </summary>
    internal sealed class RecordingDispatcher : IActorDispatcher
    {
        private readonly List<Action> _queued = new();

        /// <summary>How many actions the package has scheduled and the test has not yet drained.</summary>
        public int QueuedCount
        {
            get => _queued.Count;
        }

        /// <summary>What escaped the last drain, or <c>null</c> when nothing did.</summary>
        public Exception? DrainFailure { get; private set; }

        public void InvokeSynchronized(Action action)
        {
            _queued.Add(action);
        }

        public void InvokeSynchronizedAfter(Action action, TimeSpan delay)
        {
            _queued.Add(action);
        }

        /// <summary>
        ///     Runs everything queued, on the test's thread, the way the actor would. An exception an action
        ///     throws lands in <see cref="DrainFailure" /> rather than the test, because on a real block it
        ///     lands in the actor and not in the package.
        /// </summary>
        public int Drain()
        {
            var drained = _queued.Count;
            foreach (var action in _queued.ToArray())
            {
                try
                {
                    action();
                }
                catch (Exception exception)
                {
                    DrainFailure = exception;
                }
            }

            _queued.Clear();

            return drained;
        }
    }

    /// <summary>
    ///     Stands in for a block that has not received its first message: `LogicBlockBase` refuses the
    ///     self-send from `RequireActorContext` with this exact shape, and the package's own catch is what
    ///     turns that refusal into a callback nobody runs.
    /// </summary>
    internal sealed class UnstartedBlockDispatcher : IActorDispatcher
    {
        internal const string RefusalMessage = "InvokeSynchronized was called before the logic block received its first message, so there is no actor to schedule the action on. " +
                                               "Schedule from Ready() or Starting() instead of from the constructor.";

        public void InvokeSynchronized(Action action)
        {
            throw new InvalidOperationException(RefusalMessage);
        }

        public void InvokeSynchronizedAfter(Action action, TimeSpan delay)
        {
            throw new InvalidOperationException(RefusalMessage);
        }
    }
}