using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.ProtoActor.Test.TestHelpers
{
    /// <summary>
    ///     The receivers the pipeline's suites spawn onto a real actor system. Each is the smallest shape that
    ///     makes one of the pipeline's guarantees observable: a receiver that answers, one that never does, one
    ///     that answers twice, one that throws, and one that records what it was handed.
    /// </summary>
    public sealed class AcknowledgingReceiver : IActorReceiver
    {
        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            if (message is StopLogicBlockRequest)
            {
                actorContext.RespondToSender(new StopLogicBlockResponse());
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>A receiver that acknowledges nothing, so a wait on it runs to its timeout.</summary>
    public sealed class SilentReceiver : IActorReceiver
    {
        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            return Task.CompletedTask;
        }
    }

    /// <summary>
    ///     A receiver that answers one request twice. Its second acknowledgement is the stray message that used
    ///     to satisfy a silent peer's share of an acknowledgement wait.
    /// </summary>
    public sealed class DoubleAcknowledgingReceiver : IActorReceiver
    {
        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            if (message is StopLogicBlockRequest)
            {
                actorContext.RespondToSender(new StopLogicBlockResponse());
                actorContext.RespondToSender(new StopLogicBlockResponse());
            }

            return Task.CompletedTask;
        }
    }

    /// <summary>A receiver whose handler always throws, so the middleware's containment is observable.</summary>
    public sealed class ThrowingReceiver : IActorReceiver
    {
        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            throw new InvalidOperationException("probe receiver refused the message");
        }
    }

    /// <summary>
    ///     A receiver that records every message it was handed and signals a waiter, so a test can await
    ///     delivery instead of timing it.
    /// </summary>
    public sealed class RecordingReceiver : IActorReceiver
    {
        public ConcurrentQueue<object> Received { get; } = new();

        public SemaphoreSlim Delivered { get; } = new(0);

        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            Received.Enqueue(message);
            Delivered.Release();
            return Task.CompletedTask;
        }
    }
}