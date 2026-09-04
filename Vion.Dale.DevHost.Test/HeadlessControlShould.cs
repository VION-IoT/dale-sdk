using System;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Control;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Messages;

namespace Vion.Dale.DevHost.Test
{
    /// <summary>
    ///     End-to-end smoke tests for the headless in-process control surface: boot a real wired
    ///     network with no web UI, drive it, and observe — the multi-block analogue of the TestKit loop.
    /// </summary>
    [TestClass]
    public class HeadlessControlShould
    {
        [TestMethod]
        [TestProperty("spec", "AC-CTRL-008.1")]
        public async Task ListWiredBlocksWithIdentityAndServices()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // Act / Assert
            var logicBlocks = host.Control.ListLogicBlocks();

            Assert.HasCount(1, logicBlocks);
            Assert.AreEqual("counter", logicBlocks[0].Name);
            Assert.AreEqual(nameof(CounterBlock), logicBlocks[0].TypeName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-008.7")]
        [TestProperty("spec", "AC-CTRL-010.1")]
        public async Task PublishWrittenValueOnObservationStream()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // Register the observer BEFORE triggering the change — WaitForAsync observes only future events.
            // Match the specific target value; the startup state publish also emits Counter (=0).
            var observe = host.Control.WaitForAsync(e => e is ServicePropertyChanged { Property: "Counter" } sp && Convert.ToInt32(sp.Value) == 42 ? sp.Value : null,
                                                    TimeSpan.FromSeconds(15));

            await host.Control.SetPropertyAsync("counter", "Counter", 42);

            var observed = await observe;
            // Act / Assert
            Assert.IsNotNull(observed, "The Counter=42 change should have been observed.");
            Assert.AreEqual(42, Convert.ToInt32(observed));
            Assert.AreEqual(42, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.2")]
        public async Task CompleteWriteOnBlockAcknowledgement()
        {
            // Regression (in-process set silent no-op): SetPropertyAsync must complete only after the value is
            // applied AND published, so an immediate GetProperty returns the new value instead of racing the
            // actor. Before the fix the set was fire-and-forget, so `await Set; Get` returned the stale value
            // for every type (int/enum/double/TimeSpan) — which read as a silent no-op.
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // Deliberately written IMMEDIATELY after StartAsync, racing the block's initial startup
            // publishes: the ack is correlated with the write's own round trip (the block's response),
            // so a stale in-flight publish can never satisfy it — the regression a change-event-based
            // ack had (CI caught it: ack in 18 ms, read 0).
            // Act / Assert
            await host.Control.SetPropertyAsync("counter", "Counter", 99);
            Assert.AreEqual(99, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")), "int read-after-write must be immediate after the await.");

            await host.Control.SetPropertyAsync("counter", "ControlInterval", TimeSpan.FromSeconds(60));
            Assert.AreEqual(TimeSpan.FromSeconds(60),
                            (TimeSpan)host.Control.GetProperty("counter", "ControlInterval")!,
                            "TimeSpan read-after-write must be immediate after the await.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.2")]
        [TestProperty("spec", "AC-CTRL-010.6")]
        public async Task AcknowledgeWriteThatChangedNothing()
        {
            // A write that doesn't change the value raises no change event ([Observable] dedup); the ack
            // must come from the write's own round-trip response instead of riding out the 5 s timeout.
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            await host.Control.SetPropertyAsync("counter", "Counter", 7);

            var stopwatch = System.Diagnostics.Stopwatch.StartNew();
            await host.Control.SetPropertyAsync("counter", "Counter", 7);
            stopwatch.Stop();

            // Act / Assert
            Assert.IsLessThan(2000, stopwatch.Elapsed.TotalMilliseconds, "a no-op write must ack on its response, not the timeout.");
            Assert.AreEqual(7, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")));
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-008.6")]
        public async Task ReportEveryKnownMemberOfBlock()
        {
            // Measuring points (read-only computed metrics) are first-class on the control surface: setting
            // Counter computes CounterDoubled, and the headless surface must expose it for asserting calculations.
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // CounterDoubled is a *downstream* change: SetPropertyAsync awaits the Counter property apply+publish,
            // but the measuring point is recomputed + published just after that, so an immediate GetProperty can
            // race it (and read 0). Register the observer before the set — WaitForAsync only sees future events —
            // and wait for the measuring-point publish, the same pattern as PublishWrittenValueOnObservationStream.
            var doubled = host.Control.WaitForAsync(e => e is ServiceMeasuringPointChanged { MeasuringPoint: "CounterDoubled" } mp && Convert.ToInt32(mp.Value) == 42 ? mp.Value :
                                                             null,
                                                    TimeSpan.FromSeconds(15));

            // Act / Assert
            await host.Control.SetPropertyAsync("counter", "Counter", 21);

            Assert.IsNotNull(await doubled, "CounterDoubled = Counter * 2 should have been published after setting Counter.");

            // GetProperty reads computed measuring points too; the value cache is updated before the publish above.
            Assert.AreEqual(42, Convert.ToInt32(host.Control.GetProperty("counter", "CounterDoubled")), "CounterDoubled = Counter * 2 must be readable after setting Counter.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-019.1")]
        public async Task ExportWiredNetworkWithItsBlocksAndProviders()
        {
            // The heavyweight introspection (what the web UI renders) is reachable in-process through the one
            // control abstraction — agents can read property/measuring-point schemas without standing up the
            // web stack. This is the capability the collapsed IDevHostStateProvider used to gate behind the web.
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // Act / Assert
            var config = host.Control.GetConfiguration();

            Assert.IsNotNull(config);
            var block = config.LogicBlocks.Single(b => b.Name == "counter");
            var service = block.Services.Single();

            var counter = service.ServiceProperties.Single(p => p.Identifier == "Counter");
            Assert.IsNotNull(counter.Schema, "Each service property must carry its JSON schema.");
            Assert.IsTrue(service.ServiceMeasuringPoints.Any(m => m.Identifier == "CounterDoubled"), "The computed measuring point must be described in the configuration.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-001.6")]
        [TestProperty("spec", "AC-CTRL-008.2")]
        public async Task IntrospectOnDemandBeforeHostStarts()
        {
            // Regression: the web server starts serving /api/configuration as part of host startup, and a
            // request can race in before DevHost.StartAsync has run introspection. BuildConfiguration must
            // self-initialize rather than throw KeyNotFoundException for the first block. Calling
            // GetConfiguration on a built-but-not-started host exercises exactly that defensive path.
            // Arrange
            await using var host = BuildHost();

            // Act / Assert
            var config = host.Control.GetConfiguration();

            Assert.IsNotNull(config);
            Assert.IsTrue(config.LogicBlocks.Any(b => b.Name == "counter"), "Configuration must describe the wired blocks even when reached before StartAsync.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.3")]
        public async Task DecodeJsonWriteValueAgainstMemberSchema()
        {
            // The HTTP set path addresses a property by its service id and arrives as JSON. The unified control
            // must decode that JSON against the property schema into the precise CLR type — exercise that branch
            // directly with a JsonNode so the conversion is covered without the web stack in the loop.
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var serviceId = host.Control
                                .GetConfiguration()
                                .LogicBlocks
                                .Single(b => b.Name == "counter")
                                .Services
                                .Single(s => s.ServiceProperties.Any(p => p.Identifier == "Counter"))
                                .Id;

            // Act / Assert
            await host.Control.SetServicePropertyValueAsync(serviceId, "Counter", JsonValue.Create(99));

            Assert.AreEqual(99, Convert.ToInt32(host.Control.GetProperty("counter", "Counter")), "Setting by service id with a JSON value should decode + apply.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.1")]
        [TestProperty("spec", "AC-CTRL-016.2")]
        public async Task RefuseWriteToUnknownOrReadOnlyMember()
        {
            // Trip wire: writing a member the block can't apply — a read-only measuring point / [ServiceProperty]
            // with no public setter, or an unknown member name — used to look successful (the actor swallowed the
            // binder exception, the write ack timed out, and the HTTP path returned 200). The control surface must
            // reject such a write UP FRONT, loudly, on both the HTTP and scenario paths.
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var serviceId = host.Control
                                .GetConfiguration()
                                .LogicBlocks
                                .Single(b => b.Name == "counter")
                                .Services
                                .Single(s => s.ServiceMeasuringPoints.Any(m => m.Identifier == "CounterDoubled"))
                                .Id;

            // CounterDoubled is a [ServiceMeasuringPoint] — read-only. The typed exception carries a
            // machine-readable reason + the offending property (subclass of InvalidOperationException).
            var readOnly =
                await Assert.ThrowsExactlyAsync<ServicePropertyWriteException>(() => host.Control.SetServicePropertyValueAsync(serviceId, "CounterDoubled", JsonValue.Create(7)));
            // Act / Assert
            Assert.AreEqual(ServicePropertyWriteException.ReasonReadOnly, readOnly.Reason);
            Assert.AreEqual("CounterDoubled", readOnly.Property);

            // An unknown member name on a known service must also fail loudly, not silently no-op.
            var unknown =
                await Assert.ThrowsExactlyAsync<ServicePropertyWriteException>(() => host.Control.SetServicePropertyValueAsync(serviceId, "NoSuchMember", JsonValue.Create(7)));
            Assert.AreEqual(ServicePropertyWriteException.ReasonUnknownMember, unknown.Reason);
            Assert.AreEqual("NoSuchMember", unknown.Property);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.3")]
        public async Task AcceptDurationWriteInEitherSpelling()
        {
            // TimeSpan maps to PrimitiveKind.Duration. The rich-types codec parses ISO-8601 ("PT5S") only,
            // but the web UI (and .NET habit) submit the .NET ToString form ("00:00:05"). The write path must
            // accept both, or every TimeSpan property is unwritable from the UI (FormatException → HTTP 500).
            // Arrange
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
            // Act / Assert
            await host.Control.SetServicePropertyValueAsync(serviceId, "ControlInterval", JsonValue.Create("00:00:05"));
            Assert.AreEqual(TimeSpan.FromSeconds(5), (TimeSpan)host.Control.GetProperty("counter", "ControlInterval")!, "The .NET TimeSpan form (00:00:05) must be accepted.");

            // ISO-8601 duration — the codec/MQTT canonical form.
            await host.Control.SetServicePropertyValueAsync(serviceId, "ControlInterval", JsonValue.Create("PT10S"));
            Assert.AreEqual(TimeSpan.FromSeconds(10), (TimeSpan)host.Control.GetProperty("counter", "ControlInterval")!, "The ISO-8601 duration form (PT10S) must be accepted.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-010.3")]
        public async Task ResolveWaitWithNoValueOnTimeout()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // Act / Assert
            var observed = await host.Control.WaitForAsync(e => e is ServicePropertyChanged { Property: "DoesNotExist" } sp ? sp.Value : null, TimeSpan.FromMilliseconds(200));

            Assert.IsNull(observed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-008.9")]
        public async Task ReportMessagesTapCapturedForOneBlock()
        {
            // The message tap (opt-in ProtoActor observer) records messages each actor receives. Driving a
            // property set sends a SetServicePropertyValueRequest to the block's actor; the tap must capture
            // it under that block. This is the mechanism behind "assert device-x received a DataRequest".
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // The set is awaited until applied + published, so by now the block's actor has received the
            // SetServicePropertyValueRequest and the tap has recorded it.
            await host.Control.SetPropertyAsync("counter", "Counter", 7);

            // Act / Assert
            var received = host.Control.RecordedMessages("counter");

            Assert.IsNotEmpty(received, "The tap should have recorded messages the counter block received.");
            Assert.IsTrue(received.Any(m => m.Message is SetServicePropertyValueRequest), "The set-property request the block received should have been captured by the tap.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-008.10")]
        [TestProperty("spec", "AC-CTRL-008.11")]
        public async Task ReportRecentLogLinesOldestFirst()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // Act / Assert
            var logs = host.Control.RecentLogs();

            Assert.IsNotEmpty(logs, "The boot sequence should have produced captured log lines.");
            Assert.IsTrue(logs.Any(l => l.Message.Contains("logic", StringComparison.OrdinalIgnoreCase) || l.Message.Contains("LogicBlock", StringComparison.OrdinalIgnoreCase)),
                          "Expected at least one DevHost boot log line to be captured.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-008.12")]
        public async Task ReportServiceProviderOutputCapturedThisGeneration()
        {
            // The generic read half (the complement of DriveServiceProviderContractAsync): the stand-in records
            // what a block Sets and the control surface serves it via ReadServiceProviderOutput, so a scenario
            // can ASSERT an output. The SmokeHost IoBlock's [Timer(1)] OnTick mirrors IsEnabled -> ActiveOutput
            // and CurrentLevel -> EchoOutput. Before any Set the read reports NeverWritten — which is a
            // different fact from a Set false / 0, and the distinction the read exists to make; after driving
            // the inputs and firing the timer it is Readable and carries the mirrored value.
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var io = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "io");
            var active = io.ContractMappings.Single(m => m.ContractIdentifier == "ActiveOutput");
            var echo = io.ContractMappings.Single(m => m.ContractIdentifier == "EchoOutput");

            // Resolve each input contract's stand-in actor name the same way the web UI does — from the
            // contract's ContractHandlerActorName annotation — so the drive carries no hardcoded HAL handler name.
            string HandlerFor(string contractId)
            {
            // Act / Assert
                return io.Contracts.Single(c => c.Identifier == contractId).Annotations[ServiceProviderContractAnnotations.ContractHandlerActorName].ToString()!;
            }

            // Never Set yet -> NeverWritten, and no captured command to show.
            var activeBefore = host.Control.ReadServiceProviderOutput(active.MappedServiceProviderIdentifier, active.MappedServiceIdentifier, active.MappedContractIdentifier);
            Assert.AreEqual(ServiceProviderOutputState.NeverWritten, activeBefore.State, "A digital output that has never been Set must read as never written.");
            Assert.IsNull(activeBefore.Captured, "Nothing was written, so there is no command to carry.");
            Assert.AreEqual(ServiceProviderOutputState.NeverWritten,
                            host.Control.ReadServiceProviderOutput(echo.MappedServiceProviderIdentifier, echo.MappedServiceIdentifier, echo.MappedContractIdentifier).State,
                            "An analog output that has never been Set must read as never written.");

            // Drive the inputs, then advance one virtual second so OnTick fires and mirrors them onto the outputs.
            var enable = io.ContractMappings.Single(m => m.ContractIdentifier == "EnableInput");
            var level = io.ContractMappings.Single(m => m.ContractIdentifier == "LevelInput");
            await host.Control.DriveServiceProviderContractAsync(HandlerFor("EnableInput"),
                                                                 enable.MappedServiceProviderIdentifier,
                                                                 enable.MappedServiceIdentifier,
                                                                 enable.MappedContractIdentifier,
                                                                 JsonSerializer.SerializeToElement(true));
            await host.Control.DriveServiceProviderContractAsync(HandlerFor("LevelInput"),
                                                                 level.MappedServiceProviderIdentifier,
                                                                 level.MappedServiceIdentifier,
                                                                 level.MappedContractIdentifier,
                                                                 JsonSerializer.SerializeToElement(3.3));
            await host.Control.AdvanceAsync(TimeSpan.FromSeconds(1));

            var activeAfter = host.Control.ReadServiceProviderOutput(active.MappedServiceProviderIdentifier, active.MappedServiceIdentifier, active.MappedContractIdentifier);
            Assert.AreEqual(ServiceProviderOutputState.Readable, activeAfter.State, "ActiveOutput was written, so it must read as readable.");
            Assert.IsTrue((bool)activeAfter.Value!, "ActiveOutput must mirror IsEnabled=true after the timer fired.");

            var echoAfter = host.Control.ReadServiceProviderOutput(echo.MappedServiceProviderIdentifier, echo.MappedServiceIdentifier, echo.MappedContractIdentifier);
            Assert.AreEqual(ServiceProviderOutputState.Readable, echoAfter.State, "EchoOutput was written, so it must read as readable.");
            Assert.AreEqual(3.3, (double)echoAfter.Value!, 0.001, "EchoOutput must mirror CurrentLevel=3.3 after the timer fired.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.6")]
        public async Task ReplayEveryStandInsStateOnDemand()
        {
            // A browser that connects after a value was written is primed by PublishAllStates (the SignalR
            // hub's OnConnectedAsync). It used to ask the four HAL handlers by name, so a consumer's own value
            // contract — or a provider face — stayed dark on that client until the next write. Now every
            // discovered stand-in is asked, which is what "no hardcoded contract support" means.
            // GridBlock.Demand is the non-HAL case: a third-party-shaped struct contract on GridDemandHandler.
            // Arrange
            var config = DevConfigurationBuilder.Create().WithTopologyName("grid").AddLogicBlock<SmokeHost.LogicBlocks.GridBlock>("grid").Build();
            await using var host = DevHostBuilder.Create().WithDi<SmokeHost.DependencyInjection>().WithConfiguration(config).WithDeterministicStepping().Build();
            await host.StartAsync();

            var grid = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "grid");
            var demand = grid.ContractMappings.Single(m => m.ContractIdentifier == "Demand");
            var handler = grid.Contracts.Single(c => c.Identifier == "Demand").Annotations[ServiceProviderContractAnnotations.ContractHandlerActorName].ToString()!;
            // Act / Assert
            Assert.AreNotEqual("DigitalInputHandler", handler, "The fixture must be a NON-HAL contract for this test to mean anything.");

            // Drive, and await the stand-in's OWN event for it — so the value is recorded before the replay is
            // asked for and the assertion below cannot pass on the drive's echo.
            Func<DevHostEvent, object?> onDemand = e => e is ServiceProviderContractChanged c && c.ContractId == demand.MappedContractIdentifier ? c.Value.GetRawText() : null;

            var driven = host.Control.WaitForAsync(onDemand, TimeSpan.FromSeconds(15));
            await host.Control.DriveServiceProviderContractAsync(handler,
                                                                 demand.MappedServiceProviderIdentifier,
                                                                 demand.MappedServiceIdentifier,
                                                                 demand.MappedContractIdentifier,
                                                                 JsonSerializer.SerializeToElement(new
                                                                                                   {
                                                                                                       valid = true, scope = "PerPhase",
                                                                                                       limits = new { activePowerW = 1200.0, reactivePowerVar = 0.0 },
                                                                                                   }));
            Assert.IsNotNull(await driven, "The drive itself must reach the stand-in before the replay is asked for.");

            // Subscribe LATE — exactly like a browser connecting mid-session — then ask for the replay and
            // watch this contract's state arrive a SECOND time.
            var replayed = host.Control.WaitForAsync(onDemand, TimeSpan.FromSeconds(15));
            host.Control.PublishAllStates();

            var observed = await replayed;
            Assert.IsNotNull(observed, "A late subscriber must be primed with a non-HAL contract's state, not just the four HAL ones.");
            StringAssert.Contains((string)observed, "1200", "The replayed value must be the one that was driven.");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.4")]
        [TestProperty("spec", "AC-CTRL-016.3")]
        public async Task RefuseDriveNamingHandlerHostDidNotCreate()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var io = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "io");
            var enable = io.ContractMappings.Single(m => m.ContractIdentifier == "EnableInput");

            var refusal = await Assert.ThrowsExactlyAsync<ServiceProviderDriveException>(() =>
                                                                                            host.Control
                                                                                                .DriveServiceProviderContractAsync("NoSuchHandler",
                                                                                                    enable.MappedServiceProviderIdentifier,
                                                                                                    enable.MappedServiceIdentifier,
                                                                                                    enable.MappedContractIdentifier,
                                                                                                    JsonSerializer.SerializeToElement(true)));

            // Act / Assert
            Assert.AreEqual(ServiceProviderDriveException.ReasonUnknownHandler, refusal.Reason);
            StringAssert.Contains(refusal.Message, "NoSuchHandler");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-009.4")]
        [TestProperty("spec", "AC-CTRL-016.3")]
        public async Task RefuseDriveNamingContractHostDoesNotCarry()
        {
            // Arrange
            await using var host = BuildSteppedIoHost();
            await host.StartAsync();

            var io = host.Control.GetConfiguration().LogicBlocks.Single(b => b.Name == "io");
            var enable = io.ContractMappings.Single(m => m.ContractIdentifier == "EnableInput");
            var handler = io.Contracts.Single(c => c.Identifier == "EnableInput").Annotations[ServiceProviderContractAnnotations.ContractHandlerActorName].ToString()!;

            var refusal = await Assert.ThrowsExactlyAsync<ServiceProviderDriveException>(() =>
                                                                                            host.Control
                                                                                                .DriveServiceProviderContractAsync(handler,
                                                                                                    enable.MappedServiceProviderIdentifier,
                                                                                                    enable.MappedServiceIdentifier,
                                                                                                    "NoSuchContract",
                                                                                                    JsonSerializer.SerializeToElement(true)));

            // Act / Assert
            Assert.AreEqual(ServiceProviderDriveException.ReasonUnknownContract, refusal.Reason);
            StringAssert.Contains(refusal.Message, "NoSuchContract");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-010.4")]
        public async Task RefuseWaitWhoseTimeoutRunsBackwards()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            var refusal = await Assert.ThrowsExactlyAsync<ArgumentOutOfRangeException>(() => host.Control.WaitForAsync(_ => "x", TimeSpan.FromSeconds(-1)));

            // Act / Assert
            Assert.AreEqual("timeout", refusal.ParamName);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-010.4")]
        public async Task ObserveNothingForWaitWhoseTimeoutHasNoSpan()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // Act / Assert
            var observed = await host.Control.WaitForAsync(_ => "x", TimeSpan.Zero);

            Assert.IsNull(observed);
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-010.2")]
        public async Task DetachSubscriberOnDisposalAndSurviveOneThatThrows()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();
            var faultyCalls = 0;
            var goodCalls = 0;
            var detachedCalls = 0;
            using var faulty = host.Control.Subscribe(_ =>
                                                      {
                                                          faultyCalls++;
                                                          throw new InvalidOperationException("a subscriber that throws");
                                                      });
            using var good = host.Control.Subscribe(_ => goodCalls++);
            var detached = host.Control.Subscribe(_ => detachedCalls++);
            detached.Dispose();
            detached.Dispose();

            // Act
            await host.Control.SetPropertyAsync("counter", "Counter", 11);

            // Assert — the fan-out reached the good sink past the throwing one, and never the detached one.
            Assert.IsGreaterThan(0, faultyCalls);
            Assert.IsGreaterThan(0, goodCalls, "one faulty subscriber must not break the fan-out to the others");
            Assert.AreEqual(0, detachedCalls, "a disposed token detaches, and disposing it twice is harmless");
        }

        [TestMethod]
        [TestProperty("spec", "AC-CTRL-010.5")]
        public async Task RefuseNullSinkOrSelector()
        {
            // Arrange
            await using var host = BuildHost();
            await host.StartAsync();

            // Act / Assert — the alternative is a null reference raised from an actor thread at the first event.
            Assert.ThrowsExactly<ArgumentNullException>(() => host.Control.Subscribe(null!));
            Assert.ThrowsExactly<ArgumentNullException>(() => host.Control.SubscribeLogs(null!));
            await Assert.ThrowsExactlyAsync<ArgumentNullException>(() => host.Control.WaitForAsync<string>(null!, TimeSpan.FromSeconds(1)));
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