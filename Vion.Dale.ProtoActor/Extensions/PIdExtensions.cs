using Proto;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.ProtoActor.Extensions
{
    public static class PIdExtensions
    {
        public static IActorReference ToActorReference(this PID pid)
        {
            return new ActorReference(pid);
        }
    }
}