using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The teardown evidence sink for <see cref="TeardownStopSequenceShould" />. Owned by the test, never
    ///     by the per-block DI scope — the scope is disposed as part of the very teardown under test, so a
    ///     sink living inside it would be gone before the assertions run.
    ///     <para>
    ///         Doubles as an <see cref="IActorMessageObserver" />: DevHost combines every registered observer
    ///         (alongside its own <c>MessageTap</c>), so registering one is how a test reads the actual
    ///         teardown message order off the real actor system rather than inferring it.
    ///     </para>
    /// </summary>
    public sealed class TeardownRecorder : IActorMessageObserver
    {
        public const string ScopeDisposed = "scope-disposed";

        public const string Stopping = "Stopping";

        private readonly ConcurrentQueue<string> _entries = new();

        private readonly TaskCompletionSource _stoppingRecorded = new(TaskCreationOptions.RunContinuationsAsynchronously);

        /// <summary>The recorded entries, oldest first.</summary>
        public IReadOnlyList<string> Entries
        {
            get => _entries.ToArray();
        }

        /// <summary>
        ///     Completes once <see cref="Stopping" /> has been recorded. A teardown the test does not drive
        ///     itself — the supervised loop's recycle — runs on another thread after the reset is acknowledged,
        ///     so this is the only signal that generation's blocks were actually stopped; the status endpoint
        ///     the old generation keeps answering cannot say so.
        /// </summary>
        public Task StoppingRecorded
        {
            get => _stoppingRecorded.Task;
        }

        public void OnReceived(string actorName, object message)
        {
            // Only the two teardown messages, and only for logic block actors — the mock handlers see them
            // too, and the ordering claim is about what reaches the block.
            if (message is StopLogicBlockRequest or GetPersistentDataSnapshotRequest)
            {
                Record($"{message.GetType().Name}@{actorName}");
            }
        }

        public void OnHandled(string actorName, object message, TimeSpan elapsed, Exception? exception)
        {
        }

        public void Record(string entry)
        {
            _entries.Enqueue(entry);

            if (entry == Stopping)
            {
                _stoppingRecorded.TrySetResult();
            }
        }
    }

    /// <summary>
    ///     A scoped dependency of <see cref="StoppingBlock" />, so its <see cref="Dispose" /> marks the moment
    ///     the block actor's per-actor DI scope is torn down — i.e. the actor has actually terminated. That is
    ///     the "before the actors are terminated" half of the ordering assertion.
    ///     <para>
    ///         It records only for the scope of a block that actually started. Introspection resolves a second
    ///         <see cref="StoppingBlock" /> from the root provider (<c>DevHostIntrospection</c>) whose scope is
    ///         the root container's, disposed later with the service provider; relying on which of the two came
    ///         first would make the ordering assertion hostage to <c>DevHost.DisposeAsync</c>'s internal order.
    ///     </para>
    /// </summary>
    public sealed class ScopeProbe : IDisposable
    {
        private readonly TeardownRecorder _recorder;

        private bool _live;

        public ScopeProbe(TeardownRecorder recorder)
        {
            _recorder = recorder;
        }

        public void Dispose()
        {
            if (_live)
            {
                _recorder.Record(TeardownRecorder.ScopeDisposed);
            }
        }

        /// <summary>Called from the owning block's <c>Starting()</c>, which only an actor ever receives.</summary>
        public void MarkLive()
        {
            _live = true;
        }
    }

    /// <summary>
    ///     The first DevHost test block to override <see cref="Stopping" /> — the hook that teardown never used
    ///     to reach. It records that it ran, and writes a service property from inside the hook so the test can
    ///     also check whether that late write is still delivered.
    /// </summary>
    [LogicBlock(Name = "Stopper")]
    public class StoppingBlock : LogicBlockBase
    {
        public const string StoppedStatus = "stopped";

        private readonly TeardownRecorder _recorder;

        private readonly ScopeProbe _scopeProbe;

        [ServiceProperty(Title = "Status")]
        public string Status { get; set; } = "running";

        public StoppingBlock(ILogger logger, TeardownRecorder recorder, ScopeProbe scopeProbe) : base(logger)
        {
            _recorder = recorder;
            _scopeProbe = scopeProbe;
        }

        protected override void Ready()
        {
        }

        protected override void Starting()
        {
            // Only an actor is ever started, so this is what distinguishes this instance's scope from the one
            // introspection resolves out of the root provider.
            _scopeProbe.MarkLive();
        }

        protected override void Stopping()
        {
            _recorder.Record(TeardownRecorder.Stopping);

            // A write issued from the stop hook. It rides the same message handler, so the publish it raises
            // is enqueued before the actor is terminated. A best-effort *contract* write is the harder case —
            // it needs a grace period between Stopping() and teardown, which is VION-65's subject, not this
            // block's.
            Status = StoppedStatus;
        }
    }
}