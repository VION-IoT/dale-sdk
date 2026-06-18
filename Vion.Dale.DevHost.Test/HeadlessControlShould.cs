using System;
using System.Linq;
using System.Text.Json.Nodes;
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

            // Register the observer BEFORE triggering the change — WaitForAsync observes only future events.
            // Match the specific target value; the startup state publish also emits Counter (=0).
            var observe = host.Control.WaitForAsync(e => e is ServicePropertyChanged { Property: "Counter" } sp && Convert.ToInt32(sp.Value) == 42 ? sp.Value : null,
                                                    TimeSpan.FromSeconds(15));

            await host.Control.SetPropertyAsync("counter", "Counter", 42);

            var observed = await observe;
            Assert.IsNotNull(observed, "The Counter=42 change should have been observed.");
            Assert.AreEqual(42, Convert.ToInt32(observed));
            Assert.AreEqual(42, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")));
        }

        [TestMethod]
        public async Task SetPropertyAsync_AwaitsApply_SoReadAfterWriteReflectsTheNewValue()
        {
            // Regression (in-process set silent no-op): SetPropertyAsync must complete only after the value is
            // applied AND published, so an immediate GetProperty returns the new value instead of racing the
            // actor. Before the fix the set was fire-and-forget, so `await Set; Get` returned the stale value
            // for every type (int/enum/double/TimeSpan) — which read as a silent no-op.
            await using var host = BuildHost();
            await host.StartAsync();

            // Deliberately written IMMEDIATELY after StartAsync, racing the block's initial startup
            // publishes: the ack is correlated with the write's own round trip (the block's response),
            // so a stale in-flight publish can never satisfy it — the regression a change-event-based
            // ack had (CI caught it: ack in 18 ms, read 0).
            await host.Control.SetPropertyAsync("counter", "Counter", 99);
            Assert.AreEqual(99, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")), "int read-after-write must be immediate after the await.");

            await host.Control.SetPropertyAsync("counter", "ControlInterval", TimeSpan.FromSeconds(60));
            Assert.AreEqual(TimeSpan.FromSeconds(60),
                            (TimeSpan)host.Control.GetProperty("counter", "ControlInterval")!,
                            "TimeSpan read-after-write must be immediate after the await.");
        }

        [TestMethod]
        public async Task SetPropertyAsync_AcksNoOpWritesPromptly()
        {
            // A write that doesn't change the value raises no change event ([Observable] dedup); the ack
            // must come from the write's own round-trip response instead of riding out the 5 s timeout.
            await using var host = BuildHost();
            await host.StartAsync();

            await host.Control.SetPropertyAsync("counter", "Counter", 7);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await host.Control.SetPropertyAsync("counter", "Counter", 7);
            stopwatch.Stop();

            Assert.IsLessThan(2000, stopwatch.Elapsed.TotalMilliseconds, "a no-op write must ack on its response, not the timeout.");
            Assert.AreEqual(7, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")));
        }

        [TestMethod]
        public async Task ReadComputedMeasuringPoint_AfterSettingAProperty()
        {
            // Measuring points (read-only computed metrics) are first-class on the control surface: setting
            // Counter computes CounterDoubled, and the headless surface must expose it for asserting calculations.
            await using var host = BuildHost();
            await host.StartAsync();

            // CounterDoubled is a *downstream* change: SetPropertyAsync awaits the Counter property apply+publish,
            // but the measuring point is recomputed + published just after that, so an immediate GetProperty can
            // race it (and read 0). Register the observer before the set — WaitForAsync only sees future events —
            // and wait for the measuring-point publish, the same pattern as SetProperty_IsObservableViaWaitFor_AndReadBack.
            var doubled = host.Control.WaitForAsync(e => e is ServiceMeasuringPointChanged { MeasuringPoint: "CounterDoubled" } mp && Convert.ToInt32(mp.Value) == 42 ? mp.Value :
                                                             null,
                                                    TimeSpan.FromSeconds(15));

            await host.Control.SetPropertyAsync("counter", "Counter", 21);

            Assert.IsNotNull(await doubled, "CounterDoubled = Counter * 2 should have been published after setting Counter.");

            // GetProperty reads computed measuring points too; the value cache is updated before the publish above.
            Assert.AreEqual(42, Convert.ToInt32(host.Control.GetProperty("counter", "CounterDoubled")), "CounterDoubled = Counter * 2 must be readable after setting Counter.");
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
            Assert.IsTrue(service.ServiceMeasuringPoints.Any(m => m.Identifier == "CounterDoubled"), "The computed measuring point must be described in the configuration.");
        }

        [TestMethod]
        public async Task GetConfiguration_BeforeStart_SelfIntrospects()
        {
            // Regression: the web server starts serving /api/configuration as part of host startup, and a
            // request can race in before DevHost.StartAsync has run introspection. BuildConfiguration must
            // self-initialize rather than throw KeyNotFoundException for the first block. Calling
            // GetConfiguration on a built-but-not-started host exercises exactly that defensive path.
            await using var host = BuildHost();

            var config = host.Control.GetConfiguration();

            Assert.IsNotNull(config);
            Assert.IsTrue(config.LogicBlocks.Any(b => b.Name == "counter"), "Configuration must describe the wired blocks even when reached before StartAsync.");
        }

        [TestMethod]
        public async Task SetServicePropertyValueAsync_ByServiceId_DecodesAJsonValue()
        {
            // The HTTP set path addresses a property by its service id and arrives as JSON. The unified control
            // must decode that JSON against the property schema into the precise CLR type — exercise that branch
            // directly with a JsonNode so the conversion is covered without the web stack in the loop.
            await using var host = BuildHost();
            await host.StartAsync();

            var serviceId = host.Control
                                .GetConfiguration()
                                .LogicBlocks
                                .Single(b => b.Name == "counter")
                                .Services
                                .Single(s => s.ServiceProperties.Any(p => p.Identifier == "Counter"))
                                .Id;

            await host.Control.SetServicePropertyValueAsync(serviceId, "Counter", JsonValue.Create(99));

            Assert.AreEqual(99, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")), "Setting by service id with a JSON value should decode + apply.");
        }

        [TestMethod]
        public async Task SetServicePropertyValueAsync_OnReadOnlyOrUnknownMember_ThrowsLoudly()
        {
            // Trip wire: writing a member the block can't apply — a read-only measuring point / [ServiceProperty]
            // with no public setter, or an unknown member name — used to look successful (the actor swallowed the
            // binder exception, the write ack timed out, and the HTTP path returned 200). The control surface must
            // reject such a write UP FRONT, loudly, on both the HTTP and scenario paths.
            await using var host = BuildHost();
            await host.StartAsync();

            var serviceId = host.Control
                                .GetConfiguration()
                                .LogicBlocks
                                .Single(b => b.Name == "counter")
                                .Services
                                .Single(s => s.ServiceMeasuringPoints.Any(m => m.Identifier == "CounterDoubled"))
                                .Id;

            // CounterDoubled is a [ServiceMeasuringPoint] — read-only.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.Control.SetServicePropertyValueAsync(serviceId, "CounterDoubled", JsonValue.Create(7)));

            // An unknown member name on a known service must also fail loudly, not silently no-op.
            await Assert.ThrowsExactlyAsync<InvalidOperationException>(() => host.Control.SetServicePropertyValueAsync(serviceId, "NoSuchMember", JsonValue.Create(7)));
        }

        [TestMethod]
        public async Task SetTimeSpanProperty_AcceptsBothDotNetAndIsoDurationFormats()
        {
            // TimeSpan maps to PrimitiveKind.Duration. The rich-types codec parses ISO-8601 ("PT5S") only,
            // but the web UI (and .NET habit) submit the .NET ToString form ("00:00:05"). The write path must
            // accept both, or every TimeSpan property is unwritable from the UI (FormatException → HTTP 500).
            await using var host = BuildHost();
            await host.StartAsync();

            var serviceId = host.Control
                                .GetConfiguration()
                                .LogicBlocks
                                .Single(b => b.Name == "counter")
                                .Services
                                .Single(s => s.ServiceProperties.Any(p => p.Identifier == "ControlInterval"))
                                .Id;

            // .NET TimeSpan form — what the web UI submits today.
            await host.Control.SetServicePropertyValueAsync(serviceId, "ControlInterval", JsonValue.Create("00:00:05"));
            Assert.AreEqual(TimeSpan.FromSeconds(5), (TimeSpan)host.Control.GetProperty("counter", "ControlInterval")!, "The .NET TimeSpan form (00:00:05) must be accepted.");

            // ISO-8601 duration — the codec/MQTT canonical form.
            await host.Control.SetServicePropertyValueAsync(serviceId, "ControlInterval", JsonValue.Create("PT10S"));
            Assert.AreEqual(TimeSpan.FromSeconds(10), (TimeSpan)host.Control.GetProperty("counter", "ControlInterval")!, "The ISO-8601 duration form (PT10S) must be accepted.");
        }

        [TestMethod]
        public async Task WaitFor_ReturnsNull_OnTimeout_WhenNothingMatches()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var observed = await host.Control.WaitForAsync(e => e is ServicePropertyChanged { Property: "DoesNotExist" } sp ? sp.Value : null, TimeSpan.FromMilliseconds(200));

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

            // The set is awaited until applied + published, so by now the block's actor has received the
            // SetServicePropertyValueRequest and the tap has recorded it.
            await host.Control.SetPropertyAsync("counter", "Counter", 7);

            var received = host.Control.RecordedMessages("counter");

            Assert.IsNotEmpty(received, "The tap should have recorded messages the counter block received.");
            Assert.IsTrue(received.Any(m => m.Message is SetServicePropertyValueRequest), "The set-property request the block received should have been captured by the tap.");
        }

        [TestMethod]
        public async Task RecentLogs_CaptureTheBootSequence()
        {
            await using var host = BuildHost();
            await host.StartAsync();

            var logs = host.Control.RecentLogs();

            Assert.IsNotEmpty(logs, "The boot sequence should have produced captured log lines.");
            Assert.IsTrue(logs.Any(l => l.Message.Contains("logic", StringComparison.OrdinalIgnoreCase) || l.Message.Contains("LogicBlock", StringComparison.OrdinalIgnoreCase)),
                          "Expected at least one DevHost boot log line to be captured.");
        }

        [TestMethod]
        public async Task GetDigitalAndAnalogOutput_AreNullUntilSet_ThenCarryTheLastMirroredValue()
        {
            // The read half of HAL (the symmetric complement of SetDigitalInput/SetAnalogInput): the mock
            // output handlers record what a block Sets and raise *OutputChanged; the control surface caches
            // those (from the same events it already republishes) so a scenario can ASSERT an output. The
            // SmokeHost IoBlock's [Timer(1)] OnTick mirrors IsEnabled -> ActiveOutput and CurrentLevel ->
            // EchoOutput. Before any Set the getters return null (the member never produced a value — distinct
            // from a Set false / 0); after driving the inputs and firing the timer they carry the mirrored value.
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var io = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "io");
            var active = io.ContractMappings.Single(m => m.ContractIdentifier == "ActiveOutput");
            var echo = io.ContractMappings.Single(m => m.ContractIdentifier == "EchoOutput");

            // Never Set yet -> null.
            Assert.IsNull(host.Control.GetDigitalOutput(active.MappedServiceProviderIdentifier, active.MappedServiceIdentifier, active.MappedContractIdentifier),
                          "A digital output that has never been Set must read null.");
            Assert.IsNull(host.Control.GetAnalogOutput(echo.MappedServiceProviderIdentifier, echo.MappedServiceIdentifier, echo.MappedContractIdentifier),
                          "An analog output that has never been Set must read null.");

            // Drive the inputs, then advance one virtual second so OnTick fires and mirrors them onto the outputs.
            var enable = io.ContractMappings.Single(m => m.ContractIdentifier == "EnableInput");
            var level = io.ContractMappings.Single(m => m.ContractIdentifier == "LevelInput");
            await host.Control.SetDigitalInputAsync(enable.MappedServiceProviderIdentifier, enable.MappedServiceIdentifier, enable.MappedContractIdentifier, true);
            await host.Control.SetAnalogInputAsync(level.MappedServiceProviderIdentifier, level.MappedServiceIdentifier, level.MappedContractIdentifier, 3.3);
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1));

            Assert.IsTrue(host.Control.GetDigitalOutput(active.MappedServiceProviderIdentifier, active.MappedServiceIdentifier, active.MappedContractIdentifier) ?? false,
                          "ActiveOutput must mirror IsEnabled=true after the timer fired.");
            Assert.AreEqual(3.3,
                            host.Control.GetAnalogOutput(echo.MappedServiceProviderIdentifier, echo.MappedServiceIdentifier, echo.MappedContractIdentifier)!.Value,
                            0.001,
                            "EchoOutput must mirror CurrentLevel=3.3 after the timer fired.");
        }

        private static DevConfiguration Config()
        {
            return DevConfigurationBuilder.Create().AddLogicBlock<CounterBlock>("counter").Build();
        }

        private static IDevHost BuildHost()
        {
            return DevHostBuilder.Create().WithDi<TestDependencyInjection>().WithConfiguration(Config()).Build();
        }

        // A stepped host wiring the SmokeHost IoBlock — the committed HAL fixture (digital + analog input and
        // output, with a [Timer(1)] mirroring inputs onto outputs). Stepped so AdvanceAsync fires the timer
        // deterministically.
        private static IDevHost BuildSteppedIoHost()
        {
            var config = DevConfigurationBuilder.Create().WithTopologyName("io").AddLogicBlock<SmokeHost.LogicBlocks.IoBlock>("io").Build();
            return DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
        }
    }
}