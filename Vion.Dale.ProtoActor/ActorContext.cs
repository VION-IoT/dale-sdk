using System;
using System.Collections.Generic;
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

        public ActorContext(Func<IContext> context, IDelayedSendGate? delayedSendGate = null)
        {
            _context = context;
            _delayedSendGate = delayedSendGate;
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

            _context().ReenterAfter(Task.Delay(delay), _ => SendToSelf(message));
        }

        public void RespondToSender(object message)
        {
            _context().SendWithHeaders(_context().Sender ?? throw new InvalidOperationException("context.Sender not be null wen calling RespondToSender()"), message);
        }

        public IActorReference LookupByName(string name)
        {
            return new ActorReference(PidUtils.FromName(name));
        }
    }
}