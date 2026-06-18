using System;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Optional, opt-in virtual schedule of pending delayed self-sends. When an implementation is
    ///     registered in the actor system's service provider, every <see cref="IActorContext.SendToSelfAfter" />
    ///     (the single choke point all <c>[Timer]</c> ticks and <c>InvokeSynchronizedAfter</c> callbacks pass
    ///     through), plus the two internal ack/stop <c>Task.Delay(timeout, clock)</c> waits, register their
    ///     virtual due-time here and unregister it when they fire. When none is registered (the default,
    ///     including the production runtime), nothing extra happens. Used by DevHost's NEXT-EVENT stepping
    ///     (RFC 0003) so the engine can advance the fake clock to each next scheduled event rather than by a
    ///     caller-supplied fixed interval.
    ///     <para>
    ///         <c>FakeTimeProvider</c> does not expose its next-due
    ///         time, so the engine cannot ask the clock "when is the next timer?" — it must own its own view of
    ///         the pending schedule at the choke point it already controls. This is the same opt-in pattern as
    ///         <see cref="IActorMessageObserver" /> (RFC 0003's message tap), <see cref="IDelayedSendGate" />
    ///         (the pause gate), and <see cref="IActorActivityMonitor" /> (the in-flight monitor).
    ///     </para>
    ///     <para>Implementations must be thread-safe: registrations happen on actor threads.</para>
    /// </summary>
    public interface IVirtualSchedule
    {
        /// <summary>
        ///     Record that a delayed send identified by <paramref name="token" /> is due at
        ///     <paramref name="dueUtc" /> (the virtual UTC time read from the registered <c>TimeProvider</c>
        ///     plus the delay). The token is an opaque identity the caller also passes to
        ///     <see cref="Unregister" /> when the send fires.
        /// </summary>
        void Register(object token, DateTimeOffset dueUtc);

        /// <summary>Remove the pending entry identified by <paramref name="token" /> (called as the send fires).</summary>
        void Unregister(object token);

        /// <summary>
        ///     Record a delayed send the next-event stepper will DELIVER itself (RFC 0008 deterministic
        ///     stepping): instead of a <c>Task.Delay</c> firing the send — whose continuations race onto the
        ///     thread pool when several are due at the same virtual instant — the stepper invokes
        ///     <paramref name="deliver" /> in a fixed order (earliest due-time first, then registration order).
        ///     Used by <see cref="IActorContext.SendToSelfAfter" /> on a stepped host so same-virtual-instant
        ///     timer fires deliver in a single reproducible order. The implementation assigns each entry a
        ///     monotonic sequence at registration so ties break deterministically.
        /// </summary>
        void RegisterDelivery(object token, DateTimeOffset dueUtc, Action deliver);

        /// <summary>
        ///     The earliest pending due-time, or <c>null</c> when nothing is scheduled. The next-event stepper
        ///     advances the fake clock to this value, quiesces, then re-queries (handlers reschedule during the
        ///     quiescence, moving the minimum forward).
        /// </summary>
        DateTimeOffset? NextDue();

        /// <summary>
        ///     Remove and return the single earliest pending entry — the minimum by (due-time, then the
        ///     monotonic registration sequence), so same-virtual-instant entries come out in a deterministic
        ///     order, one at a time. <paramref name="deliver" /> is the delivery action for a
        ///     <see cref="RegisterDelivery" /> entry, or <c>null</c> for a plain <see cref="Register" /> entry
        ///     (whose own <c>Task.Delay</c> fires when the stepper advances the clock to <paramref name="dueUtc" />).
        ///     Returns <c>false</c> when nothing is scheduled. The stepper drives the deterministic timeline by
        ///     repeatedly taking the next entry, advancing the clock to its due-time, invoking
        ///     <paramref name="deliver" /> (if any), and quiescing.
        /// </summary>
        bool TryTakeNext(out DateTimeOffset dueUtc, out Action? deliver);
    }
}