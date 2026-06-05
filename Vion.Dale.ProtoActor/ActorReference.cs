using Proto;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.ProtoActor
{
    public readonly record struct ActorReference(PID Pid) : IActorReference;
}