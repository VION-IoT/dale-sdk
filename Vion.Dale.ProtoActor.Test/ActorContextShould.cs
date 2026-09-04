using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.Sdk;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using IActorSystem = Vion.Dale.Sdk.Abstractions.IActorSystem;

namespace Vion.Dale.ProtoActor.Test
{
    /// <summary>
    ///     What an actor's context carries on the messages a handler sends, and what it refuses. The claims
    ///     are about delivery — a message reaching a peer with its sender intact, a delayed self-send coming
    ///     back — so every test runs over a real actor system and awaits the arrival rather than timing it.
    /// </summary>
    [TestClass]
    public sealed class ActorContextShould
    {
        private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.5")]
        public async Task CarrySendersOwnReferenceOnMessageItSends()
        {
            // Arrange
            await using var host = new PipelineHost();
            var answerer = host.System.CreateRootActorFor(() => new AnsweringReceiver(), "context_answerer");
            var asker = new AskingReceiver(answerer);
            var askerActor = host.System.CreateRootActorFor(() => asker, "context_asker");

            // Act
            host.System.SendTo(askerActor, new StartLogicBlockRequest());
            await asker.Answered.WaitAsync(Generous);

            // Assert
            Assert.IsTrue(asker.Received.Any(message => message is StopLogicBlockResponse),
                          "The answer came back, which it can only do because the send carried the asker's own reference.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.5")]
        public async Task CarryHeadersOfMessageBeingHandledOntoMessageItSends()
        {
            // Arrange — three hops. Only the first supplies a header; the second forwards without one, so
            // the header the sink reads can only have been inherited from the message being handled.
            await using var host = new PipelineHost();
            var recorder = new HeaderRecordingReceiver();
            var sink = host.System.CreateRootActorFor(() => recorder, "context_header_sink");
            var forwarder = host.System.CreateRootActorFor(() => new ForwardingReceiver(sink), "context_header_forwarder");
            var originator = host.System.CreateRootActorFor(() => new HeaderOriginatingReceiver(forwarder), "context_header_originator");

            // Act
            host.System.SendTo(originator, new StartLogicBlockRequest());
            await recorder.Arrived.WaitAsync(Generous);

            // Assert
            Assert.IsNotNull(recorder.Headers, "A forwarded message carries the headers of the message being handled, so a correlation follows a chain of blocks.");
            Assert.AreEqual("abc", recorder.Headers!["correlation"]);
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.7")]
        public async Task CarryNeitherSenderNorHeadersOnMessageActorSendsItself()
        {
            // Arrange — the first hop supplies a header, so an empty one on the second can only mean the
            // self-send dropped it rather than that there was nothing to drop.
            await using var host = new PipelineHost();
            var receiver = new SelfSendingReceiver();
            var actor = host.System.CreateRootActorFor(() => receiver, "context_self_send_shape");
            var originator = host.System.CreateRootActorFor(() => new HeaderOriginatingReceiver(actor), "context_self_send_originator");

            // Act
            host.System.SendTo(originator, new StartLogicBlockRequest());
            await receiver.CameBack.WaitAsync(Generous);

            // Assert
            Assert.IsEmpty(receiver.HeadersOnSelfSend!,
                           "A message an actor sends itself is a bare send, so a correlation header does not survive a dispatcher action, a timer tick or a periodic save.");
            Assert.IsInstanceOfType<InvalidOperationException>(receiver.Failure, "And it carries no sender either, so a handler reached that way cannot answer.");
            Assert.AreEqual("abc", receiver.HeadersOnArrival!["correlation"], "Pre-condition: the message the handler was handling did carry one.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-014.6")]
        public async Task RefuseAnswerToMessageThatCarriesNoSender()
        {
            // Arrange
            await using var host = new PipelineHost();
            var receiver = new AnsweringReceiver();
            var actor = host.System.CreateRootActorFor(() => receiver, "context_no_sender");

            // Act — a fire-and-forget send carries no sender, unlike a request.
            host.System.SendTo(actor, new StopLogicBlockRequest());
            await receiver.Attempted.WaitAsync(Generous);

            // Assert
            Assert.IsInstanceOfType<InvalidOperationException>(receiver.Failure, "There is nobody to answer, so the answer is refused rather than sent nowhere.");
            StringAssert.Contains(receiver.Failure!.Message, "no sender", "The refusal says what was missing.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-006.1")]
        public async Task DeliverDelayedSelfSendBackToSameActor()
        {
            // Arrange
            await using var host = new PipelineHost();
            var receiver = new SelfSchedulingReceiver();
            var actor = host.System.CreateRootActorFor(() => receiver, "context_self_send");

            // Act
            host.System.SendTo(actor, new StartLogicBlockRequest());
            await receiver.CameBack.WaitAsync(Generous);

            // Assert
            Assert.IsTrue(receiver.RanOnItsOwnActor, "A scheduled action comes back as a message to the same actor, which is what makes a block's state safe to touch from it.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-LIFE-017.1")]
        public async Task SendToMintedReferenceWithoutFailingSender()
        {
            // Arrange
            await using var host = new PipelineHost();
            var receiver = new GhostSendingReceiver();
            var actor = host.System.CreateRootActorFor(() => receiver, "context_ghost");

            // Act
            host.System.SendTo(actor, new StartLogicBlockRequest());
            await receiver.Sent.WaitAsync(Generous);

            // Assert
            Assert.IsNull(receiver.Failure,
                          "A block links to its handler by name before that handler is known to exist, so the send must not fail — the message becomes a dead letter instead.");
        }

        private sealed class AnsweringReceiver : IActorReceiver
        {
            public SemaphoreSlim Attempted { get; } = new(0);

            public Exception? Failure { get; private set; }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                if (message is not StopLogicBlockRequest)
                {
                    return Task.CompletedTask;
                }

                try
                {
                    actorContext.RespondToSender(new StopLogicBlockResponse());
                }
                catch (Exception exception)
                {
                    Failure = exception;
                }

                Attempted.Release();
                return Task.CompletedTask;
            }
        }

