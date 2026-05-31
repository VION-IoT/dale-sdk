using System;
using System.Linq;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     The flagship scenario the headless surface exists for (RFC 0003): a real two-block wired network
    ///     where one block polls another. A single-SUT test can't catch a missing poll (it injects the
    ///     response by hand); this boots both blocks and asserts the cross-block message actually flowed —
    ///     observed both as the sink's state change AND via the message tap.
    /// </summary>
    [TestClass]
    public class CrossBlockMessagingShould
    {
        [TestMethod]
        public async Task DeliverPollFromSourceToSink_ObservableViaStateAndTap()
        {
            var config = DevConfigurationBuilder.Create()
                                                .AddLogicBlock<SourceBlock>("source")
                                                .AddLogicBlock<SinkBlock>("sink")
                                                .AutoConnect()
                                                .Build();

            await using var host = DevHostBuilder.Create()
                                                 .WithDi<CrossBlockDependencyInjection>()
                                                 .WithConfiguration(config)
                                                 .Build();
            await host.StartAsync();

            // The source sends one Poll to the sink in Starting(); the sink increments ReceivedPolls when it
            // handles it. That interaction can complete during StartAsync (before a WaitForAsync waiter is even
            // registered), so we poll the cached value — WaitForAsync observes only *future* events and is the
            // right tool for ongoing/periodic changes (see the counter test), not a one-shot startup interaction.
            var polls = await PollUntilAsync(() => host.Control.GetProperty("sink", "ReceivedPolls"),
                                             v => v is not null && Convert.ToInt32(v) >= 1,
                                             timeout: TimeSpan.FromSeconds(15));

            Assert.IsNotNull(polls, "The sink should have received and handled the poll from the source.");
            Assert.AreEqual(1, Convert.ToInt32(polls));

            // And the message tap must have captured the inter-block request arriving at the sink's actor —
            // the diagnostic that would reveal a missing/stubbed poll ("the sink received nothing").
            var sinkMessages = host.Control.RecordedMessages("sink");
            Assert.IsNotEmpty(sinkMessages, "The tap should have captured messages received by the sink.");
            Assert.IsTrue(sinkMessages.Any(m => m.MessageType.Contains("FunctionInterface", StringComparison.OrdinalIgnoreCase)
                                                || (m.Message.ToString() ?? "").Contains("Poll", StringComparison.OrdinalIgnoreCase)),
                          "The tap should show the cross-block Poll request arriving at the sink.");
        }

        private static async Task<object?> PollUntilAsync(Func<object?> read, Func<object?, bool> predicate, TimeSpan timeout)
        {
            var deadline = DateTime.UtcNow + timeout;
            while (DateTime.UtcNow < deadline)
            {
                var value = read();
                if (predicate(value))
                {
                    return value;
                }

                await Task.Delay(50);
            }

            return null;
        }
    }
}
