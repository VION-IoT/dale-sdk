using System;
using System.Collections.Generic;
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
    ///         A stepper's barrier also counts the exchanges SDK clients and servers carry off the actor system
    ///         (<see cref="IExchangeActivityMonitor" />): while one is open its result is in no mailbox, so depth and the
    ///         handler count both read zero. It reads <see cref="InFlightActivityMonitor.Busy" />, where an exchange and a
    ///         handler share one count, so the predicate stays a single observation. The teardown drain reads handlers
    ///         only — it must not wait on a socket.
    ///     </para>
    ///     <para>
    ///         The barrier does not poll on a timer. It takes the monitor's "a count reached zero" signal, evaluates the
    ///         predicate, and awaits the signal when it does not hold — taking the signal first is what keeps a count
    ///         reaching zero between the evaluation and the await from passing unseen. A slow real-clock re-check stands
    ///         behind the signal for the one case it does not cover: traffic queued on a mailbox nothing will run, which
    ///         no count ever leaves zero for. That re-check evaluates the same exact predicate; it is not a window. A
    ///         timeout surfaces as a thrown <see cref="OperationCanceledException" /> from the caller's token — never an
    ///         infinite loop, never a silent "assume settled".
    ///     </para>
    /// </summary>
    internal sealed class QuiescenceBarrier
    {
        // Real-clock spacing of the re-check behind the zero signal. Not load-bearing for correctness (the predicate is
        // exact) and not on the normal path: every settle that completes does so on the signal. Only a mailbox holding
        // traffic nothing will run waits on it, and that wait ends at the caller's budget either way.
        private static readonly TimeSpan FallbackInterval = TimeSpan.FromMilliseconds(50);

        // Optional monitor. When null (no DevHost monitor registered) the barrier degrades to the depth-only signal,
        // re-checked at the fallback interval — but DevHost always registers one, so the exact predicate is the live path.
        private readonly InFlightActivityMonitor? _activity;

        private readonly bool _countExchanges;

        private readonly RuntimeVitals _vitals;

        /// <summary>What every exchange still open was opened as; empty when exchanges are not counted.</summary>
        public IReadOnlyList<string> OpenExchanges
        {
            get => _countExchanges && _activity is not null ? _activity.OpenExchanges : Array.Empty<string>();
        }

        /// <param name="vitals">The per-actor mailbox statistics.</param>
        /// <param name="activity">The host's handler and exchange counts.</param>
        /// <param name="countExchanges">
        ///     Whether an open SDK exchange keeps the system from being quiescent — the stepper's predicate; the teardown
        ///     drain passes <c>false</c>.
        /// </param>
        public QuiescenceBarrier(RuntimeVitals vitals, InFlightActivityMonitor? activity, bool countExchanges)
        {
            _vitals = vitals ?? throw new ArgumentNullException(nameof(vitals));
            _activity = activity;
            _countExchanges = countExchanges;
        }

        /// <summary>
        ///     Waits until the exact quiescence predicate holds, or throws if <paramref name="cancellationToken" /> fires
        ///     first (the caller wires it to a generous real-clock safety timeout). A single satisfying observation
        ///     returns — no window.
        /// </summary>
        public async Task WaitForQuiescenceAsync(CancellationToken cancellationToken)
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var zeroReached = _activity?.WhenACountReachesZero();
                if (IsQuiescent())
                {
                    return;
                }

                if (zeroReached is null)
                {
                    await Task.Delay(FallbackInterval, cancellationToken).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    await zeroReached.WaitAsync(FallbackInterval, cancellationToken).ConfigureAwait(false);
                }
                catch (TimeoutException)
                {
                    // The re-check behind the signal: evaluate again.
                }
            }
        }

        // EXACT predicate: the count first, then every mailbox. A count entered before a handler body runs, and an
        // exchange that posts its callback before it closes, both keep the count above zero until what they started has
        // reached a mailbox — so a zero count followed by zero depth cannot be read mid-cascade. Snapshot() allocates a
        // list per call; acceptable once per wake.
        private bool IsQuiescent()
        {
            if (_activity is not null && (_countExchanges ? _activity.Busy : _activity.InFlight) != 0)
            {
                return false;
            }

            return _vitals.Snapshot().Sum(a => (long)a.MailboxDepth) == 0;
        }
    }
}