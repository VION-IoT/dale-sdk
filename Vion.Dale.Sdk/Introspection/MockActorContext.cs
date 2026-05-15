using System;
using System.Collections.Generic;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.Introspection
{
    public class MockActorContext : IActorContext
    {
        /// <inheritdoc />
        public IReadOnlyDictionary<string, string>? Headers { get; }

        /// <inheritdoc />
        public void SendTo(IActorReference target, object message, Dictionary<string, string>? headers = null)
        {
        }

        /// <inheritdoc />
        public void SendToSelf(object message)
        {
        }

        /// <inheritdoc />
        public void SendToSelfAfter(object message, TimeSpan delay)
        {
        }

        /// <inheritdoc />
        public void RespondToSender(object message)
        {
        }

        /// <inheritdoc />
        public IActorReference LookupByName(string name)
        {
            throw new NotImplementedException();
        }
    }
}