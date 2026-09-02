using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Dale.DevHost.Control;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Diagnostics;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost
{
    public class InitializationResult
    {
        public bool IsSuccess { get; private set; }

        public Exception? Exception { get; private set; }

        public string? ErrorMessage { get; private set; }

        public List<string> WarningMessages { get; } = [];

        public Dictionary<string, object> Metrics { get; } = [];

        public void AddMetric(string key, object value)
        {
            Metrics[key] = value;
        }

        public void AddWarning(string warning)
        {
            WarningMessages.Add(warning);
        }

        public void MergeWith(InitializationResult other)
        {
            WarningMessages.AddRange(other.WarningMessages);
            foreach (var (key, value) in other.Metrics)
            {
                Metrics[key] = value;
            }
        }

        public InitializationResult AsFailure(string errorMessage)
        {
            IsSuccess = false;
            ErrorMessage = errorMessage;
            return this;
        }

        public InitializationResult AsFailure(Exception exception)
        {
            IsSuccess = false;
            ErrorMessage = exception.Message;
            Exception = exception;
            return this;
        }

        public InitializationResult AsSuccess()
        {
            IsSuccess = true;
            return this;
        }
    }

    /// <summary>
    ///     Simplified logic system initializer for development.
    ///     Similar to the production LogicSystemConfigurationInitializer but without MQTT/remote components.
    /// </summary>
    public class DevLogicSystemInitializer
    {
        // The runtime's teardown timeouts (LogicSystemConfigurationInitializer.StopLogicBlockActorsAsync),
        // kept identical so a block that is slow to stop behaves the same in development and in production.
        // All three are VIRTUAL — ActorSystem routes them through the injected TimeProvider — which is why
        // StopAsync also carries the real-time backstop.
        private static readonly TimeSpan StopAcknowledgementTimeout = TimeSpan.FromSeconds(15);

        private static readonly TimeSpan SnapshotTimeout = TimeSpan.FromSeconds(5);

        private static readonly TimeSpan TerminateTimeout = TimeSpan.FromSeconds(5);

        // The wall-clock bound on the whole teardown sequence — see the note in StopAsync. Generous by
        // design: it is a last-resort backstop against a block that never acknowledges on a stepped host, not
        // a performance budget, and a slow CI box must never trip it.
        private static readonly TimeSpan StopSequenceBackstop = TimeSpan.FromSeconds(60);

        // The slice termination keeps even when the sequence budget above is already spent.
        private static readonly TimeSpan TerminateFloor = TimeSpan.FromSeconds(5);

        private static readonly Regex LogicBlockActorNameRegex = new($"^({LogicBlockUtils.LogicBlockPrefix})", RegexOptions.Compiled);

        private readonly IActorSystem _actorSystem;

        private readonly ILogger<DevLogicSystemInitializer> _logger;

        private readonly IServiceProvider _serviceProvider;

        // The names the generic service-provider stand-ins were registered under (one per discovered
        // [ScenarioWire] handler) — the contract link map is fanned out to exactly these (RFC 0010), and the
        // same registry hands the set to PublishAllStates (RFC 0020 §7). Resolved rather than injected: it is a
        // per-generation singleton and this class is public, so the internal type stays off the constructor.
        private ServiceProviderStandIns StandIns
        {
            get => _serviceProvider.GetRequiredService<ServiceProviderStandIns>();
        }

        public DevLogicSystemInitializer(IActorSystem actorSystem, IServiceProvider serviceProvider, ILogger<DevLogicSystemInitializer> logger)
        {
            _actorSystem = actorSystem;
            _serviceProvider = serviceProvider;
            _logger = logger;
        }

        public Task<InitializationResult> InitializeAsync(DevConfiguration configuration)
        {
            var result = new InitializationResult();

            try
            {
                _logger.LogInformation("Initializing development logic system with {Count} LogicBlocks...", configuration.LogicBlocks.Count);

                // Step 1: Create a generic stand-in per discovered service-provider handler (RFC 0010 — the
                // convention scan that replaces the hardcoded four HAL mocks), each carrying the topology's
                // contract-pairing table (RFC 0020). Building the table validates it, so a pairing with no
                // type-identical direction fails the host here rather than going quiet at runtime.
                CreateServiceProviderHandlers(configuration);

                // Step 2: Create mock service handlers (for service property/measuring point visibility)
                CreateMockServiceHandlers();

                CreateOtherMockHandlers();

                // Step 3: Create LogicBlock actors (same as production!)
                var createResult = CreateLogicBlockActors(configuration);
                if (!createResult.IsSuccess)
                {
                    return Task.FromResult(result.AsFailure(createResult.ErrorMessage!));
                }

                result.MergeWith(createResult);

                // Step 4: Link everything together
                var linkResult = LinkAllActors(configuration);
                if (!linkResult.IsSuccess)
                {
                    return Task.FromResult(result.AsFailure(linkResult.ErrorMessage!));
                }

                result.MergeWith(linkResult);

                _logger.LogInformation("Development logic system initialized successfully");
                return Task.FromResult(result.AsSuccess());
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize development logic system");
                return Task.FromResult(result.AsFailure(ex));
            }
        }

        public async Task StartAsync(DevConfiguration configuration)
        {
            _logger.LogInformation("Starting {Count} LogicBlocks...", configuration.LogicBlocks.Count);

            var logicBlockActors = configuration.LogicBlocks.Select(lb => _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(lb.Name, lb.Id))).ToList();

            await _actorSystem.SendAndWaitForAcknowledgementAsync<StartLogicBlockRequest, StartLogicBlockResponse>(logicBlockActors,
                                                                                                                   new StartLogicBlockRequest(),
                                                                                                                   TimeSpan.FromSeconds(5));

            _logger.LogInformation("LogicBlocks started");
        }

        /// <summary>
        ///     The mirror of <see cref="StartAsync" />: run the runtime's domain stop sequence over the logic
        ///     block actors — stop acknowledgement, persistent-data snapshot, actor termination — before the
        ///     host tears the actor system down.
        ///     <para>
        ///         Without it, <c>LogicBlockBase.Stopping()</c> is never invoked on any DevHost path: the
        ///         actors are Proto-stopped straight from <c>DevHost.DisposeAsync</c>, so a block's stop hook
        ///         — and the exact final values <c>DrainThrottlers</c> publishes with it — never run in
        ///         development while they always run in the runtime.
        ///     </para>
        ///     Never throws: every step is downgraded to a warning so teardown always reaches actor
        ///     termination, exactly like the runtime's own <c>StopLogicBlockActorsAsync</c>.
        /// </summary>
        public async Task StopAsync()
        {
            // Discover the actors from the process registry rather than from the configuration — the same
            // GetLogicBlockActors() scan the runtime's StopLogicBlockActorsAsync uses. LookupByName (what
            // StartAsync does) mints a PID whether or not the actor exists, so a configuration-driven stop
            // would send into dead letters and ride out the full timeout for a host that was never started,
            // or for a block whose creation failed and was recorded as a warning in CreateLogicBlockActors.
            var logicBlockActors = GetLogicBlockActors();
            if (logicBlockActors.Count == 0)
            {
                _logger.LogDebug("No LogicBlock actors to stop, skipping the domain stop sequence.");
                return;
            }

            _logger.LogInformation("Stopping {Count} LogicBlocks...", logicBlockActors.Count);

            // D2: one wall-clock deadline over the WHOLE sequence, deliberately NOT on the injected
            // TimeProvider. The per-wait timeouts below are virtual — ActorSystem routes them through the
            // injected clock and registers them in the virtual schedule — so on a stepped host, where nothing
            // advances the fake clock during teardown, a block that never acks would leave a due-time that
            // never arrives and hang teardown forever. Stopwatch is the system timer and is the only thing
            // here that no clock mode can stall. The budget is generous on purpose: the normal path completes
            // on the acks in milliseconds and never approaches it, so it only has to sit above the virtual
            // budget (15 s + 5 s + 5 s) by enough that a slow CI box cannot trip it.
            var sequenceStarted = Stopwatch.GetTimestamp();

            await AwaitTeardownStepAsync(_actorSystem.SendAndWaitForAcknowledgementAsync<StopLogicBlockRequest, StopLogicBlockResponse>(logicBlockActors,
                                             new StopLogicBlockRequest(),
                                             StopAcknowledgementTimeout),
                                         RemainingBackstop(sequenceStarted),
                                         "waiting for LogicBlocks to acknowledge stop");

            // D1: DevHost has no persistent data store — MockPersistentDataHandler debug-logs and drops
            // everything — so the response is deliberately discarded. The request is sent anyway because
            // message-sequence parity with the runtime is the fidelity gap being closed here, and it exercises
            // the block's CreateSnapshot() / GetCurrentSnapshot() path including its uninitialised guard.
            await AwaitTeardownStepAsync(_actorSystem.SendAndWaitForAcknowledgementAsync<GetPersistentDataSnapshotRequest, GetPersistentDataSnapshotResponse>(logicBlockActors,
                                             new GetPersistentDataSnapshotRequest(),
                                             SnapshotTimeout),
                                         RemainingBackstop(sequenceStarted),
                                         "collecting persistent data snapshots");

            // D3: the runtime pauses here for a stop grace period — a bounded, best-effort window that gives a
            // device write a block enqueued in Stopping() a chance to reach the wire before termination disposes
            // the per-block DI scopes and, with them, the clients that own the request queues. DevHost
            // deliberately has no such pause, and this is the one place the message sequence intentionally
            // diverges: a wall-clock pause would be dead weight charged to every one of the 100+
            // `await using … Build()` teardowns in Vion.Dale.DevHost.Test, and a virtual one is worse — on a
            // stepped host nothing advances the fake clock during teardown, so it would never elapse.
            //
            // What DevHost waits for instead is quiescence, not time. A block acknowledges the stop as soon as
            // Stopping() returns, while the publishes Stopping() issued (the property writes, DrainThrottlers'
            // final values) are still queued on the mock handlers' mailboxes; terminating and shutting down
            // right behind the ack drops whichever of them a contended runner has not dispatched yet — the
            // final values the UI is meant to show before a recycle, and the event a test subscribes for. The
            // barrier is the stepper's exact predicate (every mailbox drained, no handler mid-flight): it
            // returns in microseconds on the normal path because the mailboxes are already empty, waits
            // exactly as long as a queued publish needs otherwise, and is bounded by the same wall-clock
            // backstop as every other step so no clock mode can stall it.
            await AwaitTeardownStepAsync(WaitForQuiescenceAsync(RemainingBackstop(sequenceStarted)),
                                         RemainingBackstop(sequenceStarted),
                                         "draining the publishes issued during stop");

            // Termination gets whatever is left of the deadline but never less than TerminateFloor: it is the
            // step that actually releases the actors and their per-block DI scopes, so a deadline already
            // consumed by a block that would not acknowledge must not skip it entirely.
            await AwaitTeardownStepAsync(_actorSystem.StopActorsAndWaitAsync(logicBlockActors, TerminateTimeout),
                                         Max(RemainingBackstop(sequenceStarted), TerminateFloor),
                                         "waiting for LogicBlock actors to terminate");

            _logger.LogInformation("LogicBlocks stopped");
        }

        // What is left of the sequence-wide wall-clock budget. Never negative: an exhausted budget yields
        // TimeSpan.Zero, which cancels the next step immediately rather than reviving it with a fresh one.
        private static TimeSpan RemainingBackstop(long sequenceStarted)
        {
            var remaining = StopSequenceBackstop - Stopwatch.GetElapsedTime(sequenceStarted);
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }

        private static TimeSpan Max(TimeSpan a, TimeSpan b)
        {
            return a > b ? a : b;
        }

        // The stepper's barrier over the same two live signals (RuntimeVitals is the SDK's per-message mailbox
        // statistics; the in-flight monitor is DevHost's own opt-in). Resolved here rather than injected so the
        // internal barrier type stays off this public class's constructor, as StandIns does.
        private async Task WaitForQuiescenceAsync(TimeSpan budget)
        {
            var barrier = new QuiescenceBarrier(_serviceProvider.GetRequiredService<RuntimeVitals>(), _serviceProvider.GetService<IActorActivityMonitor>());
            using var budgetSource = new CancellationTokenSource(budget);
            await barrier.WaitForQuiescenceAsync(budgetSource.Token);
        }

        // The runtime's GetLogicBlockActors: every actor whose name carries the logic block prefix.
        private List<IActorReference> GetLogicBlockActors()
        {
            return _actorSystem.FindByName(LogicBlockActorNameRegex);
        }

        /// <summary>
        ///     Await one teardown step, bounded by <paramref name="backstop" /> of real time, and downgrade
        ///     every failure to a warning — teardown continues to the next step whatever happens, mirroring
        ///     the runtime, which records each <see cref="TimeoutException" /> as a warning on the result and
        ///     carries on.
        /// </summary>
        private async Task AwaitTeardownStepAsync(Task step, TimeSpan backstop, string what)
        {
            using var backstopSource = new CancellationTokenSource(backstop);
            try
            {
                await step.WaitAsync(backstopSource.Token);
            }
            catch (TimeoutException ex)
            {
                _logger.LogWarning(ex, "Timeout {What} during development host teardown; continuing.", what);
            }
            catch (OperationCanceledException) when (backstopSource.IsCancellationRequested)
            {
                // The wall-clock backstop tripped. The abandoned step keeps running on the actor system and
                // will fault with its own TimeoutException once (and if) its virtual due-time arrives —
                // observe that fault so it never resurfaces as an unobserved task exception on the finalizer.
                Observe(step);
                _logger.LogWarning("The teardown backstop elapsed after {Backstop} while {What}; continuing.", backstop, what);
            }
            catch (Exception ex)
            {
                // Includes a cancellation raised by the step itself rather than by the backstop — reported as
                // what it is, so a future debugging round is not sent after a backstop that never fired.
                _logger.LogWarning(ex, "Error {What} during development host teardown; continuing.", what);
            }
        }

        private static void Observe(Task task)
        {
            _ = task.ContinueWith(static abandoned => _ = abandoned.Exception, CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);
        }

        private void CreateServiceProviderHandlers(DevConfiguration configuration)
        {
            _logger.LogDebug("Discovering service-provider handlers (the same IServiceProviderHandlerActor scan the runtime uses)...");

            var events = _serviceProvider.GetRequiredService<DevHostEvents>();
            var outputCache = _serviceProvider.GetRequiredService<ServiceProviderOutputCache>();
            var introspection = _serviceProvider.GetRequiredService<DevHostIntrospection>();
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();

            // Mirror the runtime: scan the loaded assemblies for service-provider handler types. By this point
            // introspection has loaded every block — and so the I/O / plugin assemblies that declare the
            // handlers (DigitalInputHandler … a consumer's PowerPlantControlGridHandler). Only handlers that
            // declare a [ScenarioWire] (value contracts) yield a codec and a stand-in.
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic).ToArray();
            var discovered = ServiceProviderContractHandlerScan.Discover(assemblies);

            // RFC 0020 §4.3: the wire-type identity check lives here rather than in the topology loader because
            // the handler a contract talks to is carried by the contract INSTANCE the binder constructed —
            // introspection, which has already run, is the only place that join exists. A pairing that can carry
            // nothing throws, which InitializeAsync reports as a failed host start naming both declared types.
            var pairings = ContractPairingTable.Build(configuration.ContractPairings,
                                                      introspection.ContractHandlerActorName,
                                                      discovered.ToDictionary(d => d.HandlerType.Name, d => d.Codec, StringComparer.Ordinal));

            foreach (var (handlerType, codec) in discovered)
            {
                // Registered under the handler's class name — the name the consumer's contract
                // ContractHandlerActorName already looks up, so no production path changes.
                var logger = loggerFactory.CreateLogger($"{nameof(ServiceProviderContractHandler)}({handlerType.Name})");
                _actorSystem.CreateRootActorFor(() => new ServiceProviderContractHandler(logger,
                                                                                         events,
                                                                                         codec,
                                                                                         outputCache,
                                                                                         handlerType.Name,
                                                                                         pairings),
                                                handlerType.Name,
                                                logger);
                StandIns.Add(handlerType.Name);
                _logger.LogDebug("Created service-provider stand-in for {Handler}", handlerType.Name);
            }
        }

        private void CreateMockServiceHandlers()
        {
            _logger.LogDebug("Creating mock service handlers...");
            _actorSystem.CreateRootActorFromDi<MockServicePropertyHandler>(nameof(MockServicePropertyHandler));
            _actorSystem.CreateRootActorFromDi<MockServiceMeasuringPointHandler>(nameof(MockServiceMeasuringPointHandler));
        }

        private void CreateOtherMockHandlers()
        {
            _logger.LogDebug("Creating other mock handlers...");
            _actorSystem.CreateRootActorFromDi<MockPersistentDataHandler>(nameof(MockPersistentDataHandler));
        }

        private InitializationResult CreateLogicBlockActors(DevConfiguration configuration)
        {
            var result = new InitializationResult();
            var createdCount = 0;

            _logger.LogInformation("Creating {Count} LogicBlock actors...", configuration.LogicBlocks.Count);

            foreach (var logicBlockConfig in configuration.LogicBlocks)
            {
                try
                {
                    _logger.LogDebug("Creating actor for {Name} ({Id}) of type {Type}", logicBlockConfig.Name, logicBlockConfig.Id, logicBlockConfig.LogicBlockType.Name);

                    var name = LogicBlockUtils.CreateLogicBlockName(logicBlockConfig.Name, logicBlockConfig.Id);

                    // Spawn the block the same way production does (LogicSystemConfigurationInitializer):
                    // CreateRootActorFromDi resolves it in a per-block DI scope disposed on the actor's stop, so
                    // a per-block Modbus/HTTP client is reclaimed on host recycle instead of leaking to the root
                    // container (RFC 0018 / DF-46). A missing / unconstructable type throws (e.g. a dependency
                    // not registered in IConfigureServices) and is caught and recorded below.
                    var actorRef = _actorSystem.CreateRootActorFromDi(logicBlockConfig.LogicBlockType, name);

                    // Initialize with configuration
                    var serviceIdLookup = logicBlockConfig.Services.ToDictionary(s => s.Identifier, s => new ServiceIdentifier(s.Id));

                    var logicBlockContractIdLookup =
                        logicBlockConfig.ContractMappings.ToDictionary(m => m.ContractIdentifier, m => new LogicBlockContractId(logicBlockConfig.Id, m.ContractIdentifier));

                    // RFC 0016: carry the topology's operator-chosen parameter values so the block applies them
                    // before Configure and the Live-mode binders resolve inclusion gates.
                    var instantiationParameterValues = logicBlockConfig.InstantiationParameters
                                                                       ?.Select(kvp => new SetLogicConfigurationPayload.InstantiationParameterValue
                                                                                       { Identifier = kvp.Key, Value = kvp.Value })
                                                                       .ToList();

                    _actorSystem.SendTo(actorRef,
                                        new InitializeLogicBlock(logicBlockConfig.Id,
                                                                 logicBlockConfig.Name,
                                                                 serviceIdLookup,
                                                                 logicBlockContractIdLookup,
                                                                 _serviceProvider,
                                                                 instantiationParameterValues));

                    createdCount++;
                }
                catch (Exception ex)
                {
                    var error = $"Failed to create actor for {logicBlockConfig.Name}: {ex.Message}";
                    _logger.LogError(ex, error);
                    result.AddWarning(error);
                }
            }

            result.AddMetric("CreatedActors", createdCount);
            _logger.LogInformation("Created {Count} LogicBlock actors", createdCount);

            return result.AsSuccess();
        }

        private InitializationResult LinkAllActors(DevConfiguration configuration)
        {
            var result = new InitializationResult();

            try
            {
                // Link logic blocks with runtime actors
                LinkLogicBlocksWithMockHandlers(configuration);

                // Link contracts with the generic service-provider stand-ins
                LinkContractsWithServiceProviderHandlers(configuration);

                // Link interfaces between LogicBlocks
                LinkInterfaces(configuration);

                // Link services with mock handlers
                LinkMockHandlersWithServices(configuration);

                return result.AsSuccess();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to link actors");
                return result.AsFailure(ex);
            }
        }

        private void LinkLogicBlocksWithMockHandlers(DevConfiguration configuration)
        {
            _logger.LogDebug("Linking LogicBlocks with runtime actors...");
            foreach (var logicBlockConfig in configuration.LogicBlocks)
            {
                var actorRef = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(logicBlockConfig.Name, logicBlockConfig.Id));

                _actorSystem.SendTo(actorRef,
                                    new LinkRuntimeActors
                                    {
                                        ServicePropertyHandlerActor = _actorSystem.LookupByName(nameof(MockServicePropertyHandler)),
                                        ServiceMeasuringPointHandlerActor = _actorSystem.LookupByName(nameof(MockServiceMeasuringPointHandler)),
                                        PersistenceManagerActor = _actorSystem.LookupByName(nameof(MockPersistentDataHandler)),
                                    });
            }
        }

        private void LinkContractsWithServiceProviderHandlers(DevConfiguration configuration)
        {
            _logger.LogDebug("Linking contracts with the generic service-provider stand-ins...");

            var allMappings = new List<(LogicBlockContractId LogicBlockContractId, IActorReference ActorRef, ServiceProviderContractId ServiceProviderContractId)>();

            foreach (var logicBlockConfig in configuration.LogicBlocks)
            {
                if (logicBlockConfig.ContractMappings.Count == 0)
                {
                    continue;
                }

                var actorRef = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(logicBlockConfig.Name, logicBlockConfig.Id));

                foreach (var mapping in logicBlockConfig.ContractMappings)
                {
                    allMappings.Add((new LogicBlockContractId(logicBlockConfig.Id, mapping.ContractIdentifier), actorRef,
                                        new ServiceProviderContractId(mapping.ServiceProviderIdentifier, mapping.ServiceIdentifier, mapping.ContractEndpointIdentifier)));
                }
            }

            if (allMappings.Count > 0)
            {
                var map = allMappings.GroupBy(m => m.ServiceProviderContractId).ToDictionary(g => g.Key, g => g.ToDictionary(m => m.LogicBlockContractId, m => m.ActorRef));

                var linkMessage = new LinkLogicBlockContractActors(map);

                // Fan the full link map to every generic stand-in (each forwards only for the contracts it
                // serves). Custom contracts come for free — the map is built from all contract mappings.
                foreach (var handlerName in StandIns.Names)
                {
                    _actorSystem.SendTo(_actorSystem.LookupByName(handlerName), linkMessage);
                }

                _logger.LogInformation("Linked {Count} contract mappings to {Handlers} service-provider stand-ins", allMappings.Count, StandIns.Names.Count);
            }
        }

        private void LinkInterfaces(DevConfiguration configuration)
        {
            _logger.LogDebug("Linking interfaces between LogicBlocks...");

            var interfaceMappings = new Dictionary<IActorReference, Dictionary<InterfaceId, Dictionary<InterfaceId, IActorReference>>>();

            foreach (var mapping in configuration.InterfaceMappings)
            {
                var sourceActorRef = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(mapping.SourceLogicBlockName, mapping.SourceLogicBlockId));

                var targetActorRef = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(mapping.TargetLogicBlockName, mapping.TargetLogicBlockId));

                var sourceInterfaceId = new InterfaceId(mapping.SourceLogicBlockId, mapping.SourceInterfaceIdentifier);
                var targetInterfaceId = new InterfaceId(mapping.TargetLogicBlockId, mapping.TargetInterfaceIdentifier);

                // Add to source actor's outgoing links
                if (!interfaceMappings.ContainsKey(sourceActorRef))
                {
                    interfaceMappings[sourceActorRef] = new Dictionary<InterfaceId, Dictionary<InterfaceId, IActorReference>>();
                }

                if (!interfaceMappings[sourceActorRef].ContainsKey(sourceInterfaceId))
                {
                    interfaceMappings[sourceActorRef][sourceInterfaceId] = new Dictionary<InterfaceId, IActorReference>();
                }

                interfaceMappings[sourceActorRef][sourceInterfaceId][targetInterfaceId] = targetActorRef;

                // Add to target actor's incoming links
                if (!interfaceMappings.ContainsKey(targetActorRef))
                {
                    interfaceMappings[targetActorRef] = new Dictionary<InterfaceId, Dictionary<InterfaceId, IActorReference>>();
                }

                if (!interfaceMappings[targetActorRef].ContainsKey(targetInterfaceId))
                {
                    interfaceMappings[targetActorRef][targetInterfaceId] = new Dictionary<InterfaceId, IActorReference>();
                }

                interfaceMappings[targetActorRef][targetInterfaceId][sourceInterfaceId] = sourceActorRef;
            }

            // Send SetLinkedInterfaces to each LogicBlock
            foreach (var (actorRef, links) in interfaceMappings)
            {
                _actorSystem.SendTo(actorRef, new SetLinkedInterfaces(links));
            }
        }

        private void LinkMockHandlersWithServices(DevConfiguration configuration)
        {
            _logger.LogDebug("Linking services with mock handlers...");

            var servicePropertyHandler = _actorSystem.LookupByName(nameof(MockServicePropertyHandler));
            var serviceMeasuringPointHandler = _actorSystem.LookupByName(nameof(MockServiceMeasuringPointHandler));

            var serviceActorRefs = new Dictionary<ServiceIdentifier, IActorReference>();

            foreach (var logicBlockConfig in configuration.LogicBlocks)
            {
                var actorRef = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(logicBlockConfig.Name, logicBlockConfig.Id));

                // Collect service references
                foreach (var service in logicBlockConfig.Services)
                {
                    serviceActorRefs[new ServiceIdentifier(service.Id)] = actorRef;
                }
            }

            // Link handlers back to LogicBlocks
            _actorSystem.SendTo(servicePropertyHandler, new LinkLogicBlockServiceActors(serviceActorRefs));
            _actorSystem.SendTo(serviceMeasuringPointHandler, new LinkLogicBlockServiceActors(serviceActorRefs));
        }
    }
}