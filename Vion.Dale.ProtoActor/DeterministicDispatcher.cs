using System;
using System.Threading;
using System.Threading.Tasks;
using Proto.Mailbox;

namespace Vion.Dale.ProtoActor
{
    /// <summary>
    ///     A serial, deterministic <see cref="IDispatcher" /> for stepped DevHosts (RFC 0008 deterministic
    ///     stepping). Every mailbox-processing run is scheduled onto one shared exclusive task scheduler, so no
    ///     two actor handlers ever run concurrently and the message cascade within a quiescence round drains in
    ///     a single, reproducible order — the cross-actor ordering Proto's default <c>ThreadPoolDispatcher</c>
    ///     (<c>Task.Run</c> per mailbox) leaves to the thread pool.
    ///     <para>
    ///         Used ONLY when a controllable clock (<c>FakeTimeProvider</c>) is registered. A free-running
    ///         Player or the production runtime keeps the thread-pool dispatcher, so this changes nothing
    ///         outside stepping. Serialization is safe because Dale handlers do not block on a cross-actor
    ///         await (they message-pass and use <c>ReenterAfter</c>), so the exclusive scheduler never has a
    ///         handler waiting on another handler it is preventing from running.
    ///     </para>
    ///     <para>
    ///         Deterministic ORDERING (not just serialization) additionally requires that the events which make
    ///         actors runnable arrive in a fixed order: handler-posted messages already do (handlers run one at
    ///         a time here), and same-virtual-instant timer fires are delivered in a fixed order by the
    ///         next-event stepper rather than by racing <c>Task.Delay</c> continuations (see
    ///         <c>IVirtualSchedule.RegisterDelivery</c> / <c>ActorContext.SendToSelfAfter</c>).
    ///     </para>
    /// </summary>
    internal sealed class DeterministicDispatcher : IDispatcher
    {
        private const int DefaultThroughput = 300;

        private readonly TaskScheduler _scheduler;

        public DeterministicDispatcher(TaskScheduler scheduler, int throughput = DefaultThroughput)
        {
            _scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
            Throughput = throughput;
        }

        public int Throughput { get; }

        public void Schedule(Func<Task> runner)
        {
            // Queue the mailbox run on the shared exclusive scheduler: it runs runners one at a time, in the
            // FIFO order they were scheduled, so the whole stepped actor system processes on a single
            // serialized timeline rather than fanning out across the thread pool.
            _ = Task.Factory.StartNew(runner, CancellationToken.None, TaskCreationOptions.DenyChildAttach, _scheduler).Unwrap();
        }
    }
}