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

        public Actor(TActorReceiver actorReceiver, IDelayedSendGate? delayedSendGate = null)
        {
            _actorReceiver = actorReceiver;
            _delayedSendGate = delayedSendGate;
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
                    await _actorReceiver.HandleMessageAsync(context.Message, new ActorContext(() => context, _delayedSendGate));
                    break;
            }
        }
    }
}