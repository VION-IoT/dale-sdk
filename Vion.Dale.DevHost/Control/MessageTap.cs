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
    ///     A handler exception the actor middleware caught, recorded per receiving actor. The middleware
    ///     swallows a throw so one bad block cannot take the network down — which is why a block that failed to
    ///     configure, to bind or to start otherwise leaves no trace but a log line, and the host reports itself
    ///     started over it. <c>MessageType</c> says which message the block was handling when it threw.
    /// </summary>
    public sealed record BlockFailure(string LogicBlock, string MessageType, string Error, DateTimeOffset Timestamp);

    /// <summary>
    ///     <see cref="IActorMessageObserver" /> implementation backing <see cref="IDevHostControl.RecordedMessages" />.
    ///     Records messages received by actors into a bounded buffer — the multi-block analogue of TestKit's
    ///     <c>Verify*</c>, letting a test/agent assert which messages a block actually received (RFC 0003).
    /// </summary>
    public sealed class MessageTap : IActorMessageObserver
    {
        private const int Capacity = 5000;

        // Bounded like the message buffer, and far smaller: a host with hundreds of distinct handler failures
        // has one story to tell, not five thousand.
        private const int FailureCapacity = 200;

        private readonly ConcurrentQueue<BlockFailure> _failures = new();

        private readonly ConcurrentQueue<TappedMessage> _messages = new();

        public void OnReceived(string actorName, object message)
        {
            _messages.Enqueue(new TappedMessage(actorName, message.GetType().Name, message, DateTimeOffset.UtcNow));
            while (_messages.Count > Capacity && _messages.TryDequeue(out _))
            {
                // Bounded — drop oldest.
            }
        }

        /// <summary>
        ///     A handled message's outcome. Only a failure is kept: the middleware catches a handler's throw and
        ///     carries on, so without this the sole evidence that a block failed to configure, bind or start is
        ///     one line in the log stream, and the host reports itself started over it.
        /// </summary>
        public void OnHandled(string actorName, object message, TimeSpan elapsed, Exception? exception)
        {
            if (exception is null)
            {
                return;
            }

            _failures.Enqueue(new BlockFailure(actorName, message.GetType().Name, exception.Message, DateTimeOffset.UtcNow));
            while (_failures.Count > FailureCapacity && _failures.TryDequeue(out _))
            {
                // Bounded - drop oldest.
            }
        }

        /// <summary>Every recorded handler failure, optionally filtered to one receiving actor.</summary>
        public IReadOnlyList<BlockFailure> Failures(string? actorName = null)
        {
            var all = _failures.ToArray();
            return actorName is null ? all : all.Where(f => f.LogicBlock == actorName).ToList();
        }

        /// <summary>All captured messages, optionally filtered to those received by <paramref name="actorName" />.</summary>
        public IReadOnlyList<TappedMessage> Snapshot(string? actorName = null)
        {
            var all = _messages.ToArray();
            return actorName is null ? all : all.Where(m => m.ActorName == actorName).ToList();
        }
    }
}