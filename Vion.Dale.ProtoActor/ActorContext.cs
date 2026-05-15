using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.Sdk.Abstractions;
using Proto;

namespace Vion.Dale.ProtoActor
{
    public class ActorContext : IActorContext
    {
        private readonly Func<IContext> _context;

        public ActorContext(Func<IContext> context)
        {
            _context = context;
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