using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.DevHost.Test.Stepping;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The order a host brings a configuration up in, as it reaches each logic block: its configuration, its
    ///     runtime-actor link and its linked-interface map before its restore, and its restore before its start.
    ///     A block's start hook is written against that order — its restored values and its links are in place
    ///     by then. The host is a real built one with two linked blocks, and a message observer records what
    ///     each block's actor receives, in the order its mailbox delivers it; both clock modes, as
    ///     <c>TeardownStopSequenceShould</c> runs the way down.
    /// </summary>
    [TestClass]
    public class BringUpSequenceShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-HOST-004.1")]
        [DataRow(false, DisplayName = "free-running clock")]
        [DataRow(true, DisplayName = "stepped clock")]
        public async Task DeliverConfigurationAndLinksBeforeRestoreAndRestoreBeforeStart(bool stepped)
        {
            // Arrange
            var recorder = new BringUpRecorder();
            var configuration = DevConfigurationBuilder.Create()
                                                       .WithTopologyName("bring-up")
                                                       .AddLogicBlock<FanSourceA>("A", out var source)
                                                       .AddLogicBlock<FanAggregatorBlock>("Aggregator", out var sink)
                                                       .Connect(source, sink)
                                                       .Build();
            var builder = DevHostBuilder.Create()
                                        .WithDi<FanInDependencyInjection>()
                                        .WithConfiguration(configuration)
                                        .ConfigureServices(services => services.AddSingleton<IActorMessageObserver>(recorder));
            if (stepped)
            {
                builder = builder.WithDeterministicStepping();
            }

            await using var host = builder.Build();

            // Act
            await host.StartAsync();

            // Assert
            var blocks = recorder.Entries.Select(entry => entry.Actor).Distinct().ToList();
            Assert.HasCount(2, blocks, "Both linked blocks must receive the bring-up messages.");
            foreach (var block in blocks)
            {
                var received = recorder.Entries.Where(entry => entry.Actor == block).Select(entry => entry.Message).ToList();
                var restore = received.IndexOf(nameof(RestorePersistentDataRequest));
                var start = received.IndexOf(nameof(StartLogicBlockRequest));
                var order = $"{block} received: {string.Join(", ", received)}";

                Assert.IsGreaterThanOrEqualTo(0, restore, order);
                Assert.IsLessThan(restore, received.IndexOf(nameof(InitializeLogicBlock)), order);
                Assert.IsLessThan(restore, received.IndexOf(nameof(LinkRuntimeActors)), order);
                Assert.IsLessThan(restore, received.IndexOf(nameof(SetLinkedInterfaces)), order);
                Assert.IsLessThan(start, restore, order);
            }
        }

        private sealed class BringUpRecorder : IActorMessageObserver
        {
            private readonly ConcurrentQueue<(string Actor, string Message)> _entries = new();

            public IReadOnlyList<(string Actor, string Message)> Entries
            {
                get => _entries.ToArray();
            }

            public void OnReceived(string actorName, object message)
            {
                if (message is InitializeLogicBlock or LinkRuntimeActors or SetLinkedInterfaces or RestorePersistentDataRequest or StartLogicBlockRequest)
                {
                    _entries.Enqueue((actorName, message.GetType().Name));
                }
            }

            public void OnHandled(string actorName, object message, TimeSpan elapsed, Exception? exception)
            {
            }
        }
    }
}
