using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Awaits actor-system <em>quiescence</em>: the point at which every mailbox is drained AND no user
    ///     handler is mid-flight, so the cascade kicked off by the previous
    ///     <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider.Advance" /> has fully settled.
    ///     <para>
    ///         The predicate is EXACT, not a time-window heuristic. It conjoins two independent live signals:
    ///         <list type="bullet">
    ///             <item>
    ///                 <c>Σ MailboxDepth == 0</c> — read from <see cref="RuntimeVitals" /> (fed by the Proto
    ///                 mailbox-statistics hook for every message): <c>posted − received</c> is exactly the
    ///                 number of messages still queued. Zero means every mailbox is empty.
    ///             </item>
    ///             <item>
    ///                 <c>InFlight == 0</c> — read from <see cref="IActorActivityMonitor" /> (the DevHost opt-in
    ///                 monitor the actor middleware brackets every handler with): zero means no user handler is
    ///                 currently executing.
    ///             </item>
    ///         </list>
    ///         Mailbox depth alone has a blind spot: <c>received</c> is incremented at <em>dequeue</em>, before
    ///         the handler runs, so depth can read zero while a handler is still executing and about to post a
    ///         follow-up (a fire-and-forget forward-only cascade exposes this — there is no reverse traffic to
    ///         keep depth above zero). The in-flight count closes it exactly: the middleware enters a handler
    ///         BEFORE its body runs, so a handler that is about to post the next hop has already incremented
    ///         in-flight. Therefore <c>depth == 0 AND inFlight == 0</c> cannot be observed while any cascade is
    ///         still live — a single observation is true quiescence. No stability window is needed.
    ///     </para>
    ///     <para>
    ///         In stepped mode the in-flight bracket is widened from the user handler to the whole mailbox
    ///         RUN: the <c>DeterministicDispatcher</c> records in-flight at the synchronous schedule (before
    ///         the run touches the mailbox) and releases it at run completion. This shadows the
    ///         dequeue-to-handler-enter sub-window (depth already dropped, the handler bracket not yet
    ///         entered) under <c>inFlight &gt; 0</c>. Without it, a runner preempted in that sub-window under
    ///         load let the poll observe a transient false idle, surfacing as off-by-one stepped samples (a
    ///         watched value read from the change-event cache before the cascade's last publish landed) only
    ///         on a loaded CI runner.
    ///     </para>
    ///     <para>
    ///         The barrier still polls on the REAL wall clock (<see cref="Task.Delay(int)" />) — that is
    ///         orchestration, not simulation; only the <em>simulated</em> time is the fake clock the stepper
    ///         advances. The poll merely re-evaluates an exact predicate; it does not rely on timing. A timeout
    ///         surfaces as a thrown <see cref="TimeoutException" /> — never an infinite loop, never a silent
    ///         "assume settled".
    ///     </para>
    /// </summary>
    internal sealed class QuiescenceBarrier
    {
        // Real-clock spacing between predicate evaluations. Small enough to keep stepping snappy; the value is
        // not load-bearing for correctness (the predicate is exact, not a window) — only for responsiveness.
        private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(1);

        // Optional in-flight monitor. When null (no DevHost monitor registered) the barrier degrades to the
        // depth-only signal — but DevHost always registers one, so the exact predicate is the live path.
        private readonly IActorActivityMonitor? _activity;

        private readonly RuntimeVitals _vitals;

        public QuiescenceBarrier(RuntimeVitals vitals, IActorActivityMonitor? activity)
        {
            _vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
            _activity = activity;
        }

        /// <summary>
        ///     Polls the exact quiescence predicate (<c>Σ MailboxDepth == 0 AND InFlight == 0</c>) until it
        ///     holds, or throws if <paramref name="cancellationToken" /> fires first (the caller wires it to a
        ///     generous real-clock safety timeout). A single satisfying observation returns — no window.
        /// </summary>
        public async Task WaitForQuiescenceAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (IsQuiescent())
                {
                    return;
                }

                // Real-clock cadence (orchestration), not the simulated clock — see class remarks.
                await Task.Delay(PollInterval, cancellationToken).ConfigureAwait(false);
            }
        }

        // EXACT predicate: every mailbox empty AND no user handler currently executing. The in-flight count is
        // entered before a handler body runs, so a handler that has dequeued its message but not yet posted the
        // next hop is shadowed by inFlight > 0 — the predicate cannot be true mid-cascade. Snapshot() allocates
        // a list per call; acceptable for a real-clock poll cadence.
        private bool IsQuiescent()
        {
            if (_activity is not null && _activity.InFlight != 0)
            {
                return false;
            }

            return _vitals.Snapshot().Sum(a => (long)a.MailboxDepth) == 0;
        }
    }
}