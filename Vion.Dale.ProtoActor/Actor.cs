using System;
using System.Threading.Tasks;
using Proto;
using Proto.Mailbox;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.ProtoActor
{
    public class Actor<TActorReceiver> : IActor
        where TActorReceiver : IActorReceiver
    {
        private readonly TActorReceiver _actorReceiver;

        private readonly IDelayedSendGate? _delayedSendGate;

        private readonly TimeProvider _timeProvider;

        public Actor(TActorReceiver actorReceiver, IDelayedSendGate? delayedSendGate = null, TimeProvider? timeProvider = null)
        {
            _actorReceiver = actorReceiver;
            _delayedSendGate = delayedSendGate;
            _timeProvider = timeProvider ?? TimeProvider.System;
        }

        /// <inheritdoc />
        public async Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case null:
                case SystemMessage:
                    break;

                default:
                    await _actorReceiver.HandleMessageAsync(context.Message, new ActorContext(() => context, _delayedSendGate, _timeProvider));
                    break;
            }
        }
    }
}