using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     SPIKE (Task 3) — awaits actor-system <em>quiescence</em>: the point at which every mailbox is
    ///     drained and no handler is mid-flight, so the cascade kicked off by the previous
    ///     <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider.Advance" /> has fully settled.
    ///     <para>
    ///         The quiescence signal is read from <see cref="RuntimeVitals" />, which the Proto
    ///         mailbox-statistics hook feeds for every mailbox message (user, system, and infrastructure
    ///         alike). The clean, same-denominator signal is <see cref="ActorVitals.MailboxDepth" />
    ///         (messages posted − received): both counters come from that one hook, so their difference is
    ///         exactly the number of messages still <em>queued</em> (enqueued, not yet dequeued). When it
    ///         reads zero for every actor, every mailbox is empty.
    ///     </para>
    ///     <para>
    ///         Mailbox depth has one blind spot: <c>received</c> is incremented at <em>dequeue</em>, before
    ///         the handler runs, so depth can read zero while a handler is still executing and about to post
    ///         a follow-up. (The cumulative <see cref="ActorVitals.MessagesHandled" /> can't close this gap on
    ///         its own — it is fed by the actor <em>middleware</em>, which only sees user-handler messages, so
    ///         <c>posted − handled</c> never returns to zero: every system / infrastructure message posted to
    ///         a mailbox is counted by <c>posted</c> but never by <c>handled</c>, leaving a permanent positive
    ///         floor.) The blind spot is bridged by requiring <c>Σ MailboxDepth == 0</c> for a few CONSECUTIVE
    ///         reads spaced by a real-clock poll: a handler that is briefly in-flight will post its follow-up
    ///         within that window, re-raising the sum above zero and resetting the streak.
    ///     </para>
    ///     <para>
    ///         The barrier is orchestration: its poll cadence uses REAL wall-clock
    ///         <see cref="Task.Delay(int)" />; only the <em>simulated</em> time is the fake clock the stepper
    ///         advances. A timeout surfaces as a thrown <see cref="TimeoutException" /> — never an infinite
    ///         loop, never a silent "assume settled".
    ///     </para>
    /// </summary>
    internal sealed class QuiescenceBarrier
    {
        // Number of consecutive zero-depth reads required before declaring quiescence. Bridges the mailbox-
        // depth blind spot: a handler that has been dequeued (depth already back to zero) but is about to
        // post a follow-up will do so within this many polls, re-raising the sum and resetting the streak.
        private const int RequiredConsecutiveIdleReads = 5;

        // Real-clock spacing between polls. Small enough to keep stepping snappy, large enough to let an
        // in-flight handler make progress (post its follow-up) between reads.
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(2);

        private readonly RuntimeVitals _vitals;

        public QuiescenceBarrier(RuntimeVitals vitals)
        {
            _vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
        }

        /// <summary>
        ///     Polls <c>Σ MailboxDepth</c> until it reads zero for <see cref="RequiredConsecutiveIdleReads" />
        ///     consecutive reads (the stable window that bridges the in-flight blind spot), or throws if
        ///     <paramref name="cancellationToken" /> fires first (the caller wires it to a generous
        ///     real-clock safety timeout).
        /// </summary>
        public async Task WaitForQuiescenceAsync(CancellationToken cancellationToken)
        {
            var consecutiveIdle = 0;

            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (QueuedDepth() == 0)
                {
                    consecutiveIdle++;
                    if (consecutiveIdle >= RequiredConsecutiveIdleReads)
                    {
                        return;
                    }
                }
                else
                {
                    consecutiveIdle = 0;
                }

                // Real-clock cadence (orchestration), not the simulated clock — see class remarks.
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        // Σ MailboxDepth across all actors: messages enqueued but not yet dequeued. Zero == every mailbox
        // empty. The consecutive-reads window in the caller bridges the in-flight blind spot (a handler
        // dequeued but not finished). Snapshot() allocates a list per call — acceptable for a spike's poll.
        private long QueuedDepth()
        {
            return _vitals.Snapshot().Sum(a => (long)a.MailboxDepth);
        }
    }
}
