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
            using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));
            var waiting = Assert.ThrowsExactlyAsync<TaskCanceledException>(() => barrier.WaitForQuiescenceAsync(giveUp.Token));

            // Assert — it did not treat the system as settled; it was still waiting when the token fired.
            await waiting;
            Assert.AreEqual(1, activity.InFlight);

            // And it returns as soon as the handler leaves.
            activity.ExitHandler();
            await barrier.WaitForQuiescenceAsync(CancellationToken.None);
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
            using var giveUp = new CancellationTokenSource(TimeSpan.FromMilliseconds(200));

            // Assert
            await Assert.ThrowsExactlyAsync<TaskCanceledException>(() => barrier.WaitForQuiescenceAsync(giveUp.Token));

            // Draining the mailbox satisfies the predicate.
            vitals.OnMessageReceived("actor-1");
            await barrier.WaitForQuiescenceAsync(CancellationToken.None);
        }
    }
}
