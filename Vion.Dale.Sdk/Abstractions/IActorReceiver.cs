using System.Threading.Tasks;

namespace Vion.Dale.Sdk.Abstractions
{
    public interface IActorReceiver
    {
        Task HandleMessageAsync(object message, IActorContext actorContext);
    }
}