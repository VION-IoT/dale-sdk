using System;

namespace Vion.Dale.Sdk.Abstractions
{
    public interface IActorDispatcher
    {
        void InvokeSynchronized(Action action);

        void InvokeSynchronizedAfter(Action action, TimeSpan delay);
    }
}