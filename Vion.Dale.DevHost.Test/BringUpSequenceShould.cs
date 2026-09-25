using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Vion.Dale.DevHost.Test.Stepping;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The order a host brings a configuration up in, as it reaches each logic block: its configuration, its
    ///     runtime-actor link and, where it has links, its linked-interface map before its restore, and its
    ///     restore before its start. A block's start hook is written against that order — its restored values
    ///     and its links are in place by then. The host is a real built one with two linked blocks and one
    ///     unlinked, and a message observer records what each block's actor receives, in the order its mailbox
    ///     delivers it; both clock modes, as <c>TeardownStopSequenceShould</c> runs the way down.
    /// </summary>
    [TestClass]
    public class BringUpSequenceShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-HOST-004.1")]
        [DataRow(false, DisplayName = "free-running clock")]
        [DataRow(true, DisplayName = "stepped clock")]
        public async Task DeliverConfigurationAndRuntimeLinkBeforeRestoreAndRestoreBeforeStart(bool stepped)
        {
            // Arrange
            var recorder = new BringUpRecorder();
            await using var host = BuildHost(recorder, stepped, out var blocks, out _);

            // Act
            await host.StartAsync();

            // Assert
            foreach (var block in blocks)
            {
                var received = recorder.ReceivedBy(block);
                var restore = received.IndexOf(nameof(RestorePersistentDataRequest));
                var configure = received.IndexOf(nameof(InitializeLogicBlock));
                var link = received.IndexOf(nameof(LinkRuntimeActors));
                var order = $"{block} received: {string.Join(", ", received)}";

                Assert.IsGreaterThanOrEqualTo(0, configure, order);
                Assert.IsGreaterThanOrEqualTo(0, link, order);
                Assert.IsLessThan(restore, configure, order);
                Assert.IsLessThan(restore, link, order);
                Assert.IsLessThan(received.IndexOf(nameof(StartLogicBlockRequest)), restore, order);
            }
        }

        [TestMethod]
        [TestProperty("spec", "AC-HOST-004.1")]
        [DataRow(false, DisplayName = "free-running clock")]
        [DataRow(true, DisplayName = "stepped clock")]
        public async Task DeliverLinkedInterfaceMapBeforeRestore(bool stepped)
        {
            // Arrange
            var recorder = new BringUpRecorder();
            await using var host = BuildHost(recorder, stepped, out _, out var linkedBlocks);

            // Act
            await host.StartAsync();

            // Assert
            foreach (var block in linkedBlocks)
            {
                var received = recorder.ReceivedBy(block);
                var map = received.IndexOf(nameof(SetLinkedInterfaces));
                var order = $"{block} received: {string.Join(", ", received)}";

                Assert.IsGreaterThanOrEqualTo(0, map, order);
                Assert.IsLessThan(received.IndexOf(nameof(RestorePersistentDataRequest)), map, order);
            }
        }

        private static IDevHost BuildHost(BringUpRecorder recorder, bool stepped, out IReadOnlyList<string> blocks, out IReadOnlyList<string> linkedBlocks)
        {
            var configuration = DevConfigurationBuilder.Create()
                                                       .WithTopologyName("bring-up")
                                                       .AddLogicBlock<FanSourceA>("A", out var source)
                                                       .AddLogicBlock<FanAggregatorBlock>("Aggregator", out var sink)
                                                       .AddLogicBlock<FanSourceB>("Lone", out var lone)
                                                       .Connect(source, sink)
                                                       .Build();
            linkedBlocks = [ActorName(source), ActorName(sink)];
            blocks = [ActorName(source), ActorName(sink), ActorName(lone)];

            var builder = DevHostBuilder.Create()
                                        .WithDi<FanInDependencyInjection>()
                                        .WithConfiguration(configuration)
                                        .ConfigureServices(services => services.AddSingleton<IActorMessageObserver>(recorder));

            return (stepped ? builder.WithDeterministicStepping() : builder).Build();
        }

        private static string ActorName(LogicBlockHandle handle)
        {
            return LogicBlockUtils.CreateLogicBlockName(handle.Name, handle.Id);
        }

        private sealed class BringUpRecorder : IActorMessageObserver
        {
            private readonly ConcurrentQueue<(string Actor, string Message)> _entries = new();

            public List<string> ReceivedBy(string actor)
            {
                return _entries.Where(entry => entry.Actor == actor).Select(entry => entry.Message).ToList();
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
