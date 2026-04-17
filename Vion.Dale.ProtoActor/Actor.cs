using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Proto;
using Proto.Mailbox;

namespace Vion.Dale.ProtoActor
{
    public class Actor<TActorReceiver> : IActor
        where TActorReceiver : IActorReceiver
    {
        private readonly TActorReceiver _actorReceiver;

        public Actor(TActorReceiver actorReceiver)
        {
            _actorReceiver = actorReceiver;
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
                    await _actorReceiver.HandleMessageAsync(context.Message, new ActorContext(() => context));
                    break;
            }
        }
    }
}