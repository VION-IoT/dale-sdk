using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Abstractions
{
    public interface IActorContext
    {
        IReadOnlyDictionary<string, string>? Headers { get; }

        void SendTo(IActorReference target, object message, Dictionary<string, string>? headers = null);

        void SendToSelf(object message);

        void SendToSelfAfter(object message, TimeSpan delay);

        void RespondToSender(object message);

        IActorReference LookupByName(string name);
    }
}