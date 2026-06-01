using System;
using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Control;
using Vion.Dale.Sdk.Messages;

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
        public async Task ListLogicBlocks_AfterStart_ReturnsTheConfiguredBlock()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var logicBlocks = host.Control.ListLogicBlocks();

            Assert.HasCount(1, logicBlocks);
            Assert.AreEqual("counter", logicBlocks[0].Name);
            Assert.AreEqual(nameof(CounterBlock), logicBlocks[0].TypeName);
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
                timeout: TimeSpan.FromSeconds(15));

            Assert.IsNotNull(observed, "The Counter=42 change should have been observed within the timeout.");
            Assert.AreEqual(42, Convert.ToInt32(observed));
            Assert.AreEqual(42, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")));
        }

        [TestMethod]
        public async Task ReadComputedMeasuringPoint_AfterSettingAProperty()
        {
            // Measuring points (read-only computed metrics) are first-class on the control surface: setting
            // Counter computes CounterDoubled, and the headless surface must expose it for asserting calculations.
            await using var host = BuildHost();
            await host.StartAsync();

            await host.Control.SetPropertyAsync("counter", "Counter", 21);

            var doubled = await host.Control.WaitForAsync(
                e => e is ServiceMeasuringPointChanged { MeasuringPoint: "CounterDoubled" } mp && Convert.ToInt32(mp.Value) == 42 ? mp.Value : null,
                timeout: TimeSpan.FromSeconds(15));

            Assert.IsNotNull(doubled, "The computed measuring point should have been observed.");
            Assert.AreEqual(42, Convert.ToInt32(doubled));
            Assert.AreEqual(42, Convert.ToInt32(host.Control.GetProperty("counter", "CounterDoubled")), "GetProperty must read measuring points too.");
        }

        [TestMethod]
        public async Task GetConfiguration_AfterStart_DescribesTheWiredNetworkWithSchemas()
        {
            // The heavyweight introspection (what the web UI renders) is reachable in-process through the one
            // control abstraction — agents can read property/measuring-point schemas without standing up the
            // web stack. This is the capability the collapsed IDevHostStateProvider used to gate behind the web.
            await using var host = BuildHost();
            await host.StartAsync();

            var config = host.Control.GetConfiguration();

            Assert.IsNotNull(config);
            var block = config.LogicBlocks.Single(b => b.Name == "counter");
            var service = block.Services.Single();

            var counter = service.ServiceProperties.Single(p => p.Identifier == "Counter");
            Assert.IsNotNull(counter.Schema, "Each service property must carry its JSON schema.");
            Assert.IsTrue(service.ServiceMeasuringPoints.Any(m => m.Identifier == "CounterDoubled"),
                          "The computed measuring point must be described in the configuration.");
        }

        [TestMethod]
        public async Task SetServicePropertyValueAsync_ByServiceId_DecodesAJsonValue()
        {
            // The HTTP set path addresses a property by its service id and arrives as JSON. The unified control
            // must decode that JSON against the property schema into the precise CLR type — exercise that branch
            // directly with a JsonNode so the conversion is covered without the web stack in the loop.
            await using var host = BuildHost();
            await host.StartAsync();

            var serviceId = host.Control.GetConfiguration()
                                .LogicBlocks.Single(b => b.Name == "counter")
                                .Services.Single(s => s.ServiceProperties.Any(p => p.Identifier == "Counter"))
                                .Id;

            await host.Control.SetServicePropertyValueAsync(serviceId, "Counter", System.Text.Json.Nodes.JsonValue.Create(99));

            var observed = await host.Control.WaitForAsync(
                e => e is ServicePropertyChanged { Property: "Counter" } sp && Convert.ToInt32(sp.Value) == 99 ? sp.Value : null,
                timeout: TimeSpan.FromSeconds(15));

            Assert.IsNotNull(observed, "Setting by service id with a JSON value should be applied.");
            Assert.AreEqual(99, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")));
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
        public async Task RecordedMessages_CaptureWhatTheBlockReceived()
        {
            // The message tap (opt-in ProtoActor observer) records messages each actor receives. Driving a
            // property set sends a SetServicePropertyValueRequest to the block's actor; the tap must capture
            // it under that block. This is the mechanism behind "assert device-x received a DataRequest".
            await using var host = BuildHost();
            await host.StartAsync();

            await host.Control.SetPropertyAsync("counter", "Counter", 7);
            await host.Control.WaitForAsync(
                e => e is ServicePropertyChanged { Property: "Counter" } sp && Convert.ToInt32(sp.Value) == 7 ? sp.Value : null,
                timeout: TimeSpan.FromSeconds(15));

            var received = host.Control.RecordedMessages("counter");

            Assert.IsNotEmpty(received, "The tap should have recorded messages the counter block received.");
            Assert.IsTrue(received.Any(m => m.Message is SetServicePropertyValueRequest),
                          "The set-property request the block received should have been captured by the tap.");
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
