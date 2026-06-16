using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Drives the actor system through deterministic <em>next-event</em> virtual-time stepping. Rather than
    ///     advancing a controllable fake clock by a caller-supplied fixed interval, the stepper advances to each
    ///     next scheduled event — read from the engine-owned <see cref="IVirtualSchedule" /> — and quiesces
    ///     after each, so every due <c>[Timer]</c> / <c>InvokeSynchronizedAfter</c> fires the right number of
    ///     times at the right virtual times with no drift, even when reschedule delays are dynamic.
    ///     <para>
    ///         Why next-event and not a fixed interval: a single <c>FakeTimeProvider.Advance(5s)</c> fires a
    ///         <c>[Timer(1)]</c> only ONCE — its reschedule runs async, AFTER <c>Advance</c> returns — and
    ///         desyncs the cadence. To fire it 5× with no drift the stepper advances to EACH next scheduled
    ///         event and quiesces between, so the handler runs and reschedules at the correct virtual time
    ///         before the next advance. <see cref="Microsoft.Extensions.Time.Testing.FakeTimeProvider" /> does
    ///         not expose its next-due time, so the engine owns its own virtual schedule at the delayed-send
    ///         choke point (<c>ActorContext.SendToSelfAfter</c>) it already controls.
    ///     </para>
    ///     <para>
    ///         Determinism comes from the two-phase loop: nothing simulated happens except at a clock
    ///         <c>Advance</c>, and the barrier guarantees the system is idle (every mailbox drained, no handler
    ///         in flight) at each event boundary. So a given (block set, virtual budget) always produces the
    ///         same end state, run to run, regardless of real-thread timing.
    ///     </para>
    ///     <para>
    ///         Stepping requires a controllable clock. The stepper detects <c>FakeTimeProvider</c>
    ///         <em>structurally</em> — by its public <c>Advance(TimeSpan)</c> and <c>GetUtcNow()</c> — so the
    ///         shipped <c>Vion.Dale.DevHost</c> package carries no dependency on the test-only
    ///         <c>Microsoft.Extensions.TimeProvider.Testing</c> assembly. If the registered
    ///         <see cref="TimeProvider" /> has no <c>Advance</c> method (e.g. the real
    ///         <see cref="TimeProvider.System" />), the constructor throws: advancing a real wall clock by hand
    ///         is meaningless.
    ///     </para>
    /// </summary>
    internal sealed class DeterministicStepper
    {
        // Generous real-clock ceiling on a single quiescence wait. The barrier polls on the real clock; this
        // bounds it so a genuinely stuck system surfaces as a thrown TimeoutException rather than hanging.
        private static readonly TimeSpan QuiescenceTimeout = TimeSpan.FromSeconds(10);

        private readonly Action<TimeSpan> _advance;

        private readonly QuiescenceBarrier _barrier;

        private readonly TimeProvider _clock;

        private readonly IVirtualSchedule _schedule;

        public DeterministicStepper(TimeProvider timeProvider, QuiescenceBarrier barrier, IVirtualSchedule schedule)
        {
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            _advance = BindAdvance(timeProvider);
            _clock = timeProvider;
            _barrier = barrier ?? throw new ArgumentNullException(nameof(barrier));
            _schedule = schedule ?? throw new ArgumentNullException(nameof(schedule));
        }

        /// <summary>
        ///     Advance virtual time by <paramref name="budget" />, firing every event due within it. Settles
        ///     startup traffic first, then advances to each next scheduled event (re-querying the schedule each
        ///     iteration — handlers reschedule during quiescence, moving the minimum forward) until no event
        ///     remains at or before the target. Finally, if the clock hasn't reached the target (no event sits
        ///     exactly at the end), advances the remainder so EXACTLY <paramref name="budget" /> virtual time
        ///     elapses.
        /// </summary>
        public async Task AdvanceByAsync(TimeSpan budget, CancellationToken cancellationToken = default)
        {
            if (budget < TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(budget), budget, "Virtual time budget must not be negative.");
            }

            // Settle startup traffic once before stepping, so the first hop starts from a known-idle state.
            await SettleAsync(cancellationToken).ConfigureAwait(false);

            var target = _clock.GetUtcNow() + budget;

            // Advance to each next scheduled event due at or before the target, quiescing between. Re-query
            // every iteration: each quiescence runs handlers that reschedule the next event forward.
            while (_schedule.NextDue() is { } due && due <= target)
            {
                AdvanceTo(due);
                await SettleAsync(cancellationToken).ConfigureAwait(false);
            }

            // No event sits exactly at the end of the budget: advance the remainder so exactly `budget`
            // virtual time elapses (a caller asking for "5 virtual seconds" gets the clock moved 5 s even if
            // the last event landed before t+5s), then quiesce.
            var now = _clock.GetUtcNow();
            if (now < target)
            {
                _advance(target - now);
                await SettleAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        /// <summary>
        ///     Advance to the single next scheduled event and quiesce. When nothing is scheduled, just waits
        ///     for the system to be idle and returns (no clock movement). A single event hop — the primitive
        ///     <c>waitUntil</c> / <c>settle</c> helpers build on.
        /// </summary>
        public async Task AdvanceToNextEventAsync(CancellationToken cancellationToken = default)
        {
            await SettleAsync(cancellationToken).ConfigureAwait(false);

            if (_schedule.NextDue() is { } due)
            {
                AdvanceTo(due);
                await SettleAsync(cancellationToken).ConfigureAwait(false);
            }
        }

        // Advance the fake clock from its current value to `due`. Never moves backward: a due-time already at
        // or behind the clock (e.g. an event that became due during quiescence) advances by zero, then the
        // following quiescence still drains it. Guards the BindAdvance reflection invariant (non-negative).
        private void AdvanceTo(DateTimeOffset due)
        {
            var delta = due - _clock.GetUtcNow();
            if (delta > TimeSpan.Zero)
            {
                _advance(delta);
            }
        }

        // Each quiescence wait gets its own real-clock safety budget, linked to the caller's token so an
        // external cancel still wins. A timeout throws TimeoutException — never a silent "assume settled".
        private async Task SettleAsync(CancellationToken cancellationToken)
        {
            using var timeout = new CancellationTokenSource(QuiescenceTimeout);
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeout.Token);
            try
            {
                await _barrier.WaitForQuiescenceAsync(linked.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested && !cancellationToken.IsCancellationRequested)
            {
                throw new TimeoutException($"Actor system did not reach quiescence within {QuiescenceTimeout.TotalSeconds:0}s — the exact " +
                                           "predicate (Σ MailboxDepth == 0 AND no user handler in flight) never held. The cascade is " +
                                           "either stuck or producing unbounded follow-up traffic.");
            }
        }

        // Structural detection of FakeTimeProvider: bind its public instance Advance(TimeSpan). Avoids a
        // compile-time reference to the test-only TimeProvider.Testing assembly from the shipped library.
        private static Action<TimeSpan> BindAdvance(TimeProvider timeProvider)
        {
            var advance = timeProvider.GetType().GetMethod("Advance", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(TimeSpan) }, null);

            if (advance is null || advance.ReturnType != typeof(void))
            {
                throw new InvalidOperationException("Deterministic stepping requires a controllable clock (FakeTimeProvider) registered as the " +
                                                    "TimeProvider — e.g. .ConfigureServices(s => s.AddSingleton<TimeProvider>(new FakeTimeProvider())). " +
                                                    $"The resolved TimeProvider is '{timeProvider.GetType().Name}', which exposes no Advance(TimeSpan) " +
                                                    "and cannot be stepped.");
            }

            return interval => advance.Invoke(timeProvider, new object[] { interval });
        }
    }
}