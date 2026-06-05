using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     A single inter-actor message captured by the tap. <c>ActorName</c> is the receiving actor's
    ///     registered id; <c>Message</c> is the message instance (e.g. a <c>FunctionInterfaceMessage&lt;T&gt;</c>
    ///     for a cross-block command/request, or a service set-request).
    /// </summary>
    public sealed record TappedMessage(string ActorName, string MessageType, object Message, DateTimeOffset Timestamp);

    /// <summary>
    ///     <see cref="IActorMessageObserver" /> implementation backing <see cref="IDevHostControl.RecordedMessages" />.
    ///     Records messages received by actors into a bounded buffer — the multi-block analogue of TestKit's
    ///     <c>Verify*</c>, letting a test/agent assert which messages a block actually received (RFC 0003).
    /// </summary>
    public sealed class MessageTap : IActorMessageObserver
    {
        private const int Capacity = 5000;

        private readonly ConcurrentQueue<TappedMessage> _messages = new();

        public void OnReceived(string actorName, object message)
        {
            _messages.Enqueue(new TappedMessage(actorName, message.GetType().Name, message, DateTimeOffset.UtcNow));
            while (_messages.Count > Capacity && _messages.TryDequeue(out _))
            {
                // Bounded — drop oldest.
            }
        }

        /// <summary>The message tap records messages on receipt only; handler outcomes are not tapped.</summary>
        public void OnHandled(string actorName, object message, TimeSpan elapsed, Exception? exception)
        {
        }

        /// <summary>All captured messages, optionally filtered to those received by <paramref name="actorName" />.</summary>
        public IReadOnlyList<TappedMessage> Snapshot(string? actorName = null)
        {
            var all = _messages.ToArray();
            return actorName is null ? all : all.Where(m => m.ActorName == actorName).ToList();
        }
    }
}