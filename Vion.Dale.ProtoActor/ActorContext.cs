using System;
using System.Collections.Generic;
using System.Reflection;
using System.Threading.Tasks;
using Proto;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.ProtoActor
{
    public class ActorContext : IActorContext
    {
        private readonly Func<IContext> _context;

        private readonly IDelayedSendGate? _delayedSendGate;

        // Optional, opt-in virtual schedule (DevHost's next-event stepping). Null when none is registered,
        // so a host without it keeps the original scheduling behaviour.
        private readonly IVirtualSchedule? _schedule;

        // True when the clock is controllable (FakeTimeProvider) — i.e. the host is stepped. Computed once
        // (the structural Advance(TimeSpan) detection used across the stepping stack) so the hot SendToSelfAfter
        // path doesn't reflect per call.
        private readonly bool _stepped;

        private readonly TimeProvider _timeProvider;

        public ActorContext(Func<IContext> context, IDelayedSendGate? delayedSendGate = null, TimeProvider? timeProvider = null, IVirtualSchedule? schedule = null)
        {
            _context = context;
            _delayedSendGate = delayedSendGate;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _schedule = schedule;
            _stepped = IsSteppedClock(_timeProvider);
        }

        public IReadOnlyDictionary<string, string> Headers
        {
            get => _context().Headers.ToDictionary().AsReadOnly();
        }

        public void SendTo(IActorReference target, object message, Dictionary<string, string>? headers = null)
        {
            var targetPid = ((ActorReference)target).Pid;
            var messageHeader = headers != null ? new MessageHeader(headers) : null;
            _context().SendWithHeaders(targetPid, message, messageHeader);
        }

        public void SendToSelf(object message)
        {
            _context().Send(_context().Self, message);
        }

        public void SendToSelfAfter(object message, TimeSpan delay)
        {
            // Opt-in pause gate (DevHost): when registered AND holding, the schedule is queued and replayed
            // on resume — with the original delay, so a paused 5 s timer fires 5 s after resume. Production
            // registers no gate → unchanged. The replay re-enters this method, so a still-paused gate simply
            // re-holds.
            if (_delayedSendGate is not null && _delayedSendGate.TryHold(() => SendToSelfAfter(message, delay)))
            {
                return;
            }

            // Stepped host (DevHost + FakeTimeProvider): the next-event stepper DELIVERS this send itself, in a
            // deterministic order, instead of a Task.Delay continuation firing it — when several timers are due
            // at the same virtual instant, racing Task.Delay continuations on the thread pool would otherwise
            // deliver them in a thread-pool-dependent order (RFC 0008 / DF-18). Register the delivery action
            // (no Task.Delay armed); the stepper invokes it at the due virtual time, earliest-then-registration
            // order, having taken (removed) the entry — so the action just performs the send.
            if (_schedule is not null && _stepped)
            {
                _schedule.RegisterDelivery(new object(), _timeProvider.GetUtcNow() + delay, () => SendToSelf(message));
                return;
            }

            // Opt-in virtual schedule (DevHost next-event stepping) on a real-clock host: record this send's
            // virtual due-time so the stepper can advance the fake clock to the next scheduled event. The token
            // is a fresh identity; it is unregistered in the continuation BEFORE the message is sent, so the
            // moment the delay completes the entry is gone and a re-query sees the rescheduled (later) due-time,
            // not this stale one. Null schedule (production) → unchanged.
            if (_schedule is null)
            {
                _context().ReenterAfter(Task.Delay(delay, _timeProvider), _ => SendToSelf(message));
                return;
            }

            var token = new object();
            _schedule.Register(token, _timeProvider.GetUtcNow() + delay);
            _context()
                .ReenterAfter(Task.Delay(delay, _timeProvider),
                              _ =>
                              {
                                  _schedule.Unregister(token);
                                  SendToSelf(message);
                              });
        }

        public void RespondToSender(object message)
        {
            _context().SendWithHeaders(_context().Sender ?? throw new InvalidOperationException("context.Sender not be null wen calling RespondToSender()"), message);
        }

        public IActorReference LookupByName(string name)
        {
            return new ActorReference(PidUtils.FromName(name));
        }

        // Structural detection of a controllable clock (FakeTimeProvider): a public instance Advance(TimeSpan)
        // returning void — the same detection used by DeterministicStepper / IDevHostControl.IsStepped, so the
        // shipped library needs no reference to the test-only TimeProvider.Testing assembly.
        private static bool IsSteppedClock(TimeProvider timeProvider)
        {
            var advance = timeProvider.GetType().GetMethod("Advance", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(TimeSpan) }, null);
            return advance is { ReturnType: { } returnType } && returnType == typeof(void);
        }
    }
}