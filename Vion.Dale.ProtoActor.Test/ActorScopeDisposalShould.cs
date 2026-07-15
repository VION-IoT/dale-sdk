using System;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.ProtoActor.Extensions;
using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.ProtoActor.Test
{
    /// <summary>
    ///     An actor spawned from DI must have its DI-resolved <see cref="IDisposable" /> dependencies reclaimed
    ///     when the actor stops, so a same-version redeploy does not strand per-block resources (RFC 0018 /
    ///     DF-46 — the Modbus client socket leak). The actor is resolved from a per-actor DI scope disposed on
    ///     the actor's terminal <c>Stopped</c>.
    /// </summary>
    [TestClass]
    public class ActorScopeDisposalShould
    {
        [TestMethod]
        public async Task DisposeADiResolvedDependency_WhenTheActorStops()
        {
            var services = new ServiceCollection();
            services.AddLogging();
            services.AddProtoActorSystem();

            var tracker = new DisposalTracker();
            services.AddSingleton(tracker);
            services.AddTransient<ScopedResource>();

            await using var provider = services.BuildServiceProvider();
            var actorSystem = provider.GetRequiredService<IActorSystem>();

            var actorRef = actorSystem.CreateRootActorFromDi<ProbeReceiver>("probe");
            await actorSystem.StopActorsAndWaitAsync([actorRef], TimeSpan.FromSeconds(5));

            Assert.AreEqual(1,
                            Volatile.Read(ref tracker.DisposeCount),
                            "The actor's DI-resolved IDisposable dependency must be disposed when the actor stops (per-actor DI scope, RFC 0018).");
        }

        private sealed class DisposalTracker
        {
            public int DisposeCount;
        }

        // A transient IDisposable the receiver takes as a constructor dependency — the stand-in for
        // ILogicBlockModbusTcpClient. It is created while building the receiver, so it is tracked for disposal
        // by whatever provider/scope resolved the receiver: the root container today (leak), a per-actor scope
        // after the fix.
        private sealed class ScopedResource : IDisposable
        {
            private readonly DisposalTracker _tracker;

            public ScopedResource(DisposalTracker tracker)
            {
                _tracker = tracker;
            }

            public void Dispose()
            {
                Interlocked.Increment(ref _tracker.DisposeCount);
            }
        }

        private sealed class ProbeReceiver : IActorReceiver
        {
            public ProbeReceiver(ScopedResource resource)
            {
                _ = resource;
            }

            public Task HandleMessageAsync(object message, IActorContext actorContext)
            {
                return Task.CompletedTask;
            }
        }
    }
}