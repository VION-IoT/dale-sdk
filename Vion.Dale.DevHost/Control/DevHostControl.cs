using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json.Nodes;
using System.Threading;
using System.Threading.Tasks;
using Vion.Contracts.Codec;
using Vion.Contracts.TypeRef;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.DigitalIo.Output;
using Vion.Dale.Sdk.AnalogIo.Input;
using Vion.Dale.Sdk.AnalogIo.Output;
using Vion.Dale.Sdk.Messages;
using Vion.Dale.Sdk.Utils;

namespace Vion.Dale.DevHost.Control
{
    /// <inheritdoc />
    internal sealed class DevHostControl : IDevHostControl, IDisposable
    {
        private readonly DevConfiguration _configuration;

        private readonly DevHostEvents _events;

        private readonly DevHostLogSink _logSink;

        private readonly DevHostIntrospection _introspection;

        private readonly IActorSystem _actorSystem;

        private readonly MessageTap _messageTap;

        // Event-stream fan-out + pending WaitForAsync waiters share one lock — both touched from actor threads.
        private readonly object _gate = new();

        private readonly List<Action<DevHostEvent>> _subscribers = new();

        private readonly List<Func<DevHostEvent, bool>> _waiters = new();

        // Last-known value per (serviceConfigId, memberName) — fed by the change events, read by GetProperty.
        private readonly ConcurrentDictionary<(string ServiceId, string Member), object?> _values = new();

        // service-id (GUID carried on the events) → logic block name. Built lazily: in a headless boot the
        // config's Services aren't populated until DevHostIntrospection runs, which may be after construction.
        private Dictionary<string, string>? _serviceToLogicBlock;

        public DevHostControl(DevConfiguration configuration,
                              DevHostEvents events,
                              DevHostLogSink logSink,
                              DevHostIntrospection introspection,
                              IActorSystem actorSystem,
                              MessageTap messageTap)
        {
            _configuration = configuration;
            _events = events;
            _logSink = logSink;
            _introspection = introspection;
            _actorSystem = actorSystem;
            _messageTap = messageTap;

            _events.ServicePropertyChanged += OnServiceProperty;
            _events.ServiceMeasuringPointChanged += OnMeasuringPoint;
            _events.DigitalInputChanged += OnDigitalInput;
            _events.DigitalOutputChanged += OnDigitalOutput;
            _events.AnalogInputChanged += OnAnalogInput;
            _events.AnalogOutputChanged += OnAnalogOutput;
        }

        public IReadOnlyList<LogicBlockInfo> ListLogicBlocks()
        {
            return _configuration.LogicBlocks
                                 .Select(b => new LogicBlockInfo(b.Id, b.Name, b.LogicBlockType.Name, b.Services.Select(s => s.Id).ToList()))
                                 .ToList();
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

        public Task SetPropertyAsync(string logicBlockIdOrName, string propertyName, object value)
        {
            var logicBlock = ResolveLogicBlock(logicBlockIdOrName)
                             ?? throw new InvalidOperationException($"Unknown logic block '{logicBlockIdOrName}'.");

            if (!_introspection.TryGetServiceId(logicBlock.Id, propertyName, out var serviceId))
            {
                throw new InvalidOperationException(
                    $"Logic block '{logicBlock.Name}' has no service property or measuring point named '{propertyName}'.");
            }

            return SetServicePropertyValueAsync(serviceId, propertyName, value);
        }

        public Task SetServicePropertyValueAsync(string serviceId, string propertyName, object value)
        {
            var logicBlock = _configuration.LogicBlocks.FirstOrDefault(lb => lb.Services.Any(s => s.Id == serviceId))
                             ?? throw new InvalidOperationException($"Unknown service id '{serviceId}'.");

            // Decode JSON values (the HTTP path delivers JsonElement) into the precise CLR type the block
            // expects; CLR values from in-process callers pass through unchanged.
            var typedValue = NormalizeValue(serviceId, propertyName, value);

            var logicBlockActor = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(logicBlock.Name, logicBlock.Id));
            var handler = _actorSystem.LookupByName(nameof(MockServicePropertyHandler));
            _actorSystem.SendTo(handler, new MockSetServicePropertyValue(logicBlockActor, new SetServicePropertyValueRequest(new ServiceIdentifier(serviceId), propertyName, typedValue!)));

            return Task.CompletedTask;
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

        // JSON → typed CLR for the HTTP set path (moved here when IDevHostStateProvider was collapsed into the
        // control surface). Re-parses the property's JSON Schema into a TypeRef and delegates to the same
        // PropertyValueCodec the runtime uses. CLR values pass through untouched.
        private object? NormalizeValue(string serviceId, string propertyName, object value)
        {
            var isJson = value is System.Text.Json.JsonElement || value is JsonNode;
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
            JsonNode? json = value switch
            {
                System.Text.Json.JsonElement je when je.ValueKind == System.Text.Json.JsonValueKind.Null => null,
                System.Text.Json.JsonElement je => JsonNode.Parse(je.GetRawText()),
                JsonNode node => JsonNode.Parse(node.ToJsonString()),
                _ => null,
            };

            return PropertyValueCodec.JsonToClr(json, typeRef, clrType);
        }

        private DevLogicBlockConfig? ResolveLogicBlock(string logicBlockIdOrName)
        {
            return _configuration.LogicBlocks.FirstOrDefault(b => b.Name == logicBlockIdOrName)
                   ?? _configuration.LogicBlocks.FirstOrDefault(b => b.Id == logicBlockIdOrName);
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

        public async Task<T?> WaitForAsync<T>(Func<DevHostEvent, T?> selector, TimeSpan timeout)
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
