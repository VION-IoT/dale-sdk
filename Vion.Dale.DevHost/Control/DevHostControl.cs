using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
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
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Control
{
    /// <inheritdoc />
    internal sealed class DevHostControl : IDevHostControl, IDisposable
    {
        private readonly IActorSystem _actorSystem;

        private readonly DevConfiguration _configuration;

        private readonly DevHostEvents _events;

        // Event-stream fan-out + pending WaitForAsync waiters share one lock — both touched from actor threads.
        private readonly object _gate = new();

        private readonly DevHostIntrospection _introspection;

        private readonly DevHostLogSink _logSink;

        private readonly MessageTap _messageTap;

        private readonly DevHostRunControl _runControl;

        private readonly List<Action<DevHostEvent>> _subscribers = new();

        // Last-known value per (serviceConfigId, memberName) — fed by the change events, read by GetProperty.
        private readonly ConcurrentDictionary<(string ServiceId, string Member), object?> _values = new();

        private readonly List<Func<DevHostEvent, bool>> _waiters = new();

        // service-id (GUID carried on the events) → logic block name. Built lazily: in a headless boot the
        // config's Services aren't populated until DevHostIntrospection runs, which may be after construction.
        private Dictionary<string, string>? _serviceToLogicBlock;

        public DevHostControl(DevConfiguration configuration,
                              DevHostEvents events,
                              DevHostLogSink logSink,
                              DevHostIntrospection introspection,
                              IActorSystem actorSystem,
                              MessageTap messageTap,
                              DevHostRunControl runControl)
        {
            _configuration = configuration;
            _events = events;
            _logSink = logSink;
            _introspection = introspection;
            _actorSystem = actorSystem;
            _messageTap = messageTap;
            _runControl = runControl;

            _events.ServicePropertyChanged += OnServiceProperty;
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
        public bool TryRequestReset()
        {
            return _runControl.TryRequestReset();
        }

        /// <inheritdoc />
        public IDisposable OnResetRequested(Action handler)
        {
            return _runControl.OnResetRequested(handler);
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
                             throw new InvalidOperationException($"Unknown service id '{serviceId}'.");

            // Decode JSON values (the HTTP path delivers JsonElement) into the precise CLR type the block
            // expects; CLR values from in-process callers pass through unchanged.
            var typedValue = NormalizeValue(serviceId, propertyName, value);

            var logicBlockActor = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(logicBlock.Name, logicBlock.Id));
            var handler = _actorSystem.LookupByName(nameof(MockServicePropertyHandler));

            // The actor applies the set and re-publishes the value asynchronously; the SendTo below is
            // fire-and-forget. Await that publish so a read-after-write — `await SetPropertyAsync(...)` then
            // `GetProperty(...)` — reflects the new value instead of racing the actor and returning the stale
            // one. Register the waiter BEFORE sending: WaitForAsync only observes events raised after the call.
            // A set that doesn't change the value raises no event, so the wait falls back to its timeout rather
            // than hanging (GetProperty already returns the correct, unchanged value in that case).
            var applied = WaitForAsync(e => e is ServicePropertyChanged sp && sp.ServiceId == serviceId && sp.Property == propertyName ? (object)sp : null,
                                       TimeSpan.FromSeconds(5));

            _actorSystem.SendTo(handler,
                                new MockSetServicePropertyValue(logicBlockActor, new SetServicePropertyValueRequest(new ServiceIdentifier(serviceId), propertyName, typedValue!)));

            await applied.ConfigureAwait(false);
        }

        public Task SetDigitalInputAsync(string serviceProviderId, string serviceId, string contractId, bool value)
        {
            var handler = _actorSystem.LookupByName(nameof(DigitalInputHandler));
            _actorSystem.SendTo(handler, new MockSetDigitalInputMessage(serviceProviderId, serviceId, contractId, value));
            return Task.CompletedTask;
        }

        public Task SetAnalogInputAsync(string serviceProviderId, string serviceId, string contractId, double value)
        {
            var handler = _actorSystem.LookupByName(nameof(AnalogInputHandler));
            _actorSystem.SendTo(handler, new MockSetAnalogInputMessage(serviceProviderId, serviceId, contractId, value));
            return Task.CompletedTask;
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
            _events.ServiceMeasuringPointChanged -= OnMeasuringPoint;
            _events.DigitalInputChanged -= OnDigitalInput;
            _events.DigitalOutputChanged -= OnDigitalOutput;
            _events.AnalogInputChanged -= OnAnalogInput;
            _events.AnalogOutputChanged -= OnAnalogOutput;
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
            Publish(new DigitalOutputChanged(e.ServiceProviderIdentifier, e.ServiceIdentifier, e.ContractIdentifier, e.Value));
        }

        private void OnAnalogInput(object? sender, AnalogInputChangedEventArgs e)
        {
            Publish(new AnalogInputChanged(e.ServiceProviderIdentifier, e.ServiceIdentifier, e.ContractIdentifier, e.Value));
        }

        private void OnAnalogOutput(object? sender, AnalogOutputChangedEventArgs e)
        {
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