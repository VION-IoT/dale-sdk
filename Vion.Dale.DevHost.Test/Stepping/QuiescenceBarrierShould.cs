using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Vion.Dale.DevHost.Control;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Diagnostics;

namespace Vion.Dale.DevHost.Test.Stepping
{
    /// <summary>
    ///     The quiescence predicate in isolation: every mailbox empty AND no handler in flight, both read
    ///     live. Its whole value is that it never stands down on a heuristic, so the two half-satisfied
    ///     states — depth zero with a handler running, and a handler done with traffic still queued — are
    ///     what this suite pins.
    ///     <para>
    ///         The stepper's real-clock safety timeout around this wait is not covered: it is a fixed ten
    ///         seconds with no injection seam, so a test could only reach it by waiting it out
    ///         (<c>docs/specs/_findings.md</c>).
    ///     </para>
    /// </summary>
    [TestClass]
    public class QuiescenceBarrierShould
    {
        // The window a "still waiting" claim is observed over. Load can only make it MORE likely to hold —
        // the wait completing early is what would falsify it, and a slow machine cannot cause that.
        private static readonly TimeSpan Window = TimeSpan.FromMilliseconds(200);

        // A hang guard for the waits that are expected to complete.
        private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.5")]
        public async Task ReturnOnceEveryMailboxDrainedAndNoHandlerRunning()
        {
            // Arrange
            var vitals = new RuntimeVitals(TimeProvider.System);
            var activity = new InFlightActivityMonitor();
            var barrier = new QuiescenceBarrier(vitals, activity);

            // Act
            await barrier.WaitForQuiescenceAsync(CancellationToken.None);

            // Assert — an idle system satisfies the predicate on the first observation, with no window.
            Assert.AreEqual(0, activity.InFlight);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.6")]
        public async Task KeepWaitingWhileHandlerRunsWithEveryMailboxDrained()
        {
            // Arrange — the blind spot mailbox depth alone has: a handler that has dequeued its message and
            // has not yet posted the next hop, so depth reads zero while the cascade is still live.
            var activity = new InFlightActivityMonitor();
            var barrier = new QuiescenceBarrier(new RuntimeVitals(TimeProvider.System), activity);
            activity.EnterHandler();

            // Act
            var waiting = barrier.WaitForQuiescenceAsync(CancellationToken.None);

            // Assert — it did not treat the system as settled: the observation window expires with the wait
            // still pending, and the expiry IS the assertion. A cancellation token here would race the
            // barrier's first observation instead — on a loaded machine the token can fire before the wait is
            // entered, and the entry throw is an OperationCanceledException, not the TaskCanceledException a
            // cancelled wait produces.
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => waiting.WaitAsync(Window));
            Assert.AreEqual(1, activity.InFlight);

            // And it returns as soon as the handler leaves.
            activity.ExitHandler();
            await waiting.WaitAsync(Timeout);
        }

        [TestMethod]
        [TestProperty("spec", "AC-SCEN-012.6")]
        public async Task KeepWaitingWhileTrafficQueuedWithNoHandlerRunning()
        {
            // Arrange — the other half: nothing executing, but a mailbox still holding a message.
            var vitals = new RuntimeVitals(TimeProvider.System);
            vitals.OnMessagePosted("actor-1");
            vitals.OnMessagePosted("actor-1");
            vitals.OnMessageReceived("actor-1");
            var barrier = new QuiescenceBarrier(vitals, new InFlightActivityMonitor());

            // Act
            var waiting = barrier.WaitForQuiescenceAsync(CancellationToken.None);

            // Assert — same shape as the sibling: the window expiring while the wait is pending is the claim.
            await Assert.ThrowsExactlyAsync<TimeoutException>(() => waiting.WaitAsync(Window));

            // Draining the mailbox satisfies the predicate.
            vitals.OnMessageReceived("actor-1");
            await waiting.WaitAsync(Timeout);
        }
    }
}