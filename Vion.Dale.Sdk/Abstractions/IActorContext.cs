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

        /// <summary>
        ///     Answers the sender of the message this actor is handling when it is called.
        /// </summary>
        /// <param name="message">The answer.</param>
        /// <exception cref="InvalidOperationException">
        ///     Thrown when the message being handled carries no sender: only a request does, and a
        ///     fire-and-forget send or a message an actor sent itself does not.
        /// </exception>
        void RespondToSender(object message);

        IActorReference LookupByName(string name);
    }
}