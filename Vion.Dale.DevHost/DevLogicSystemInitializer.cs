using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Vion.Contracts.Events.CloudToMesh;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
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
        private readonly IActorSystem _actorSystem;

        private readonly ILogger<DevLogicSystemInitializer> _logger;

        private readonly IServiceProvider _serviceProvider;

        // The names the generic service-provider stand-ins were registered under (one per discovered
        // [ScenarioWire] handler) — the contract link map is fanned out to exactly these (RFC 0010).
        private readonly List<string> _serviceProviderHandlerNames = [];

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
                // convention scan that replaces the hardcoded four HAL mocks).
                CreateServiceProviderHandlers();

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

        private void CreateServiceProviderHandlers()
        {
            _logger.LogDebug("Discovering service-provider handlers (the same IServiceProviderHandlerActor scan the runtime uses)...");

            var events = _serviceProvider.GetRequiredService<DevHostEvents>();
            var outputCache = _serviceProvider.GetRequiredService<Control.ServiceProviderOutputCache>();
            var loggerFactory = _serviceProvider.GetRequiredService<ILoggerFactory>();

            // Mirror the runtime: scan the loaded assemblies for service-provider handler types. By this point
            // introspection has loaded every block — and so the I/O / plugin assemblies that declare the
            // handlers (DigitalInputHandler … a consumer's PowerPlantControlGridHandler). Only handlers that
            // declare a [ScenarioWire] (value contracts) yield a codec and a stand-in.
            var assemblies = AppDomain.CurrentDomain.GetAssemblies().Where(assembly => !assembly.IsDynamic).ToArray();

            foreach (var (handlerType, codec) in ServiceProviderContractHandlerScan.Discover(assemblies))
            {
                // Registered under the handler's class name — the name the consumer's contract
                // ContractHandlerActorName already looks up, so no production path changes.
                var logger = loggerFactory.CreateLogger($"{nameof(ServiceProviderContractHandler)}({handlerType.Name})");
                _actorSystem.CreateRootActorFor(() => new ServiceProviderContractHandler(logger, events, codec, outputCache), handlerType.Name, logger);
                _serviceProviderHandlerNames.Add(handlerType.Name);
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
                foreach (var handlerName in _serviceProviderHandlerNames)
                {
                    _actorSystem.SendTo(_actorSystem.LookupByName(handlerName), linkMessage);
                }

                _logger.LogInformation("Linked {Count} contract mappings to {Handlers} service-provider stand-ins", allMappings.Count, _serviceProviderHandlerNames.Count);
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