        private sealed class AskingReceiver : IActorReceiver
        {
            private readonly IActorReference _peer;

            public ConcurrentQueue<object> Received { get; } = new();

            public SemaphoreSlim Answered { get; } = new(0);

            public AskingReceiver(IActorReference peer)
            {
                _peer = peer;
            }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                if (message is StartLogicBlockRequest)
                {
                    actorContext.SendTo(_peer, new StopLogicBlockRequest());
                    return Task.CompletedTask;
                }

                Received.Enqueue(message);
                Answered.Release();
                return Task.CompletedTask;
            }
        }

        /// <summary>The one hop that supplies a header of its own.</summary>
        private sealed class HeaderOriginatingReceiver : IActorReceiver
        {
            private readonly IActorReference _next;

            public HeaderOriginatingReceiver(IActorReference next)
            {
                _next = next;
            }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                if (message is StartLogicBlockRequest)
                {
                    actorContext.SendTo(_next, new StartLogicBlockRequest(), new Dictionary<string, string> { ["correlation"] = "abc" });
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>Forwards with no headers of its own, so only what it inherits can reach the sink.</summary>
        private sealed class ForwardingReceiver : IActorReceiver
        {
            private readonly IActorReference _sink;

            public ForwardingReceiver(IActorReference sink)
            {
                _sink = sink;
            }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                if (message is StartLogicBlockRequest)
                {
                    actorContext.SendTo(_sink, new StopLogicBlockRequest());
                }

                return Task.CompletedTask;
            }
        }

        private sealed class HeaderRecordingReceiver : IActorReceiver
        {
            public SemaphoreSlim Arrived { get; } = new(0);

            public IReadOnlyDictionary<string, string>? Headers { get; private set; }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                if (message is StopLogicBlockRequest)
                {
                    Headers = actorContext.Headers;
                    Arrived.Release();
                }

                return Task.CompletedTask;
            }
        }

        /// <summary>Sends itself a message while handling one that carries a header, and reads both back.</summary>
        private sealed class SelfSendingReceiver : IActorReceiver
        {
            public SemaphoreSlim CameBack { get; } = new(0);

            public IReadOnlyDictionary<string, string>? HeadersOnArrival { get; private set; }

            public IReadOnlyDictionary<string, string>? HeadersOnSelfSend { get; private set; }

            public Exception? Failure { get; private set; }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                switch (message)
                {
                    case StartLogicBlockRequest:
                        HeadersOnArrival = actorContext.Headers;
                        actorContext.SendToSelf(new StopLogicBlockRequest());
                        break;
                    case StopLogicBlockRequest:
                        HeadersOnSelfSend = actorContext.Headers;
                        try
                        {
                            actorContext.RespondToSender(new StopLogicBlockResponse());
                        }
                        catch (Exception exception)
                        {
                            Failure = exception;
                        }

                        CameBack.Release();
                        break;
                }

                return Task.CompletedTask;
            }
        }

        private sealed class SelfSchedulingReceiver : IActorReceiver
        {
            public SemaphoreSlim CameBack { get; } = new(0);

            public bool RanOnItsOwnActor { get; private set; }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                switch (message)
                {
                    case StartLogicBlockRequest:
                        actorContext.SendToSelfAfter(new StopLogicBlockRequest(), TimeSpan.Zero);
                        break;
                    case StopLogicBlockRequest:
                        RanOnItsOwnActor = true;
                        CameBack.Release();
                        break;
                }

                return Task.CompletedTask;
            }
        }

        private sealed class GhostSendingReceiver : IActorReceiver
        {
            public SemaphoreSlim Sent { get; } = new(0);

            public Exception? Failure { get; private set; }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                if (message is not StartLogicBlockRequest)
                {
                    return Task.CompletedTask;
                }

                try
                {
                    actorContext.SendTo(actorContext.LookupByName("an_actor_nobody_spawned"), new StopLogicBlockRequest());
                }
                catch (Exception exception)
                {
                    Failure = exception;
                }

                Sent.Release();
                return Task.CompletedTask;
            }
        }

        private sealed class PipelineHost : IAsyncDisposable
        {
            public ServiceProvider Provider { get; }

            public IActorSystem System { get; }

            public PipelineHost()
            {
                var services = new ServiceCollection();
                services.AddLogging(builder => builder.SetMinimumLevel(LogLevel.None));
                services.AddDaleSdk();
                services.AddProtoActorSystem();
                Provider = services.BuildServiceProvider();
                System = Provider.GetRequiredService<IActorSystem>();
            }

            public ValueTask DisposeAsync()
            {
                return Provider.DisposeAsync();
            }
        }
    }
}