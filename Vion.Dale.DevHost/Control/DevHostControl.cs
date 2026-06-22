using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using System.Xml;
using Vion.Contracts.Codec;
using Vion.Contracts.TypeRef;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Diagnostics;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Control
{
    /// <inheritdoc />
    internal sealed class DevHostControl : IDevHostControl, IDisposable
    {
        // In-flight handler monitor — the exact-quiescence complement to mailbox depth. Held for the lazily
        // built stepper's barrier.
        private readonly InFlightActivityMonitor _activityMonitor;

        private readonly IActorSystem _actorSystem;

        private readonly ConcurrentDictionary<(string ServiceProviderId, string ServiceId, string ContractId), double> _analogOutputs = new();

        // The virtual clock at construction — the per-generation clean baseline. On a stepped host the clock
        // only moves via explicit advance/step, so VirtualTimeUtc > this means the generation has been dirtied
        // (a prior run or manual stepping) and is no longer reproducible from a clean slate.
        private readonly DateTimeOffset _baselineUtc;

        private readonly DevConfiguration _configuration;

        // Last-known mocked-output value per (serviceProviderId, serviceId, contractId) — fed by the
        // DigitalOutputChanged / AnalogOutputChanged events the mock output handlers raise (the same events
        // republished to subscribers), read by GetDigitalOutput / GetAnalogOutput. The output read-cache
        // mirroring the _values service-property cache; an absent key means the output was never Set.
        private readonly ConcurrentDictionary<(string ServiceProviderId, string ServiceId, string ContractId), bool> _digitalOutputs = new();

        private readonly DevHostEvents _events;

        // Event-stream fan-out + pending WaitForAsync waiters share one lock — both touched from actor threads.
        private readonly object _gate = new();

        private readonly DevHostIntrospection _introspection;

        private readonly DevHostLogSink _logSink;

        private readonly MessageTap _messageTap;

        private readonly DevHostRunControl _runControl;

        // Engine-owned virtual schedule of pending delayed sends — read by next-event stepping to find the
        // next scheduled event. Held for the lazily built stepper.
        private readonly IVirtualSchedule _schedule;

        private readonly List<Action<DevHostEvent>> _subscribers = new();

        // Deterministic stepping deps. Held but unused unless AdvanceAsync is called; the stepper (which
        // validates the clock is a FakeTimeProvider) is built lazily so a non-stepping host isn't burdened
        // and a real-clock host isn't rejected at construction.
        private readonly TimeProvider _timeProvider;

        // Last-known value per (serviceConfigId, memberName) — fed by the change events, read by GetProperty.
        private readonly ConcurrentDictionary<(string ServiceId, string Member), object?> _values = new();

        private readonly RuntimeVitals _vitals;

        private readonly List<Func<DevHostEvent, bool>> _waiters = new();

        // service-id (GUID carried on the events) → logic block name. Built lazily: in a headless boot the
        // config's Services aren't populated until DevHostIntrospection runs, which may be after construction.
        private Dictionary<string, string>? _serviceToLogicBlock;

        private DeterministicStepper? _stepper;

        public DevHostControl(DevConfiguration configuration,
                              DevHostEvents events,
                              DevHostLogSink logSink,
                              DevHostIntrospection introspection,
                              IActorSystem actorSystem,
                              MessageTap messageTap,
                              DevHostRunControl runControl,
                              RuntimeVitals vitals,
                              InFlightActivityMonitor activityMonitor,
                              VirtualSchedule schedule,
                              TimeProvider timeProvider)
        {
            _configuration = configuration;
            _events = events;
            _logSink = logSink;
            _introspection = introspection;
            _actorSystem = actorSystem;
            _messageTap = messageTap;
            _runControl = runControl;
            _vitals = vitals;
            _activityMonitor = activityMonitor;
            _schedule = schedule;
            _timeProvider = timeProvider;

            // Captured before the logic system starts and before any advance/step — the clean baseline for
            // this host generation (the deterministic epoch on a stepped host).
            _baselineUtc = timeProvider.GetUtcNow();

            _events.ServicePropertyChanged += OnServiceProperty;
            _events.ServicePropertyWriteAcknowledged += OnWriteAcknowledged;
            _events.ServiceMeasuringPointChanged += OnMeasuringPoint;
            _events.DigitalInputChanged += OnDigitalInput;
            _events.DigitalOutputChanged += OnDigitalOutput;
            _events.AnalogInputChanged += OnAnalogInput;
            _events.AnalogOutputChanged += OnAnalogOutput;
        }

        /// <inheritdoc />
        public bool IsPaused
        {
            get => _runControl.IsPaused;
        }

        /// <inheritdoc />
        public bool IsStepped
        {
            // Structural detection: same check DeterministicStepper.BindAdvance performs. Avoids a
            // compile-time reference to the test-only Microsoft.Extensions.TimeProvider.Testing assembly.
            get =>
                _timeProvider.GetType().GetMethod("Advance", BindingFlags.Public | BindingFlags.Instance, null, new[] { typeof(TimeSpan) }, null) is { ReturnType: var r } &&
                r == typeof(void);
        }

        /// <inheritdoc />
        public bool CanReset
        {
            get => _runControl.CanReset;
        }

        /// <inheritdoc />
        public void Pause()
        {
            _runControl.Pause();
        }

        /// <inheritdoc />
        public void Resume()
        {
            _runControl.Resume();
        }

        /// <inheritdoc />
        public Task AdvanceAsync(TimeSpan virtualTime, CancellationToken cancellationToken = default)
        {
            return EnsureStepper().AdvanceByAsync(virtualTime, cancellationToken);
        }

        /// <inheritdoc />
        public Task AdvanceToNextEventAsync(CancellationToken cancellationToken = default)
        {
            return EnsureStepper().AdvanceToNextEventAsync(cancellationToken);
        }

        /// <inheritdoc />
        public DateTimeOffset VirtualTimeUtc
        {
            get => _timeProvider.GetUtcNow();
        }

        /// <inheritdoc />
        public bool HasAdvancedFromBaseline
        {
            get => IsStepped && _timeProvider.GetUtcNow() > _baselineUtc;
        }

        /// <inheritdoc />
        public bool TryRequestReset()
        {
            return _runControl.TryRequestReset();
        }

        /// <inheritdoc />
        public IDisposable OnResetRequested(Action handler)
        {
            return _runControl.OnResetRequested(handler);
        }

        /// <inheritdoc />
        public bool TryRequestTopologySwitch(string topologyId)
        {
            return _runControl.TryRequestTopologySwitch(topologyId);
        }

        /// <inheritdoc />
        public string? RequestedTopology
        {
            get => _runControl.RequestedTopology;
        }

        public IReadOnlyList<LogicBlockInfo> ListLogicBlocks()
        {
            return _configuration.LogicBlocks.Select(b => new LogicBlockInfo(b.Id, b.Name, b.LogicBlockType.Name, b.Services.Select(s => s.Id).ToList())).ToList();
        }

        public ConfigurationOutput GetConfiguration()
        {
            return _introspection.BuildConfiguration();
        }

        public object? GetProperty(string logicBlockIdOrName, string propertyName)
        {
            var logicBlock = ResolveLogicBlock(logicBlockIdOrName);
            if (logicBlock is null || !_introspection.TryGetServiceId(logicBlock.Id, propertyName, out var serviceId))
            {
                return null;
            }

            return _values.TryGetValue((serviceId, propertyName), out var value) ? value : null;
        }

        public object? GetProperty(string logicBlockIdOrName, string serviceIdentifier, string propertyName)
        {
            var logicBlock = ResolveLogicBlock(logicBlockIdOrName);
            if (logicBlock is null || !_introspection.TryGetServiceId(logicBlock.Id, serviceIdentifier, propertyName, out var serviceId))
            {
                return null;
            }

            return _values.TryGetValue((serviceId, propertyName), out var value) ? value : null;
        }

        public IReadOnlyDictionary<string, object?> GetAllProperties(string logicBlockIdOrName)
        {
            var result = new Dictionary<string, object?>();
            var logicBlock = ResolveLogicBlock(logicBlockIdOrName);
            if (logicBlock is null)
            {
                return result;
            }

            foreach (var propertyName in _introspection.PropertyNames(logicBlock.Id))
            {
                result[propertyName] = GetProperty(logicBlock.Id, propertyName);
            }

            return result;
        }

        public Task SetPropertyAsync(string logicBlockIdOrName, string propertyName, object? value)
        {
            var logicBlock = ResolveLogicBlock(logicBlockIdOrName) ?? throw new InvalidOperationException($"Unknown logic block '{logicBlockIdOrName}'.");

            if (!_introspection.TryGetServiceId(logicBlock.Id, propertyName, out var serviceId))
            {
                throw new InvalidOperationException($"Logic block '{logicBlock.Name}' has no service property or measuring point named '{propertyName}'.");
            }

            return SetServicePropertyValueAsync(serviceId, propertyName, value);
        }

        public Task SetPropertyAsync(string logicBlockIdOrName, string serviceIdentifier, string propertyName, object? value)
        {
            var logicBlock = ResolveLogicBlock(logicBlockIdOrName) ?? throw new InvalidOperationException($"Unknown logic block '{logicBlockIdOrName}'.");

            if (!_introspection.TryGetServiceId(logicBlock.Id, serviceIdentifier, propertyName, out var serviceId))
            {
                throw new
                    InvalidOperationException($"Logic block '{logicBlock.Name}' has no service '{serviceIdentifier}' with a property or measuring point named '{propertyName}'.");
            }

            return SetServicePropertyValueAsync(serviceId, propertyName, value);
        }

        public async Task SetServicePropertyValueAsync(string serviceId, string propertyName, object? value)
        {
            var logicBlock = _configuration.LogicBlocks.FirstOrDefault(lb => lb.Services.Any(s => s.Id == serviceId)) ??
                             throw new ServicePropertyWriteException(ServicePropertyWriteException.ReasonUnknownService, null, $"Unknown service id '{serviceId}'.");

            // Reject a write the block can't apply UP FRONT, loudly. Otherwise the binder throws inside the
            // actor, the middleware swallows it, the write ack times out, and the HTTP path returns 200 — a
            // silent no-op that misleads an agent or developer into thinking the value took (the trip wire).
            switch (_introspection.GetServicePropertyWriteState(serviceId, propertyName))
            {
                case ServicePropertyWriteState.Unknown:
                    throw new ServicePropertyWriteException(ServicePropertyWriteException.ReasonUnknownMember,
                                                            propertyName,
                                                            $"No service property '{propertyName}' on service '{serviceId}'.");
                case ServicePropertyWriteState.ReadOnly:
                    throw new ServicePropertyWriteException(ServicePropertyWriteException.ReasonReadOnly,
                                                            propertyName,
                                                            $"Service property '{propertyName}' is read-only and cannot be set.");
            }

            // Decode JSON values (the HTTP path delivers JsonElement) into the precise CLR type the block
            // expects; CLR values from in-process callers pass through unchanged.
            var typedValue = NormalizeValue(serviceId, propertyName, value);

            var logicBlockActor = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(logicBlock.Name, logicBlock.Id));
            var handler = _actorSystem.LookupByName(nameof(MockServicePropertyHandler));

            // The actor applies the set asynchronously; the SendTo below is fire-and-forget. Await the
            // write's OWN acknowledgement — the block's SetServicePropertyValueResponse, surfaced as
            // ServicePropertyWriteAcknowledged — so a read-after-write reflects the new value. The ack is
            // correlated with this write's round trip: it cannot be satisfied by a stale in-flight publish
            // (e.g. the block's initial startup state — a change-event-based ack raced exactly that), and
            // it fires for no-op sets too, which raise no change event. The ack arrives FIFO-after the
            // change event, so the value cache is current when the await releases. The timeout is the
            // safety net for writes the block never applied (unknown member, actor-side throw — the
            // swallowed-exception hollow ack ScenarioRunner's rejected-write detection looks for).
            var applied = WaitForAsync(e => e is ServicePropertyWriteAcknowledged ack && ack.ServiceId == serviceId && ack.Property == propertyName ? (object)ack : null,
                                       TimeSpan.FromSeconds(5));

            _actorSystem.SendTo(handler,
                                new MockSetServicePropertyValue(logicBlockActor, new SetServicePropertyValueRequest(new ServiceIdentifier(serviceId), propertyName, typedValue!)));

            await applied.ConfigureAwait(false);
        }

        public Task SetDigitalInputAsync(string serviceProviderId, string serviceId, string contractId, bool value)
        {
            DriveServiceProviderInput(nameof(DigitalInputHandler), serviceProviderId, serviceId, contractId, JsonSerializer.SerializeToElement(value));
            return Task.CompletedTask;
        }

        public Task SetAnalogInputAsync(string serviceProviderId, string serviceId, string contractId, double value)
        {
            DriveServiceProviderInput(nameof(AnalogInputHandler), serviceProviderId, serviceId, contractId, JsonSerializer.SerializeToElement(value));
            return Task.CompletedTask;
        }

        public Task DriveServiceProviderContractAsync(string handlerName, string serviceProviderId, string serviceId, string contractId, JsonElement value)
        {
            DriveServiceProviderInput(handlerName, serviceProviderId, serviceId, contractId, value);
            return Task.CompletedTask;
        }

        // Drive an input contract through its generic stand-in (RFC 0010): the stand-in is registered under the
        // contract's handler name. The digital/analog HAL contracts keep their well-known handler names; the
        // generic serviceProviderSet passes the resolved ContractHandlerActorName.
        private void DriveServiceProviderInput(string handlerName, string serviceProviderId, string serviceId, string contractId, JsonElement value)
        {
            var handler = _actorSystem.LookupByName(handlerName);
            _actorSystem.SendTo(handler, new MockSetServiceProviderInputMessage(new ServiceProviderContractId(serviceProviderId, serviceId, contractId), value));
        }

        public bool? GetDigitalOutput(string serviceProviderId, string serviceId, string contractId)
        {
            return _digitalOutputs.TryGetValue((serviceProviderId, serviceId, contractId), out var value) ? value : null;
        }

        public double? GetAnalogOutput(string serviceProviderId, string serviceId, string contractId)
        {
            return _analogOutputs.TryGetValue((serviceProviderId, serviceId, contractId), out var value) ? value : null;
        }

        public void PublishAllStates()
        {
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(DigitalInputHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(DigitalOutputHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(AnalogInputHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(AnalogOutputHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(MockServicePropertyHandler)), new MockPublishAllStatesMessage());
            _actorSystem.SendTo(_actorSystem.LookupByName(nameof(MockServiceMeasuringPointHandler)), new MockPublishAllStatesMessage());
        }

        public IDisposable Subscribe(Action<DevHostEvent> sink)
        {
            if (sink is null)
            {
                throw new ArgumentNullException(nameof(sink));
            }

            lock (_gate)
            {
                _subscribers.Add(sink);
            }

            return new Token(() =>
                             {
                                 lock (_gate)
                                 {
                                     _subscribers.Remove(sink);
                                 }
                             });
        }

        public async Task<T?> WaitForAsync<T>(Func<DevHostEvent, T?> selector, TimeSpan timeout, CancellationToken cancellationToken = default)
            where T : class
        {
            if (selector is null)
            {
                throw new ArgumentNullException(nameof(selector));
            }

            var tcs = new TaskCompletionSource<T?>(TaskCreationOptions.RunContinuationsAsynchronously);

            bool Waiter(DevHostEvent e)
            {
                var match = selector(e);
                if (match is null)
                {
                    return false;
                }

                tcs.TrySetResult(match);
                return true;
            }

            lock (_gate)
            {
                _waiters.Add(Waiter);
            }

            using var cts = new CancellationTokenSource(timeout);
            using var externalCancellation = cancellationToken.CanBeCanceled ? cancellationToken.Register(() => tcs.TrySetResult(null)) : default;
            using (cts.Token.Register(() => tcs.TrySetResult(null)))
            {
                try
                {
                    return await tcs.Task.ConfigureAwait(false);
                }
                finally
                {
                    lock (_gate)
                    {
                        _waiters.Remove(Waiter);
                    }
                }
            }
        }

        public IDisposable SubscribeLogs(Action<LogLine> sink)
        {
            return _logSink.Subscribe(sink);
        }

        public IReadOnlyList<LogLine> RecentLogs(int max = 500)
        {
            return _logSink.Recent(max);
        }

        public IReadOnlyList<TappedMessage> RecordedMessages(string? logicBlockIdOrName = null)
        {
            if (logicBlockIdOrName is null)
            {
                return _messageTap.Snapshot();
            }

            var logicBlock = ResolveLogicBlock(logicBlockIdOrName);
            if (logicBlock is null)
            {
                return Array.Empty<TappedMessage>();
            }

            return _messageTap.Snapshot(LogicBlockUtils.CreateLogicBlockName(logicBlock.Name, logicBlock.Id));
        }

        public void Dispose()
        {
            _events.ServicePropertyChanged -= OnServiceProperty;
            _events.ServicePropertyWriteAcknowledged -= OnWriteAcknowledged;
            _events.ServiceMeasuringPointChanged -= OnMeasuringPoint;
            _events.DigitalInputChanged -= OnDigitalInput;
            _events.DigitalOutputChanged -= OnDigitalOutput;
            _events.AnalogInputChanged -= OnAnalogInput;
            _events.AnalogOutputChanged -= OnAnalogOutput;
        }

        // Lazy: building the stepper validates the clock is a FakeTimeProvider, so a real-clock host is only
        // rejected when stepping is actually requested — not at construction.
        private DeterministicStepper EnsureStepper()
        {
            return _stepper ??= new DeterministicStepper(_timeProvider, new QuiescenceBarrier(_vitals, _activityMonitor), _schedule);
        }

        // JSON → typed CLR for the HTTP set path (moved here when IDevHostStateProvider was collapsed into the
        // control surface). Re-parses the property's JSON Schema into a TypeRef and delegates to the same
        // PropertyValueCodec the runtime uses. CLR values pass through untouched.
        private object? NormalizeValue(string serviceId, string propertyName, object? value)
        {
            var isJson = value is JsonElement || value is JsonNode;
            if (!isJson)
            {
                return value;
            }

            if (!_introspection.TryGetPropertyConversion(serviceId, propertyName, out var schema, out var clrType) || schema is null || clrType is null)
            {
                // No schema/type available — hand the raw value through rather than fail.
                return value;
            }

            var typeRef = TypeSchemaSerialization.FromJsonSchema(schema).Type;

            // The codec reads JsonElement-backed nodes. The HTTP path already produces those (JsonElement →
            // re-parsed). An in-process JsonNode may instead be CLR-backed (e.g. JsonValue.Create(99)), which the
            // codec can't read — round-trip it through its JSON string so any JsonNode input honours the contract.
            var json = value switch
            {
                JsonElement je when je.ValueKind == JsonValueKind.Null => null,
                JsonElement je => JsonNode.Parse(je.GetRawText()),
                JsonNode node => JsonNode.Parse(node.ToJsonString()),
                _ => null,
            };

            // Duration is the one primitive whose wire form is ambiguous: the codec parses ISO-8601 ("PT5S")
            // only (XmlConvert.ToTimeSpan), but the web UI and .NET callers submit the .NET ToString form
            // ("00:00:05"). Accept both here, otherwise every TimeSpan property is unwritable from the UI
            // (FormatException → HTTP 500). The UI's read side is already tolerant of both forms.
            var underlyingClr = Nullable.GetUnderlyingType(clrType) ?? clrType;
            if (underlyingClr == typeof(TimeSpan) && json is JsonValue durationValue && durationValue.TryGetValue<string>(out var durationText) &&
                TryParseDuration(durationText, out var duration))
            {
                return duration;
            }

            return PropertyValueCodec.JsonToClr(json, typeRef, clrType);
        }

        // Parse a Duration from either the ISO-8601 form ("PT5S", the codec/MQTT canonical) or the .NET
        // TimeSpan ToString form ("00:00:05", what the web UI renders and submits).
        private static bool TryParseDuration(string text, out TimeSpan value)
        {
            try
            {
                value = XmlConvert.ToTimeSpan(text);
                return true;
            }
            catch (FormatException)
            {
                return TimeSpan.TryParse(text, CultureInfo.InvariantCulture, out value);
            }
        }

        private DevLogicBlockConfig? ResolveLogicBlock(string logicBlockIdOrName)
        {
            return _configuration.LogicBlocks.FirstOrDefault(b => b.Name == logicBlockIdOrName) ?? _configuration.LogicBlocks.FirstOrDefault(b => b.Id == logicBlockIdOrName);
        }

        private string LogicBlockNameFor(string serviceId)
        {
            // Built lazily and cached once the config's Services are populated (after introspection), since
            // this control instance can be constructed before that happens in a headless boot.
            var map = _serviceToLogicBlock;
            if (map is null || map.Count == 0)
            {
                map = new Dictionary<string, string>();
                foreach (var logicBlock in _configuration.LogicBlocks)
                {
                    foreach (var service in logicBlock.Services)
                    {
                        map[service.Id] = logicBlock.Name;
                    }
                }

                if (map.Count > 0)
                {
                    _serviceToLogicBlock = map;
                }
            }

            return map.TryGetValue(serviceId, out var name) ? name : serviceId;
        }

        private void OnServiceProperty(object? sender, ServicePropertyChangedEventArgs e)
        {
            _values[(e.ServiceIdentifier, e.PropertyIdentifier)] = e.Value;
            Publish(new ServicePropertyChanged(LogicBlockNameFor(e.ServiceIdentifier), e.ServiceIdentifier, e.PropertyIdentifier, e.Value));
        }

        private void OnWriteAcknowledged(object? sender, ServicePropertyWriteAcknowledgedEventArgs e)
        {
            // The applied value as read back from the block — usually a no-op (the change event already
            // updated the cache, FIFO-ordered before this), but it also covers writes whose value never
            // publishes a change (no-op sets against a not-yet-published member).
            _values[(e.ServiceIdentifier, e.PropertyIdentifier)] = e.Value;
            Publish(new ServicePropertyWriteAcknowledged(LogicBlockNameFor(e.ServiceIdentifier), e.ServiceIdentifier, e.PropertyIdentifier, e.Value));
        }

        private void OnMeasuringPoint(object? sender, ServiceMeasuringPointChangedEventArgs e)
        {
            _values[(e.ServiceIdentifier, e.MeasuringPointIdentifier)] = e.Value;
            Publish(new ServiceMeasuringPointChanged(LogicBlockNameFor(e.ServiceIdentifier), e.ServiceIdentifier, e.MeasuringPointIdentifier, e.Value));
        }

        private void OnDigitalInput(object? sender, DigitalInputChangedEventArgs e)
        {
            Publish(new DigitalInputChanged(e.ServiceProviderIdentifier, e.ServiceIdentifier, e.ContractIdentifier, e.Value));
        }

        private void OnDigitalOutput(object? sender, DigitalOutputChangedEventArgs e)
        {
            _digitalOutputs[(e.ServiceProviderIdentifier, e.ServiceIdentifier, e.ContractIdentifier)] = e.Value;
            Publish(new DigitalOutputChanged(e.ServiceProviderIdentifier, e.ServiceIdentifier, e.ContractIdentifier, e.Value));
        }

        private void OnAnalogInput(object? sender, AnalogInputChangedEventArgs e)
        {
            Publish(new AnalogInputChanged(e.ServiceProviderIdentifier, e.ServiceIdentifier, e.ContractIdentifier, e.Value));
        }

        private void OnAnalogOutput(object? sender, AnalogOutputChangedEventArgs e)
        {
            _analogOutputs[(e.ServiceProviderIdentifier, e.ServiceIdentifier, e.ContractIdentifier)] = e.Value;
            Publish(new AnalogOutputChanged(e.ServiceProviderIdentifier, e.ServiceIdentifier, e.ContractIdentifier, e.Value));
        }

        private void Publish(DevHostEvent e)
        {
            Action<DevHostEvent>[] subscribers;
            Func<DevHostEvent, bool>[] waiters;
            lock (_gate)
            {
                subscribers = _subscribers.ToArray();
                waiters = _waiters.ToArray();
            }

            foreach (var subscriber in subscribers)
            {
                try
                {
                    subscriber(e);
                }
                catch
                {
                    // A faulty subscriber must not break the event fan-out.
                }
            }

            foreach (var waiter in waiters)
            {
                try
                {
                    waiter(e);
                }
                catch
                {
                    // Selector exceptions are swallowed; the waiter simply doesn't complete on this event.
                }
            }
        }

        private sealed class Token : IDisposable
        {
            private Action? _dispose;

            public Token(Action dispose)
            {
                _dispose = dispose;
            }

            public void Dispose()
            {
                _dispose?.Invoke();
                _dispose = null;
            }
        }
    }
}