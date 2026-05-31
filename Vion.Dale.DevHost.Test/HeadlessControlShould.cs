using System;
using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Control;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     End-to-end smoke tests for the headless in-process control surface (RFC 0003): boot a real wired
    ///     network with no web UI, drive it, and observe — the multi-block analogue of the TestKit loop.
    /// </summary>
    [TestClass]
    public class HeadlessControlShould
    {
        private static DevConfiguration Config()
        {
            return DevConfigurationBuilder.Create()
                                          .AddLogicBlock<CounterBlock>("counter")
                                          .Build();
        }

        private static IDevHost BuildHost()
        {
            return DevHostBuilder.Create()
                                 .WithDi<TestDependencyInjection>()
                                 .WithConfiguration(Config())
                                 .Build();
        }

        [TestMethod]
        public async Task ListBlocks_AfterStart_ReturnsTheConfiguredBlock()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var blocks = host.Control.ListBlocks();

            Assert.HasCount(1, blocks);
            Assert.AreEqual("counter", blocks[0].Name);
            Assert.AreEqual(nameof(CounterBlock), blocks[0].TypeName);
        }

        [TestMethod]
        public async Task SetProperty_IsObservableViaWaitFor_AndReadBack()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            await host.Control.SetPropertyAsync("counter", "Counter", 42);

            // Match the specific target value — the startup state publish also emits Counter (=0).
            var observed = await host.Control.WaitForAsync(
                e => e is ServicePropertyChanged { Property: "Counter" } sp && Convert.ToInt32(sp.Value) == 42 ? sp.Value : null,
                timeout: TimeSpan.FromSeconds(5));

            Assert.IsNotNull(observed, "The Counter=42 change should have been observed within the timeout.");
            Assert.AreEqual(42, Convert.ToInt32(observed));
            Assert.AreEqual(42, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")));
        }

        [TestMethod]
        public async Task WaitFor_ReturnsNull_OnTimeout_WhenNothingMatches()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var observed = await host.Control.WaitForAsync(
                e => e is ServicePropertyChanged { Property: "DoesNotExist" } sp ? sp.Value : null,
                timeout: TimeSpan.FromMilliseconds(200));

            Assert.IsNull(observed);
        }

        [TestMethod]
        public async Task RecentLogs_CaptureTheBootSequence()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var logs = host.Control.RecentLogs();

            Assert.IsNotEmpty(logs, "The boot sequence should have produced captured log lines.");
            Assert.IsTrue(logs.Any(l => l.Message.Contains("logic", StringComparison.OrdinalIgnoreCase)
                                        || l.Message.Contains("LogicBlock", StringComparison.OrdinalIgnoreCase)),
                          "Expected at least one DevHost boot log line to be captured.");
        }
    }
}
