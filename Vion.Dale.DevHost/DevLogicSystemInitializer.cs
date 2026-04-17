using Vion.Dale.DevHost.Mocking;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Core;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;

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

                // Step 1: Create mock HAL handlers (for I/O simulation)
                CreateMockHalHandlers();

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

        private void CreateMockHalHandlers()
        {
            _logger.LogDebug("Creating mock HAL handlers...");
            _actorSystem.CreateRootActorFromDi<MockHalDigitalInputHandler>(nameof(DigitalInputHandler));
            _actorSystem.CreateRootActorFromDi<MockHalDigitalOutputHandler>(nameof(DigitalOutputHandler));
            _actorSystem.CreateRootActorFromDi<MockHalAnalogInputHandler>(nameof(AnalogInputHandler));
            _actorSystem.CreateRootActorFromDi<MockHalAnalogOutputHandler>(nameof(AnalogOutputHandler));
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

                    // Instantiate LogicBlock from DI using the Type
                    var logicBlock = (LogicBlockBase)_serviceProvider.GetService(logicBlockConfig.LogicBlockType)!;

                    if (logicBlock == null)
                    {
                        throw new InvalidOperationException($"Failed to instantiate {logicBlockConfig.LogicBlockType.Name} from DI. " +
                                                            $"Make sure it's registered in IConfigureServices.");
                    }

                    var name = LogicBlockUtils.CreateLogicBlockName(logicBlockConfig.Name, logicBlockConfig.Id);

                    // Create actor from the instantiated LogicBlock
                    var actorRef = _actorSystem.CreateRootActorFor(() => logicBlock, name, _logger);

                    // Initialize with configuration
                    var serviceIdLookup = logicBlockConfig.Services.ToDictionary(s => s.Identifier, s => new ServiceIdentifier(s.Id));

                    var logicBlockContractIdLookup =
                        logicBlockConfig.ContractMappings.ToDictionary(m => m.ContractIdentifier, m => new LogicBlockContractId(logicBlockConfig.Id, m.ContractIdentifier));

                    _actorSystem.SendTo(actorRef,
                                        new InitializeLogicBlock(logicBlockConfig.Id, logicBlockConfig.Name, serviceIdLookup, logicBlockContractIdLookup, _serviceProvider));

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

                // Link contracts with mock HAL handlers
                LinkContractsWithMockHandlers(configuration);

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

        private void LinkContractsWithMockHandlers(DevConfiguration configuration)
        {
            _logger.LogDebug("Linking contracts with mock HAL handlers...");

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

                // Send to all mock HAL handlers
                _actorSystem.SendTo(_actorSystem.LookupByName(nameof(DigitalInputHandler)), linkMessage);
                _actorSystem.SendTo(_actorSystem.LookupByName(nameof(DigitalOutputHandler)), linkMessage);
                _actorSystem.SendTo(_actorSystem.LookupByName(nameof(AnalogInputHandler)), linkMessage);
                _actorSystem.SendTo(_actorSystem.LookupByName(nameof(AnalogOutputHandler)), linkMessage);

                _logger.LogInformation("Linked {Count} contract mappings with mock HAL handlers", allMappings.Count);
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