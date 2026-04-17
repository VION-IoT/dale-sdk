using Vion.Dale.Sdk.Abstractions;
using Proto;

namespace Vion.Dale.ProtoActor
{
    public readonly record struct ActorReference(PID Pid) : IActorReference;
}