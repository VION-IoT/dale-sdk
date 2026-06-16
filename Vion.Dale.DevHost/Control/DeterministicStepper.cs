using System;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Drives the actor system through N deterministic simulation cycles. Each cycle advances a
    ///     controllable fake clock by one interval (releasing every <c>Task.Delay(…, clock)</c> due at the
    ///     new simulated time — i.e. the next <c>[Timer]</c> tick) and then awaits
    ///     <see cref="QuiescenceBarrier" /> so the resulting handler cascade fully settles before the next
    ///     advance.
    ///     <para>
    ///         Determinism comes from the two-phase loop: nothing simulated happens except at a clock
    ///         <c>Advance</c>, and the barrier guarantees the system is idle (every mailbox drained, no
    ///         handler in flight) at each cycle boundary. So a given (block set, interval, cycle count)
    ///         always produces the same end state, run to run, regardless of real-thread timing.
    ///     </para>
    ///     <para>
    ///         Stepping requires a controllable clock. The stepper detects <c>FakeTimeProvider</c>
    ///         <em>structurally</em> — by its public <c>Advance(TimeSpan)</c> method — so the shipped
    ///         <c>Vion.Dale.DevHost</c> package carries no dependency on the test-only
    ///         <c>Microsoft.Extensions.TimeProvider.Testing</c> assembly. If the registered
    ///         <see cref="TimeProvider" /> has no such method (e.g. the real
    ///         <see cref="TimeProvider.System" />), the constructor throws: advancing a real wall clock by
    ///         hand is meaningless.
    ///     </para>
    /// </summary>
    internal sealed class DeterministicStepper
    {
        // Generous real-clock ceiling on a single quiescence wait. The barrier polls on the real clock; this
        // bounds it so a genuinely stuck system surfaces as a thrown TimeoutException rather than hanging.
        private static readonly TimeSpan QuiescenceTimeout = TimeSpan.FromSeconds(10);

        private readonly Action<TimeSpan> _advance;

        private readonly QuiescenceBarrier _barrier;

        public DeterministicStepper(TimeProvider timeProvider, QuiescenceBarrier barrier)
        {
            if (timeProvider is null)
            {
                throw new ArgumentNullException(nameof(timeProvider));
            }

            _advance = BindAdvance(timeProvider);
            _barrier = barrier ?? throw new ArgumentNullException(nameof(barrier));
        }

        /// <summary>
        ///     Settle any startup traffic, then run <paramref name="cycles" /> deterministic cycles:
        ///     advance the fake clock by <paramref name="interval" /> and await quiescence each time.
        /// </summary>
        public async Task AdvanceAsync(TimeSpan interval, int cycles, CancellationToken cancellationToken = default)
        {
            if (cycles < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(cycles), cycles, "Cycle count must not be negative.");
            }

            // Settle startup traffic once before stepping, so cycle 1 starts from a known-idle state.
            await SettleAsync(cancellationToken).ConfigureAwait(false);

            for (var cycle = 0; cycle < cycles; cycle++)
            {
                _advance(interval);
                await SettleAsync(cancellationToken).ConfigureAwait(false);
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