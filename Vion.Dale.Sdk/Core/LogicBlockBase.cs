using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.Configuration;
using Vion.Dale.Sdk.Configuration.Contract;
using Vion.Dale.Sdk.Configuration.Interfaces;
using Vion.Dale.Sdk.Configuration.Services;
using Vion.Dale.Sdk.Configuration.Timers;
using Vion.Dale.Sdk.Diagnostics;
using Vion.Dale.Sdk.Emission;
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

        // Measuring-point emission stream — see _servicePropertyThrottlers for why a property and a
        // measuring point are gated by SEPARATE collections (a member can be both; they must not share).
        private readonly Dictionary<(string ServiceIdentifier, string MemberIdentifier), Throttler> _measuringPointThrottlers = [];

        private readonly PersistentData _persistentData = new();

        private readonly ServiceBinder _serviceBinder = new();

        // RFC 0004 emission gates — one Throttler per bound member, keyed by (ServiceIdentifier,
        // MemberIdentifier). A property and a measuring point are SEPARATE streams (own MQTT topic
        // .../property/state vs .../measuring-point/state, own throttle/deadband), so they get
        // separate collections rather than one map keyed by member name. This matters because a
        // member can be BOTH a [ServiceProperty] and a [ServiceMeasuringPoint] on the same C#
        // property (the dual-annotated grid-meter telemetry shape): with a shared throttler the
        // property's leading-edge emit seeded LastEmitted, so the identical measuring-point offer hit
        // the value-equality floor and was dropped — silencing the measuring-point stream entirely.
        // Built after Configure() (BuildThrottlers); empty until then; never consulted when
        // _emissionPolicyActive is false. A value-clear rebuilds a member's gate from its
        // Throttler.Policy (the gate has no in-place cancel) — see ResetThrottlerPending.
        private readonly Dictionary<(string ServiceIdentifier, string MemberIdentifier), Throttler> _servicePropertyThrottlers = [];

        private readonly Dictionary<string, (TimeSpan interval, Action callback)> _timerCallbacks = [];

        // RFC 0005 watchdog: raw per-[Timer] callback duration + scheduler jitter, reported to the vitals
        // core when one is registered. Absent in bare hosts and the TestKit, where measurement is skipped.
        private readonly Dictionary<string, long> _timerLastTickTimestamp = [];

        private IActorContext _actorContext = null!;

        // RFC 0004 emission policy. The per-property gate is active when the clock is NOT a
        // controllable (FakeTimeProvider-style) clock — i.e. production + free-run DevHost — OR
        // when a TestKit override forces it on despite a controllable clock. Resolved once at
        // InitializeLogicBlock and cached. When inactive, Handle*ValueChanged sends as before.
        private bool _emissionPolicyActive;

        // Set from an optional injected EmissionPolicyForceMarker (TestKit WithEmissionPolicy(FromAttributes)):
        // forces the policy active even though the injected clock is controllable.
        private bool _forcePolicyFromAttributes;

        private bool _initializeDeferred;

        private IActorReference _persistenceManagerActorRef = null!; // set during initialization

        // Tracks whether LinkRuntimeActors has been processed (i.e. _servicePropertyHandlerActorRef
        // and friends are populated). Used to defer SendBindLogicBlockServices + Ready when
        // InitializeLogicBlock is processed first — which it currently is in
        // dale's LogicSystemConfigurationInitializer (Step 2 sends InitializeLogicBlock; Step 3
        // sends LinkRuntimeActors). Without deferring, the bind-message goes to a still-null
        // _servicePropertyHandlerActorRef and the ServicePropertyHandler never learns the
        // bindings, causing every subsequent property/set to be silently dropped.
        private bool _runtimeActorsLinked;

        // RFC 0004: tracks the deadline of the single outstanding flush timer (≤1 per block at a
        // time). Null when no flush is armed. Set in ScheduleEmissionFlush; cleared at the top of
        // OnEmissionFlushDue so the body can re-arm if further pending values remain.
        private DateTimeOffset? _scheduledFlushDeadline;

        // Key: ServiceIdentifier, Value: ServiceIdentifier
        private Dictionary<string, ServiceIdentifier> _serviceIdLookup = [];

        // Key: ServiceIdentifier, Value: ServiceIdentifier
        private Dictionary<ServiceIdentifier, string> _serviceIdentifierLookup = [];

        private IActorReference _serviceMeasuringPointHandlerActorRef = null!; // set during initialization

        private IActorReference _servicePropertyHandlerActorRef = null!; // set during initialization

        private bool _started;

        private TimeProvider _timeProvider = TimeProvider.System;

        private string? _vitalsActorName;

        private IActorVitalsCollector? _vitalsCollector;

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

                    _runtimeActorsLinked = true;

                    // If InitializeLogicBlock was processed before this message arrived,
                    // SendBindLogicBlockServices + Ready were deferred — fire them now so the
                    // ServicePropertyHandler learns the bindings before any property traffic.
                    if (_initializeDeferred)
                    {
                        _initializeDeferred = false;
                        SendBindLogicBlockServices();
                        Ready();
                    }

                    break;

                case InitializeLogicBlock m: // initialization
                    Id = m.LogicBlockId;
                    Name = m.LogicBlockName;

                    // RFC 0005: resolve the vitals collector + clock for per-[Timer] watchdog signals.
                    // Optional — absent in bare hosts and the TestKit, where timer measurement is skipped.
                    _vitalsCollector = m.ServiceProvider.GetService(typeof(IActorVitalsCollector)) as IActorVitalsCollector;
                    _timeProvider = m.ServiceProvider.GetService(typeof(TimeProvider)) as TimeProvider ?? TimeProvider.System;
                    _vitalsActorName = LogicBlockUtils.CreateLogicBlockName(Name, Id);

                    // RFC 0004: resolve emission-policy activation once. Force flag from an optional
                    // injected marker (TestKit override); otherwise active iff the clock is not a
                    // controllable (stepped / FakeTimeProvider) clock.
                    _forcePolicyFromAttributes = m.ServiceProvider.GetService(typeof(EmissionPolicyForceMarker)) is EmissionPolicyForceMarker;
                    _emissionPolicyActive = _forcePolicyFromAttributes || !ControllableClock.Detect(_timeProvider);

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

                    // RFC 0004: build one Throttler per bound service property + measuring point,
                    // resolved from each binding's attribute + CLR value type. Cheap no-op state when
                    // the policy is inactive (the gate bypasses), but always built so the override path
                    // and production share one construction site.
                    BuildThrottlers();

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

                    // Defer SendBindLogicBlockServices + Ready if LinkRuntimeActors hasn't been
                    // processed yet. SendBindLogicBlockServices sends to _servicePropertyHandlerActorRef,
                    // which is set by LinkRuntimeActors — sending to a null ref drops the bindings
                    // and breaks all subsequent property traffic. Ready() may also fire events that
                    // depend on the handler ref, so defer it together.
                    if (_runtimeActorsLinked)
                    {
                        SendBindLogicBlockServices();
                        Ready();
                    }
                    else
                    {
                        _initializeDeferred = true;
                        _logger.LogDebug("InitializeLogicBlock processed before LinkRuntimeActors; deferring SendBindLogicBlockServices + Ready until handler refs are set.");
                    }

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

                    // RFC 0004: the initial publish flows through Handle*ValueChanged with the gate
                    // active. The first Throttler.Offer for each member returns Emit (!HasEmitted),
                    // which force-emits and seeds lastEmitted/lastEmitAt — so the initial value is
                    // never throttled and subsequent changes are measured from start time.
                    _serviceBinder.PublishInitialStateUpdates(_logger);

                    // Schedule periodic state saves
                    ScheduleNextPeriodicStateSave(actorContext);

                    _actorContext.RespondToSender(new StartLogicBlockResponse());
                    break;

                case StopLogicBlockRequest: // stopping, before removal
                    // Guard only the snapshot: if Configure() aborted during InitializeLogicBlock,
                    // PersistentData is never initialised and CreateSnapshot() would throw,
                    // skipping the stop ack and hanging shutdown until timeout. Stopping(),
                    // _started reset, ClearRetainedMessages and the StopLogicBlockResponse ack
                    // must always run so the actor acks stop regardless of init state.
                    if (_persistentData.IsInitialized)
                    {
                        _persistentData.CreateSnapshot();
                    }

                    try
                    {
                        Stopping();
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Exception in Stopping() for logic block '{LogicBlockName}' ({LogicBlockId}); continuing shutdown.", Name, Id);
                    }

                    // RFC 0004: drain each throttler's exact current value before stopping, so the final
                    // retained state is exact (the deadband suppresses sub-threshold changes only during
                    // operation). Must run while _started is still true and bindings are live.
                    DrainThrottlers();

                    _started = false;
                    _serviceBinder.ClearRetainedMessages(_logger);
                    _actorContext.RespondToSender(new StopLogicBlockResponse());
                    break;

                case PublishServiceState: // after broker reconnect
                    _serviceBinder.PublishInitialStateUpdates(_logger);
                    break;

                case GetPersistentDataSnapshotRequest: // after stopping, before removal
                    // Same uninitialised-shutdown hang as the StopLogicBlockRequest /
                    // HandlePeriodicStateSave guards, just the next message in the runtime
                    // teardown sequence: the runtime sends StopLogicBlockRequest then
                    // GetPersistentDataSnapshotRequest, each awaiting an acknowledgement. If
                    // Configure() aborted during InitializeLogicBlock, PersistentData is never
                    // initialised and GetCurrentSnapshot() would throw, so the response is never
                    // sent and shutdown hangs until timeout. Always respond — with an empty
                    // snapshot when uninitialised — so the runtime's wait is always satisfied.
                    var snapshot = _persistentData.IsInitialized ? _persistentData.GetCurrentSnapshot() : new List<PersistentDataEntry>();
                    actorContext.RespondToSender(new GetPersistentDataSnapshotResponse(Id, snapshot));
                    break;

                case IContractMessage m: // delegate to contract
                    GetContractById(m.LogicBlockContractId).HandleContractMessage(m);
                    break;

                case IFunctionInterfaceMessage m: // delegate to logic interface
                    GetFunctionById(m.ToId).HandleMessage(m);
                    break;

                case SetServicePropertyValueRequest m: // from service proxy, set value and respond with current value
                    HandleSetServicePropertyValueRequest(actorContext, m);
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
        ///     Called once after the block has been configured (attribute-driven bindings are in place) and is ready to run,
        ///     but BEFORE the runtime has restored persisted <see cref="ServicePropertyAttribute" /> values, registered
        ///     per-contract sender instances, or fired <see cref="Starting" />. The right place to <b>attach event handlers</b>
        ///     to contract / interface elements and to do other block-local one-time setup that doesn't depend on SDK runtime
        ///     state.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Three things are NOT yet available in Ready:</b>
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <b>Persisted <see cref="ServicePropertyAttribute" /> values.</b> The runtime applies them between
        ///             <see cref="Ready" /> and <see cref="Starting" />, so in Ready every property still holds its C#
        ///             field-initialiser default. Code that reads a property expecting the operator-set / persisted value
        ///             (rather than its compile-time default) belongs in <see cref="Starting" />.
        ///         </item>
        ///         <item>
        ///             <b>Cross-block link topology.</b> The SDK-generated <c>GetLinkedXxx()</c> helpers from a
        ///             <see cref="LogicBlockContractAttribute" />-decorated contract return an empty collection in Ready —
        ///             per-contract sender instances are registered between Ready and Starting. Calling <c>GetLinkedXxx()</c>
        ///             from Ready is silently wrong (no error, no warning, just empty).
        ///         </item>
        ///         <item>
        ///             <b>Outbound emits.</b> Cross-block <c>this.SendStateUpdate(...)</c> calls iterate the still-empty link
        ///             set and reach no one. Writes through a <see cref="ServicePropertyAttribute" /> setter are gated by an
        ///             internal started flag, dropped, and logged at info level as
        ///             <c>"is not started, ignoring property change"</c>. Initial values for
        ///             <see cref="ServicePropertyAttribute" /> and <see cref="ServiceMeasuringPointAttribute" /> are
        ///             auto-published by the runtime after <see cref="Starting" /> — no manual emit needed for those.
        ///         </item>
        ///     </list>
        ///     <para>
        ///         <b>Rule of thumb:</b> if it depends on the SDK having finished its between-Ready-and-Starting work
        ///         (persistence restore, link registration, sender-instance attachment), it belongs in <see cref="Starting" />,
        ///         not Ready.
        ///     </para>
        /// </remarks>
        /// <seealso cref="Starting" />
        /// <seealso cref="Stopping" />
        protected abstract void Ready();

        /// <summary>
        ///     Called once after <see cref="Ready" /> and after the runtime has restored persisted
        ///     <see cref="ServicePropertyAttribute" /> values and registered per-contract sender instances. The right place
        ///     for setup that depends on SDK runtime state: reading persisted property values, enumerating contract links via
        ///     <c>GetLinkedXxx()</c>, scheduling first periodic ticks, and emitting initial cross-block contract state-updates.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>Typical Starting work:</b>
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             Configure external clients and protocol bindings from <see cref="ServicePropertyAttribute" />
        ///             values — persisted values are applied by now.
        ///         </item>
        ///         <item>
        ///             Snapshot the static link topology via the generated <c>GetLinkedXxx()</c> helpers if the block needs
        ///             to know its peers.
        ///         </item>
        ///         <item>
        ///             Schedule the first periodic tick via <see cref="InvokeSynchronizedAfter" /> — the canonical
        ///             self-rescheduling pattern: <see cref="Starting" /> kicks off the first
        ///             <c>InvokeSynchronizedAfter</c>; the scheduled action does its work and re-schedules itself.
        ///         </item>
        ///         <item>
        ///             Emit initial cross-block contract <c>[StateUpdate]</c> messages via <c>this.SendStateUpdate(...)</c> —
        ///             linked blocks typically rely on these at startup to know peer state. Initial values for
        ///             <see cref="ServicePropertyAttribute" /> and <see cref="ServiceMeasuringPointAttribute" /> are
        ///             auto-published by the runtime, so no manual emit is needed for those.
        ///         </item>
        ///     </list>
        /// </remarks>
        /// <seealso cref="Ready" />
        /// <seealso cref="Stopping" />
        protected virtual void Starting()
        {
        }

        /// <summary>
        ///     Called once before the block is removed, after the runtime processes a stop request. The right place to
        ///     <b>release resources acquired during the block's lifetime</b>: detach event handlers attached in
        ///     <see cref="Ready" />, cancel in-flight operations, dispose injected clients, flush pending I/O.
        /// </summary>
        /// <remarks>
        ///     <para>
        ///         <b>What's still available:</b> the block is still in its started state during Stopping (the internal
        ///         started flag is reset only after Stopping returns), so contract bindings, link topology, and
        ///         <see cref="ServicePropertyAttribute" /> values behave normally.
        ///     </para>
        ///     <para>
        ///         <b>Three things worth knowing:</b>
        ///     </para>
        ///     <list type="bullet">
        ///         <item>
        ///             <b>Persistence snapshot is already captured.</b> The runtime captures the persistent-state snapshot
        ///             before calling Stopping, so writes to a <see cref="ServicePropertyAttribute" /> from inside Stopping
        ///             do <em>not</em> survive a restart.
        ///         </item>
        ///         <item>
        ///             <b>Outbound emits are not guaranteed to be observed.</b> Linked blocks may already be shutting down;
        ///             do not rely on cross-block <c>this.SendStateUpdate(...)</c> calls from Stopping reaching peers.
        ///         </item>
        ///         <item>
        ///             <b>Exceptions don't halt shutdown.</b> Any exception thrown from Stopping is logged at error level
        ///             and swallowed — the block still stops, the runtime still acks. Stopping cannot be used to signal a
        ///             refused shutdown.
        ///         </item>
        ///     </list>
        ///     <para>
        ///         <b>Symmetry rule:</b> anything attached or scheduled in <see cref="Ready" /> / <see cref="Starting" />
        ///         should typically be detached or cancelled here.
        ///     </para>
        /// </remarks>
        /// <seealso cref="Ready" />
        /// <seealso cref="Starting" />
        protected virtual void Stopping()
        {
        }

        /// <summary>
        ///     Binds this logic block's interfaces, contracts, services and timers from their declarative attributes.
        ///     Internal infrastructure invoked by the runtime; not an extension point.
        /// </summary>
        internal void Configure(ILogicBlockConfigurationBuilder configurationBuilder)
        {
            DeclarativeInterfaceBinder.BindInterfacesFromAttributes(this, configurationBuilder.Interfaces);
            DeclarativeContractBinder.BindContractsFromAttributes(this, configurationBuilder.Contracts);
            DeclarativeServiceBinder.BindServicesFromAttributes(this, (ServiceBinder)configurationBuilder.Services);
            DeclarativeTimerBinder.BindTimersFromAttributes(this, configurationBuilder.Timers);
        }

        /// <summary>
        ///     RFC 0004: constructs one <see cref="Throttler" /> per bound service property and per bound
        ///     measuring point from its declarative emission attributes, into the matching stream collection
        ///     (<see cref="_servicePropertyThrottlers" /> / <see cref="_measuringPointThrottlers" />) keyed by
        ///     (ServiceIdentifier, member name). A dual-annotated member thus gets one gate per stream, so the
        ///     two streams don't cross-suppress. The attribute (an <see cref="IThrottleConfigured" />) is read
        ///     off the binding's root source <see cref="System.Reflection.PropertyInfo" />; the value type
        ///     comes from <see cref="ServiceBinding.TargetPropertyType" />.
        /// </summary>
        private void BuildThrottlers()
        {
            BuildThrottlersFor(_serviceBinder.GetAllServicePropertyBindings(), _servicePropertyThrottlers);
            BuildThrottlersFor(_serviceBinder.GetAllServiceMeasuringPointBindings(), _measuringPointThrottlers);
        }

        private void BuildThrottlersFor(IReadOnlyDictionary<string, IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>> bindings,
                                        Dictionary<(string ServiceIdentifier, string MemberIdentifier), Throttler> throttlers)
        {
            foreach (var (serviceIdentifier, interfaceMap) in bindings)
            {
                foreach (var perInterface in interfaceMap.Values)
                {
                    foreach (var (memberIdentifier, binding) in perInterface)
                    {
                        var configured = ResolveThrottleConfigured(binding, out var configuredSource);
                        if (configured == null)
                        {
                            continue;
                        }

                        // The custom-threshold scan (DF-34) probes the assembly that DECLARES the property the
                        // knobs were read from — the same compilation DALE034 validated against. For a knob
                        // inherited from a shared [ServiceInterface] (DF-33), that is the interface library's
                        // assembly (where a shared custom IChangeThreshold<T> lives), not the block's assembly.
                        var declaringAssembly = configuredSource?.DeclaringType?.Assembly ?? binding.Source?.GetType().Assembly;
                        var policy = ThrottlePolicy.FromConfigured(configured, binding.TargetPropertyType, declaringAssembly);

                        // DF-34: a configured MinChange that resolves to no IChangeThreshold<T> is a
                        // misconfiguration, not a silent no-op. Fail fast at start. DALE034 already errors at
                        // compile time, so this only fires when that gate was bypassed or the impl is not
                        // instantiable.
                        if (!string.IsNullOrEmpty(configured.MinChange) && policy.Threshold == null)
                        {
                            var valueTypeName = (Nullable.GetUnderlyingType(binding.TargetPropertyType) ?? binding.TargetPropertyType).Name;
                            throw new InvalidOperationException($"Service member '{memberIdentifier}' on service '{serviceIdentifier}' sets MinChange='{configured.MinChange}', " +
                                                                $"but no IChangeThreshold<{valueTypeName}> is built-in or discoverable in assembly " +
                                                                $"'{declaringAssembly?.GetName().Name}'. Implement IChangeThreshold<{valueTypeName}> in that assembly (with a " +
                                                                "parameterless constructor), or remove MinChange.");
                        }

                        throttlers[(serviceIdentifier, memberIdentifier)] = new Throttler(policy);
                    }
                }
            }
        }

        private static IThrottleConfigured? ResolveThrottleConfigured(ServiceBinding binding, out PropertyInfo? source)
        {
            // DF-33: the emission knobs follow the schema-from-interface precedent (PropertyMetadataBuilder.
            // BuildSplit). The impl property wins if it declares its own [ServiceProperty]/[ServiceMeasuringPoint];
            // otherwise the knobs are inherited from the [ServiceInterface] property the schema is declared on.
            // This lets a family of blocks sharing a [ServiceInterface] declare emission policy once (DRY),
            // the same way it declares the schema once. For a non-interface (extra) property the schema source
            // is the impl property itself, so this collapses to "read from the impl property".
            // `source` is the property the knobs were actually read from — used to probe the right assembly
            // for a custom IChangeThreshold<T> (DF-34).
            var implConfigured = ConfiguredFrom(binding.RootSourcePropertyInfo);
            if (implConfigured != null)
            {
                source = binding.RootSourcePropertyInfo;
                return implConfigured;
            }

            source = binding.SchemaSourcePropertyInfo;
            return ConfiguredFrom(binding.SchemaSourcePropertyInfo);
        }

        private static IThrottleConfigured? ConfiguredFrom(PropertyInfo? property)
        {
            if (property == null)
            {
                return null;
            }

            // The same PropertyInfo carries either [ServiceProperty] or [ServiceMeasuringPoint];
            // both implement IThrottleConfigured. Reflect whichever is present.
            return (IThrottleConfigured?)property.GetCustomAttribute(typeof(ServicePropertyAttribute), true) ??
                   (IThrottleConfigured?)property.GetCustomAttribute(typeof(ServiceMeasuringPointAttribute), true);
        }

        /// <summary>
        ///     RFC 0004: discards a throttler's pending held flush (and its emitted state) on a value-clear,
        ///     so the cleared edge is not undone by a later trailing flush. Reconstructs the gate from its
        ///     own <see cref="Throttler.Policy" /> via a fresh <see cref="Throttler" /> — there is no
        ///     in-place cancel on the gate. The caller passes the member's stream collection.
        /// </summary>
        private static void ResetThrottlerPending(Dictionary<(string ServiceIdentifier, string MemberIdentifier), Throttler> throttlers,
                                                  (string ServiceIdentifier, string MemberIdentifier) key)
        {
            if (throttlers.TryGetValue(key, out var existing))
            {
                throttlers[key] = new Throttler(existing.Policy);
            }
        }

        /// <summary>
        ///     RFC 0004: ensures one trailing-edge flush is scheduled at the earliest pending deadline
        ///     across all throttlers. Mirrors <see cref="ScheduleNextTimerTick" /> — a single idiomatic
        ///     self-send via the pause-gated / stepper-aware self-scheduling path. The flush body
        ///     (<see cref="OnEmissionFlushDue" />) coalesces and reschedules the next-earliest, so an
        ///     extra wakeup at worst finds nothing due and reschedules.
        ///     <para>
        ///         The flush is dispatched as an <c>InvokeSynchronized</c> action rather than a bespoke
        ///         self-message: the action wrapper is what both the production actor loop and the
        ///         TestKit's virtual clock (AdvanceTime / FlushPendingActions) actually pump, so the
        ///         trailing flush is observable under deterministic tests. A raw self-message would be
        ///         delivered in production but silently dropped by the TestKit, which never re-dispatches
        ///         non-action self-messages.
        ///     </para>
        /// </summary>
        private void ScheduleEmissionFlush(DateTimeOffset earliestDeadline)
        {
            // RFC 0004 invariant: at most one outstanding flush per block. Skip arming a new timer
            // when one is already scheduled for the same or an earlier deadline — the common case
            // for a burst of holds sharing _lastEmitAt + MinInterval as their deadline. Only arm
            // (or re-arm) when the new deadline is strictly earlier than the in-flight one, which
            // replaces it (InvokeSynchronizedAfter cannot be cancelled; the earlier wakeup wins and
            // the late one fires into an idempotent no-op or a same-deadline reschedule).
            if (_scheduledFlushDeadline is not null && earliestDeadline >= _scheduledFlushDeadline.Value)
            {
                return;
            }

            var now = _timeProvider.GetUtcNow();
            var delay = earliestDeadline - now;
            if (delay < TimeSpan.Zero)
            {
                delay = TimeSpan.Zero;
            }

            _scheduledFlushDeadline = earliestDeadline;
            InvokeSynchronizedAfter(OnEmissionFlushDue, delay);
        }

        /// <summary>
        ///     RFC 0004: flushes every throttler whose hold deadline has elapsed, emitting its pending
        ///     value, then reschedules a single wakeup for the earliest still-pending deadline (if any).
        /// </summary>
        private void OnEmissionFlushDue()
        {
            if (!_started)
            {
                return;
            }

            // Clear the outstanding-flush sentinel first, before any work, so that the reschedule
            // call below (if further pending values remain) correctly re-arms rather than no-ops.
            _scheduledFlushDeadline = null;

            var now = _timeProvider.GetUtcNow();
            var nextDeadline = DateTimeOffset.MaxValue;
            var hasNext = false;

            // Each stream flushes to its own central handler; the per-stream emit closure keeps the
            // routing explicit (no kind flag), and the next-deadline is coalesced across both streams.
            FlushDue(_servicePropertyThrottlers,
                     (serviceId, member, value) => _actorContext.SendTo(_servicePropertyHandlerActorRef, new ServicePropertyValueChanged(serviceId, member, value)));
            FlushDue(_measuringPointThrottlers,
                     (serviceId, member, value) => _actorContext.SendTo(_serviceMeasuringPointHandlerActorRef, new ServiceMeasuringPointValueChanged(serviceId, member, value)));

            if (hasNext)
            {
                ScheduleEmissionFlush(nextDeadline);
            }

            void FlushDue(Dictionary<(string ServiceIdentifier, string MemberIdentifier), Throttler> throttlers, Action<ServiceIdentifier, string, object?> emit)
            {
                foreach (var ((serviceIdentifier, memberIdentifier), throttler) in throttlers)
                {
                    if (throttler.HasPending && throttler.PendingDeadline <= now && throttler.TryFlush(now, out var value) &&
                        _serviceIdLookup.TryGetValue(serviceIdentifier, out var serviceId))
                    {
                        emit(serviceId, memberIdentifier, value);
                    }

                    if (throttler.HasPending && throttler.PendingDeadline < nextDeadline)
                    {
                        nextDeadline = throttler.PendingDeadline;
                        hasNext = true;
                    }
                }
            }
        }

        /// <summary>
        ///     RFC 0004: on stop, emit each throttled member's exact current value if it differs from the
        ///     throttler's last-emitted value — bypassing throttle and deadband — so the final retained
        ///     state is exact. Reads the current value straight from the binding getter.
        /// </summary>
        private void DrainThrottlers()
        {
            if (!_emissionPolicyActive)
            {
                return;
            }

            DrainBindings(_serviceBinder.GetAllServicePropertyBindings(),
                          _servicePropertyThrottlers,
                          (serviceId, member, current) => _actorContext.SendTo(_servicePropertyHandlerActorRef, new ServicePropertyValueChanged(serviceId, member, current)));
            DrainBindings(_serviceBinder.GetAllServiceMeasuringPointBindings(),
                          _measuringPointThrottlers,
                          (serviceId, member, current) =>
                              _actorContext.SendTo(_serviceMeasuringPointHandlerActorRef, new ServiceMeasuringPointValueChanged(serviceId, member, current)));
        }

        private void DrainBindings(IReadOnlyDictionary<string, IReadOnlyDictionary<Type, IReadOnlyDictionary<string, ServiceBinding>>> bindings,
                                   Dictionary<(string ServiceIdentifier, string MemberIdentifier), Throttler> throttlers,
                                   Action<ServiceIdentifier, string, object?> emit)
        {
            foreach (var (serviceIdentifier, interfaceMap) in bindings)
            {
                if (!_serviceIdLookup.TryGetValue(serviceIdentifier, out var serviceId))
                {
                    continue;
                }

                foreach (var perInterface in interfaceMap.Values)
                {
                    foreach (var (memberIdentifier, binding) in perInterface)
                    {
                        if (!throttlers.TryGetValue((serviceIdentifier, memberIdentifier), out var throttler))
                        {
                            continue;
                        }

                        object? current;
                        try
                        {
                            current = binding.Getter(binding.Source);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogWarning("Failed to read current value for drain of {ServiceIdentifier}.{MemberIdentifier}: {ExceptionMessage}",
                                               serviceIdentifier,
                                               memberIdentifier,
                                               ex.Message);
                            continue;
                        }

                        if (throttler.HasEmitted && Equals(throttler.LastEmitted, current))
                        {
                            continue;
                        }

                        emit(serviceId, memberIdentifier, current);
                    }
                }
            }
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

            // RFC 0004: when the policy is inactive (controllable clock without override), or no
            // throttler exists for this member, send as before. Otherwise gate via the throttler.
            if (!_emissionPolicyActive || !_servicePropertyThrottlers.TryGetValue((args.ServiceIdentifier, args.PropertyIdentifier), out var throttler))
            {
                _actorContext.SendTo(_servicePropertyHandlerActorRef, new ServicePropertyValueChanged(serviceIdentifier, args.PropertyIdentifier, args.Value));
                return;
            }

            switch (throttler.Offer(args.Value, _timeProvider.GetUtcNow()).Action)
            {
                case EmitAction.Emit:
                    _actorContext.SendTo(_servicePropertyHandlerActorRef, new ServicePropertyValueChanged(serviceIdentifier, args.PropertyIdentifier, args.Value));
                    break;
                case EmitAction.Drop:
                    break;
                case EmitAction.Hold:
                    ScheduleEmissionFlush(throttler.PendingDeadline);
                    break;
            }
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

            // RFC 0004: same gate as the service-property path, keyed by measuring-point identifier.
            if (!_emissionPolicyActive || !_measuringPointThrottlers.TryGetValue((args.ServiceIdentifier, args.MeasuringPointIdentifier), out var throttler))
            {
                _actorContext.SendTo(_serviceMeasuringPointHandlerActorRef, new ServiceMeasuringPointValueChanged(serviceIdentifier, args.MeasuringPointIdentifier, args.Value));
                return;
            }

            switch (throttler.Offer(args.Value, _timeProvider.GetUtcNow()).Action)
            {
                case EmitAction.Emit:
                    _actorContext.SendTo(_serviceMeasuringPointHandlerActorRef,
                                         new ServiceMeasuringPointValueChanged(serviceIdentifier, args.MeasuringPointIdentifier, args.Value));
                    break;
                case EmitAction.Drop:
                    break;
                case EmitAction.Hold:
                    ScheduleEmissionFlush(throttler.PendingDeadline);
                    break;
            }
        }

        private void HandleServicePropertyCleared(object sender, ServicePropertyClearedEventArgs args)
        {
            if (!_serviceIdLookup.TryGetValue(args.ServiceIdentifier, out var serviceIdentifier))
            {
                _logger.LogWarning("Unknown service identifier for identifier '{ServiceIdentifier}' in logic block '{Id}'.", args.ServiceIdentifier, Id);
                return;
            }

            // RFC 0004: a clear is a state-significant edge — emit immediately (bypass the gate) and
            // discard any pending held flush so the just-cleared value is not re-emitted afterwards.
            ResetThrottlerPending(_servicePropertyThrottlers, (args.ServiceIdentifier, args.PropertyIdentifier));

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

            // RFC 0004: same clear-edge semantics as the property path — emit immediately and discard
            // any pending held flush for this measuring point.
            ResetThrottlerPending(_measuringPointThrottlers, (args.ServiceIdentifier, args.MeasuringPointIdentifier));

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

            if (_vitalsCollector == null)
            {
                callback();
                return;
            }

            // RFC 0005 watchdog: measure the callback duration and the scheduler jitter (actual minus
            // requested inter-tick delay), reporting both to the vitals core. The callback's exception
            // still propagates (the finally only reports), so behaviour is otherwise unchanged.
            var jitter = ComputeTimerJitter(message.Identifier, interval);
            var startTimestamp = _timeProvider.GetTimestamp();
            try
            {
                callback();
            }
            finally
            {
                _vitalsCollector.OnTimerCallback(_vitalsActorName!, _timeProvider.GetElapsedTime(startTimestamp), jitter);
            }
        }

        private TimeSpan ComputeTimerJitter(string timerIdentifier, TimeSpan interval)
        {
            var jitter = TimeSpan.Zero;
            if (_timerLastTickTimestamp.TryGetValue(timerIdentifier, out var lastTimestamp))
            {
                jitter = _timeProvider.GetElapsedTime(lastTimestamp) - interval;
            }

            _timerLastTickTimestamp[timerIdentifier] = _timeProvider.GetTimestamp();
            return jitter;
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

            // Defence-in-depth, mirroring the StopLogicBlockRequest guard: if Configure() aborted
            // during InitializeLogicBlock, PersistentData is never initialised and both
            // CreateSnapshot() and GetCurrentSnapshot() would throw. CreateSnapshot +
            // GetCurrentSnapshot are one logical "snapshot and publish" operation, so guard them
            // together; rescheduling a periodic save that can never produce a snapshot is
            // pointless, so it stays inside the guard too. The normal (initialised) path is
            // unchanged — all three statements still run exactly as before.
            if (!_persistentData.IsInitialized)
            {
                _logger.LogWarning("Logic block '{Id}' has uninitialised PersistentData (Configure() likely aborted); skipping periodic state save.", Id);
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