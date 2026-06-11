using System;

namespace Vion.Dale.Sdk.Abstractions
{
    /// <summary>
    ///     Optional, opt-in gate for delayed self-sends (<see cref="IActorContext.SendToSelfAfter" /> — the
    ///     single choke point all <c>[Timer]</c> ticks and <c>InvokeSynchronizedAfter</c> callbacks pass
    ///     through). When registered, the gate may HOLD a scheduling instead of performing it — the DevHost's
    ///     pause feature queues them and replays on resume, so self-rescheduling chains survive a pause
    ///     instead of breaking permanently.
    ///     <para>
    ///         Production registers no gate → behaviour is unchanged (the same opt-in pattern as
    ///         <see cref="IActorMessageObserver" />, RFC 0003's message tap).
    ///     </para>
    /// </summary>
    public interface IDelayedSendGate
    {
        /// <summary>
        ///     Offer a delayed send to the gate. Return <c>true</c> when the gate holds it (the gate becomes
        ///     responsible for invoking <paramref name="scheduleNow" /> later, e.g. on resume); <c>false</c>
        ///     when the caller should schedule immediately. <paramref name="scheduleNow" /> performs the real
        ///     delayed send with the original delay and may be offered to the gate again when invoked while
        ///     the gate is holding.
        /// </summary>
        bool TryHold(Action scheduleNow);
    }
}