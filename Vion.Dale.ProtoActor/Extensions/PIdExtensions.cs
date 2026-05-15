using Vion.Dale.Sdk.Abstractions;
using Proto;

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