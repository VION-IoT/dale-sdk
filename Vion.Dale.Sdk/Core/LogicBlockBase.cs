using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Configuration.Timers;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Persistence;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Base class for all logic blocks. Provides actor lifecycle, service binding, persistence, and timer support.
    /// </summary>
    [PublicApi]
    public abstract class LogicBlockBase : IActorReceiver, IActorDispatcher
    {
        private static readonly TimeSpan PersistentDataSaveInterval = TimeSpan.FromSeconds(60);

        // Key: ContractIdentifier, Value: ContractImplementation
        private readonly Dictionary<string, LogicBlockContractBase> _contracts = [];

        private readonly Dictionary<string, LogicSenderInterfaceBase> _interfaces = [];

        private readonly ILogger _logger;

        private readonly PersistentData _persistentData = new();

        private readonly ServiceBinder _serviceBinder = new();

        private readonly Dictionary<string, (TimeSpan interval, Action callback)> _timerCallbacks = [];

        private IActorContext _actorContext = null!;

        private IActorReference _persistenceManagerActorRef = null!; // set during initialization

        // Key: ServiceIdentifier, Value: ServiceIdentifier
        private Dictionary<ServiceIdentifier, string> _serviceIdentifierLookup = [];

        // Key: ServiceIdentifier, Value: ServiceIdentifier
        private Dictionary<string, ServiceIdentifier> _serviceIdLookup = [];

        private IActorReference _serviceMeasuringPointHandlerActorRef = null!; // set during initialization

        private IActorReference _servicePropertyHandlerActorRef = null!; // set during initialization

        private bool _started;

        protected string Id { get; private set; } = null!;

        protected string Name { get; private set; } = null!;

#pragma warning disable CS8618 // Non-nullable event 'PropertyChanged' is added by Metalama and initialized during aspect weaving
        protected LogicBlockBase(ILogger logger)
        {
            _logger = logger;
            _serviceBinder.ServicePropertyValueChanged += HandleServicePropertyValueChanged;
            _serviceBinder.ServiceMeasuringPointValueChanged += HandleServiceMeasuringPointValueChanged;
            _serviceBinder.ServicePropertyCleared += HandleServicePropertyCleared;
            _serviceBinder.ServiceMeasuringPointCleared += HandleServiceMeasuringPointCleared;
        }
#pragma warning restore CS8618

        public void InvokeSynchronized(Action action)
        {
            _actorContext.SendToSelf(new InvokeActionMessage(action));
        }

        public void InvokeSynchronizedAfter(Action action, TimeSpan delay)
        {
            _actorContext.SendToSelfAfter(new InvokeActionMessage(action), delay);
        }

        public Task HandleMessageAsync(object message, IActorContext actorContext)
        {
            _actorContext = actorContext;
            switch (message)
            {
                case LinkRuntimeActors m: // initialization
                    _servicePropertyHandlerActorRef = m.ServicePropertyHandlerActor;
                    _serviceMeasuringPointHandlerActorRef = m.ServiceMeasuringPointHandlerActor;
                    _persistenceManagerActorRef = m.PersistenceManagerActor;

                    // Link each contract to its handler actor (e.g. DigitalOutput → DigitalOutputHandler)
                    foreach (var contract in _contracts.Values)
                    {
                        contract.SetLinkedContractHandler(actorContext.LookupByName(contract.ContractHandlerActorName));
                    }

                    break;

                case InitializeLogicBlock m: // initialization
                    Id = m.LogicBlockId;
                    Name = m.LogicBlockName;
                    _logger.LogDebug("Initializing logic block '{LogicBlockName}' ({LogicBlockId}) with {ContractMappingCount} contract mappings",
                                     Name,
                                     Id,
                                     m.LogicBlockContractIdLookup.Count);
                    Configure(new LogicBlockConfigurationBuilder(AddContract,
                                                                 AddInterface,
                                                                 _serviceBinder,
                                                                 AddTimerCallback,
                                                                 () => Id,
                                                                 actorContext,
                                                                 ScheduleNextTimerTick,
                                                                 m.ServiceProvider));

                    foreach (var (identifier, logicBlockContractId) in m.LogicBlockContractIdLookup)
                    {
                        _contracts[identifier].SetLogicBlockContractId(logicBlockContractId);
                    }

                    // Warn about contracts that have no mapping — their LogicBlockContractId will remain unset
                    foreach (var contractIdentifier in _contracts.Keys)
                    {
                        if (!m.LogicBlockContractIdLookup.ContainsKey(contractIdentifier))
                        {
                            _logger.LogWarning("Contract '{ContractIdentifier}' in logic block '{LogicBlockId}' ({LogicBlockName}) has no contract mapping in configuration. " +
                                               "This contract will not be functional until a mapping is provided.",
                                               contractIdentifier,
                                               Id,
                                               Name);
                        }
                    }

                    _serviceIdLookup = m.ServiceIdLookup;
                    _serviceIdentifierLookup = m.ServiceIdLookup.ToDictionary(s => s.Value, s => s.Key);

                    _persistentData.Initialize(this, _serviceBinder, _logger);

                    SendBindLogicBlockServices();

                    Ready();
                    break;

                case SetLinkedInterfaces m: // initialization
                    foreach (var (interfaceId, linkedInterfaces) in m.LinkedInterfaceIds)
                    {
                        GetFunctionById(interfaceId).SetLinkedInterfaceIds(linkedInterfaces);
                    }

                    break;

                case RestorePersistentDataRequest m: // initialization, before starting
                    _persistentData.Apply(m.PersistentDataValues);
                    actorContext.RespondToSender(new RestorePersistentDataResponse());
                    break;

                case StartLogicBlockRequest: // after initialization
                    Starting();
                    _started = true;
                    _serviceBinder.PublishInitialStateUpdates(_logger);

                    // Schedule periodic state saves
                    ScheduleNextPeriodicStateSave(actorContext);

                    _actorContext.RespondToSender(new StartLogicBlockResponse());
                    break;

                case StopLogicBlockRequest: // stopping, before removal
                    _persistentData.CreateSnapshot();
                    Stopping();
                    _started = false;
                    _serviceBinder.ClearRetainedMessages(_logger);
                    _actorContext.RespondToSender(new StopLogicBlockResponse());
                    break;

                case PublishServiceState: // after broker reconnect
                    _serviceBinder.PublishInitialStateUpdates(_logger);
                    break;

                case GetPersistentDataSnapshotRequest: // after stopping, before removal
                    actorContext.RespondToSender(new GetPersistentDataSnapshotResponse(Id, _persistentData.GetCurrentSnapshot()));
                    break;

                case IContractMessage m: // delegate to contract
                    GetContractById(m.LogicBlockContractId).HandleContractMessage(m);
                    break;

                case IFunctionInterfaceMessage m: // delegate to logic interface
                    GetFunctionById(m.ToId).HandleMessage(m);
                    break;

                case GetServicePropertyValueRequest m: // from handler, respond with current value
                    HandleGetServicePropertyValueRequest(actorContext, m);
                    break;

                case SetServicePropertyValueRequest m: // from service proxy, set value and respond with current value
                    HandleSetServicePropertyValueRequest(actorContext, m);
                    break;

                case GetServiceMeasuringPointValueRequest m: // from handler, respond with current value
                    HandleGetServiceMeasuringPointValueRequest(actorContext, m);
                    break;

                case InvokeActionMessage m: // internal message from self
                    m.Action();
                    break;

                case TimerTickMessage m: // internal periodic message from a timer
                    HandleTimerTickMessage(actorContext, m);
                    break;

                case PeriodicPersistentDataSaveMessage: // internal periodic message
                    HandlePeriodicStateSave(actorContext);
                    break;
            }

            return Task.CompletedTask;
        }

        /// <summary>
        ///     Can be overridden to provide custom configurationBuilder logic, e.g. creating interfaces, contracts, services and
        ///     timers
        ///     programmatically with full control
        /// </summary>
        protected virtual void Configure(ILogicBlockConfigurationBuilder configurationBuilder)
        {
            DeclarativeInterfaceBinder.BindInterfacesFromAttributes(this, configurationBuilder.Interfaces);
            DeclarativeContractBinder.BindContractsFromAttributes(this, configurationBuilder.Contracts);
            DeclarativeServiceBinder.BindServicesFromAttributes(this, (ServiceBinder)configurationBuilder.Services);
            DeclarativeTimerBinder.BindTimersFromAttributes(this, configurationBuilder.Timers);
        }

        /// <summary>
        ///     Called when the logic block has been configured and is ready to run. this is the place to attach event handlers to
        ///     contract or interface elements
        /// </summary>
        protected abstract void Ready();

        /// <summary>
        ///     Called when the logic block is started (after it has been initialized/ready)
        /// </summary>
        protected virtual void Starting()
        {
        }

        /// <summary>
        ///     Called when the logic block is stopped (before it gets removed)
        /// </summary>
        protected virtual void Stopping()
        {
        }

        private void HandleGetServicePropertyValueRequest(IActorContext actorContext, GetServicePropertyValueRequest m)
        {
            if (!_serviceIdentifierLookup.TryGetValue(m.ServiceIdentifier, out var serviceIdentifier))
            {
                _logger.LogWarning("Unknown service identifier '{ServiceIdentifier}' in logic block '{Id}'.", m.ServiceIdentifier, Id);
                return;
            }

            var propertyValue = _serviceBinder.GetPropertyValue(serviceIdentifier, m.PropertyIdentifier);
            actorContext.RespondToSender(new GetServicePropertyValueResponse(m.ServiceIdentifier, m.PropertyIdentifier, propertyValue));
        }

        private void HandleSetServicePropertyValueRequest(IActorContext actorContext, SetServicePropertyValueRequest m)
        {
            if (!_serviceIdentifierLookup.TryGetValue(m.ServiceIdentifier, out var serviceIdentifier))
            {
                _logger.LogWarning("Unknown service identifier '{ServiceIdentifier}' in logic block '{Id}'.", m.ServiceIdentifier, Id);
                return;
            }

            _serviceBinder.SetPropertyValue(serviceIdentifier, m.PropertyIdentifier, m.Value);
            var propertyValue = _serviceBinder.GetPropertyValue(serviceIdentifier, m.PropertyIdentifier);
            actorContext.RespondToSender(new SetServicePropertyValueResponse(m.ServiceIdentifier, m.PropertyIdentifier, propertyValue));
        }

        private void HandleServicePropertyValueChanged(object _, ServicePropertyChangedEventArgs args)
        {
            if (!_started)
            {
                _logger.LogInformation("Logic block '{Id}' is not started, ignoring property change.", Id);
                return;
            }

            if (!_serviceIdLookup.TryGetValue(args.ServiceIdentifier, out var serviceIdentifier))
            {
                _logger.LogWarning("Unknown service identifier for identifier '{ServiceIdentifier}' in logic block '{Id}'.", args.ServiceIdentifier, Id);
                return;
            }

            _actorContext.SendTo(_servicePropertyHandlerActorRef, new ServicePropertyValueChanged(serviceIdentifier, args.PropertyIdentifier, args.Value));
        }

        private void HandleGetServiceMeasuringPointValueRequest(IActorContext actorContext, GetServiceMeasuringPointValueRequest m)
        {
            if (!_serviceIdentifierLookup.TryGetValue(m.ServiceIdentifier, out var serviceIdentifier))
            {
                _logger.LogWarning("Unknown service identifier '{ServiceIdentifier}' in logic block '{Id}'.", m.ServiceIdentifier, Id);
                return;
            }

            var propertyValue = _serviceBinder.GetMeasuringPointValue(serviceIdentifier, m.MeasuringPointIdentifier);
            actorContext.RespondToSender(new GetServiceMeasuringPointValueResponse(m.ServiceIdentifier, m.MeasuringPointIdentifier, propertyValue));
        }

        private void HandleServiceMeasuringPointValueChanged(object sender, ServiceMeasuringPointChangedEventArgs args)
        {
            if (!_started)
            {
                _logger.LogInformation("Logic block '{Id}' is not started, ignoring measuring point value change.", Id);
                return;
            }

            if (!_serviceIdLookup.TryGetValue(args.ServiceIdentifier, out var serviceIdentifier))
            {
                _logger.LogWarning("Unknown service identifier for identifier '{ServiceIdentifier}' in logic block '{Id}'.", args.ServiceIdentifier, Id);
                return;
            }

            _actorContext.SendTo(_serviceMeasuringPointHandlerActorRef, new ServiceMeasuringPointValueChanged(serviceIdentifier, args.MeasuringPointIdentifier, args.Value));
        }

        private void HandleServicePropertyCleared(object sender, ServicePropertyClearedEventArgs args)
        {
            if (!_serviceIdLookup.TryGetValue(args.ServiceIdentifier, out var serviceIdentifier))
            {
                _logger.LogWarning("Unknown service identifier for identifier '{ServiceIdentifier}' in logic block '{Id}'.", args.ServiceIdentifier, Id);
                return;
            }

            // Send empty retained message to clear
            _actorContext.SendTo(_servicePropertyHandlerActorRef, new ServicePropertyValueCleared(serviceIdentifier, args.PropertyIdentifier));
        }

        private void HandleServiceMeasuringPointCleared(object sender, ServiceMeasuringPointClearedEventArgs args)
        {
            if (!_serviceIdLookup.TryGetValue(args.ServiceIdentifier, out var serviceIdentifier))
            {
                _logger.LogWarning("Unknown service identifier for identifier '{ServiceIdentifier}' in logic block '{Id}'.", args.ServiceIdentifier, Id);
                return;
            }

            // Send empty retained message to clear
            _actorContext.SendTo(_serviceMeasuringPointHandlerActorRef, new ServiceMeasuringPointValueCleared(serviceIdentifier, args.MeasuringPointIdentifier));
        }

        // Sent once per LogicBlock at the end of InitializeLogicBlock, after Configure() has populated
        // the ServiceBinder. Per-sender ordering of Proto.Actor guarantees this arrives at the handlers
        // before any *ValueChanged from the same LogicBlock, so the handlers can rely on the lookup
        // being populated when the codec is invoked at the MQTT boundary.
        private void SendBindLogicBlockServices()
        {
            var properties = BuildBindingMap(_serviceBinder.GetAllServicePropertyBindings());
            var measuringPoints = BuildBindingMap(_serviceBinder.GetAllServiceMeasuringPointBindings());

            var message = new BindLogicBlockServices(Id, properties, measuringPoints);
            _actorContext.SendTo(_servicePropertyHandlerActorRef, message);
            _actorContext.SendTo(_serviceMeasuringPointHandlerActorRef, message);
        }

        private Dictionary<ServiceIdentifier, Dictionary<string, ServiceBindingInfo>> BuildBindingMap(
            IReadOnlyDictionary<string, IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>> bindings)
        {
            var result = new Dictionary<ServiceIdentifier, Dictionary<string, ServiceBindingInfo>>();
            foreach (var (serviceName, interfaceMap) in bindings)
            {
                if (!_serviceIdLookup.TryGetValue(serviceName, out var serviceId))
                {
                    _logger.LogWarning("No ServiceIdentifier mapping for service '{ServiceName}' in logic block '{Id}'; skipping its bindings.", serviceName, Id);
                    continue;
                }

                var perIdentifier = new Dictionary<string, ServiceBindingInfo>();
                foreach (var perInterface in interfaceMap.Values)
                {
                    foreach (var (identifier, binding) in perInterface)
                    {
                        perIdentifier[identifier] = new ServiceBindingInfo(binding.Metadata, binding.TargetPropertyType);
                    }
                }

                result[serviceId] = perIdentifier;
            }

            return result;
        }

        private void HandleTimerTickMessage(IActorContext actorContext, TimerTickMessage message)
        {
            var (interval, callback) = GetTimerCallback(message.Identifier);
            ScheduleNextTimerTick(actorContext, message.Identifier, interval);
            if (!_started)
            {
                _logger.LogInformation("Logic block '{Id}' is not started, ignoring timer tick '{TimerIdentifier}'.", Id, message.Identifier);
                return;
            }

            callback();
        }

        private void AddTimerCallback(string identifier, TimeSpan interval, Action callback)
        {
            _timerCallbacks[identifier] = (interval, callback);
        }

        private void AddInterface(string identifier, LogicSenderInterfaceBase logicBlockInterface)
        {
            _interfaces[identifier] = logicBlockInterface;
        }

        private void AddContract(string identifier, LogicBlockContractBase logicBlockContract)
        {
            _contracts[identifier] = logicBlockContract;
        }

        private LogicSenderInterfaceBase GetFunctionByIdentifier(string identifier)
        {
            return _interfaces[identifier];
        }

        private LogicSenderInterfaceBase GetFunctionById(InterfaceId functionId)
        {
            var identifier = functionId.InterfaceIdentifier;
            return GetFunctionByIdentifier(identifier);
        }

        private LogicBlockContractBase GetContractById(LogicBlockContractId logicBlockContractId)
        {
            return _contracts[logicBlockContractId.ContractIdentifier];
        }

        private (TimeSpan interval, Action callback) GetTimerCallback(string mIdentifier)
        {
            return _timerCallbacks[mIdentifier];
        }

        private static void ScheduleNextTimerTick(IActorContext actorContext, string timerIdentifier, TimeSpan delay)
        {
            actorContext.SendToSelfAfter(new TimerTickMessage(timerIdentifier), delay);
        }

        private void HandlePeriodicStateSave(IActorContext context)
        {
            if (!_started)
            {
                _logger.LogInformation("Logic block '{Id}' is not started, skipping periodic state save.", Id);
                return;
            }

            _persistentData.CreateSnapshot();
            context.SendTo(_persistenceManagerActorRef, new PersistentDataSnapshotChanged(Id, _persistentData.GetCurrentSnapshot()));
            ScheduleNextPeriodicStateSave(context);
        }

        private static void ScheduleNextPeriodicStateSave(IActorContext context)
        {
            context.SendToSelfAfter(new PeriodicPersistentDataSaveMessage(), PersistentDataSaveInterval);
        }

        private readonly record struct TimerTickMessage(string Identifier); // internal message

        /// <summary>
        ///     Represents a message that contains an action to be executed in the context of the actor.
        ///     This is not serializable, therefore only usable locally, usually within one actor
        /// </summary>
        [InternalApi]
        internal readonly record struct InvokeActionMessage(Action Action); // internal message

        private readonly record struct PeriodicPersistentDataSaveMessage; // internal message
    }
}
