using System;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Proto;
using Proto.Mailbox;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.ProtoActor
{
    public class Actor<TActorReceiver> : IActor
        where TActorReceiver : IActorReceiver
    {
        private readonly TActorReceiver _actorReceiver;

        private readonly IDelayedSendGate? _delayedSendGate;

        private readonly IVirtualSchedule? _schedule;

        // The per-actor DI scope the receiver was resolved from, owned by this actor and disposed on its
        // terminal Stopped, so the receiver's IDisposable dependencies (a per-block Modbus/HTTP client) are
        // reclaimed when the actor stops instead of being pinned on the root container (docs/specs/block-lifecycle.md).
        // Null for actors created from a caller-supplied instance without a scope.
        private readonly IServiceScope? _scope;

        private readonly TimeProvider _timeProvider;

        public Actor(TActorReceiver actorReceiver,
                     IDelayedSendGate? delayedSendGate = null,
                     TimeProvider? timeProvider = null,
                     IVirtualSchedule? schedule = null,
                     IServiceScope? scope = null)
        {
            _actorReceiver = actorReceiver;
            _delayedSendGate = delayedSendGate;
            _timeProvider = timeProvider ?? TimeProvider.System;
            _schedule = schedule;
            _scope = scope;
        }

        /// <inheritdoc />
        public async Task ReceiveAsync(IContext context)
        {
            switch (context.Message)
            {
                case Stopped:
                    // The actor has terminated. Dispose the per-actor DI scope so its resolved IDisposable
                    // dependencies are reclaimed. The receiver's own teardown (e.g. LogicBlockBase.Stopping(),
                    // which is driven by the domain stop request and runs before the actor is Proto-stopped)
                    // has returned by this point — but that only means it stopped executing, not that a device
                    // write it issued was delivered. Stopping() can merely enqueue such a write, and disposing
                    // the scope here disposes the client that owns the queue, cancelling both what is queued and
                    // the request in flight. The runtime therefore grants a bounded grace period before it
                    // terminates the actors (DevHost deliberately does not — see DevLogicSystemInitializer.StopAsync),
                    // so a just-issued safe-baseline write has a chance to reach the wire. That window is
                    // best-effort: it buys time, it does not guarantee delivery.
                    _scope?.Dispose();
                    break;

                case null:
                case SystemMessage:
                    break;

                default:
                    await _actorReceiver.HandleMessageAsync(context.Message, new ActorContext(() => context, _delayedSendGate, _timeProvider, _schedule));
                    break;
            }
        }
    }
}