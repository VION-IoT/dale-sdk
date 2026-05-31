using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Mocking;
using Vion.Dale.Sdk.Abstractions;
using Vion.Dale.Sdk.DigitalIo.Input;
using Vion.Dale.Sdk.AnalogIo.Input;
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

        // Event-stream fan-out + pending WaitForAsync waiters share one lock — both touched from actor threads.
        private readonly object _gate = new();

        private readonly List<Action<DevHostEvent>> _subscribers = new();

        private readonly List<Func<DevHostEvent, bool>> _waiters = new();

        // Last-known value per (serviceConfigId, memberName) — fed by the change events, read by GetProperty.
        private readonly ConcurrentDictionary<(string ServiceId, string Member), object?> _values = new();

        // service-id (GUID carried on the events) → block name. Built lazily: in a headless boot the config's
        // Services aren't populated until DevHostIntrospection runs, which may be after this is constructed.
        private Dictionary<string, string>? _serviceToBlock;

        public DevHostControl(DevConfiguration configuration, DevHostEvents events, DevHostLogSink logSink, DevHostIntrospection introspection, IActorSystem actorSystem)
        {
            _configuration = configuration;
            _events = events;
            _logSink = logSink;
            _introspection = introspection;
            _actorSystem = actorSystem;

            _events.ServicePropertyChanged += OnServiceProperty;
            _events.ServiceMeasuringPointChanged += OnMeasuringPoint;
            _events.DigitalInputChanged += OnDigitalInput;
            _events.DigitalOutputChanged += OnDigitalOutput;
            _events.AnalogInputChanged += OnAnalogInput;
            _events.AnalogOutputChanged += OnAnalogOutput;
        }

        public IReadOnlyList<BlockInfo> ListBlocks()
        {
            return _configuration.LogicBlocks
                                 .Select(b => new BlockInfo(b.Id, b.Name, b.LogicBlockType.Name, b.Services.Select(s => s.Id).ToList()))
                                 .ToList();
        }

        public object? GetProperty(string blockIdOrName, string propertyName)
        {
            var block = ResolveBlock(blockIdOrName);
            if (block is null || !_introspection.TryGetServiceId(block.Id, propertyName, out var serviceId))
            {
                return null;
            }

            return _values.TryGetValue((serviceId, propertyName), out var value) ? value : null;
        }

        public IReadOnlyDictionary<string, object?> GetAllProperties(string blockIdOrName)
        {
            var result = new Dictionary<string, object?>();
            var block = ResolveBlock(blockIdOrName);
            if (block is null)
            {
                return result;
            }

            foreach (var propertyName in _introspection.PropertyNames(block.Id))
            {
                result[propertyName] = GetProperty(block.Id, propertyName);
            }

            return result;
        }

        public Task SetPropertyAsync(string blockIdOrName, string propertyName, object value)
        {
            var block = ResolveBlock(blockIdOrName)
                        ?? throw new InvalidOperationException($"Unknown block '{blockIdOrName}'.");

            if (!_introspection.TryGetServiceId(block.Id, propertyName, out var serviceId))
            {
                throw new InvalidOperationException(
                    $"Block '{block.Name}' has no service property or measuring point named '{propertyName}'. " +
                    "(Note: get/set on the in-process control surface requires a headless boot; the property metadata " +
                    "is introspected at StartAsync.)");
            }

            var blockActor = _actorSystem.LookupByName(LogicBlockUtils.CreateLogicBlockName(block.Name, block.Id));
            var handler = _actorSystem.LookupByName(nameof(MockServicePropertyHandler));
            _actorSystem.SendTo(handler, new MockSetServicePropertyValue(blockActor, new SetServicePropertyValueRequest(new ServiceIdentifier(serviceId), propertyName, value)));

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

        private DevLogicBlockConfig? ResolveBlock(string blockIdOrName)
        {
            return _configuration.LogicBlocks.FirstOrDefault(b => b.Name == blockIdOrName)
                   ?? _configuration.LogicBlocks.FirstOrDefault(b => b.Id == blockIdOrName);
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

        public void Dispose()
        {
            _events.ServicePropertyChanged -= OnServiceProperty;
            _events.ServiceMeasuringPointChanged -= OnMeasuringPoint;
            _events.DigitalInputChanged -= OnDigitalInput;
            _events.DigitalOutputChanged -= OnDigitalOutput;
            _events.AnalogInputChanged -= OnAnalogInput;
            _events.AnalogOutputChanged -= OnAnalogOutput;
        }

        private string BlockFor(string serviceId)
        {
            // Built lazily and cached once the config's Services are populated (after introspection), since
            // this control instance can be constructed before that happens in a headless boot.
            var map = _serviceToBlock;
            if (map is null || map.Count == 0)
            {
                map = new Dictionary<string, string>();
                foreach (var block in _configuration.LogicBlocks)
                {
                    foreach (var service in block.Services)
                    {
                        map[service.Id] = block.Name;
                    }
                }

                if (map.Count > 0)
                {
                    _serviceToBlock = map;
                }
            }

            return map.TryGetValue(serviceId, out var name) ? name : serviceId;
        }

        private void OnServiceProperty(object? sender, ServicePropertyChangedEventArgs e)
        {
            _values[(e.ServiceIdentifier, e.PropertyIdentifier)] = e.Value;
            Publish(new ServicePropertyChanged(BlockFor(e.ServiceIdentifier), e.ServiceIdentifier, e.PropertyIdentifier, e.Value));
        }

        private void OnMeasuringPoint(object? sender, ServiceMeasuringPointChangedEventArgs e)
        {
            _values[(e.ServiceIdentifier, e.MeasuringPointIdentifier)] = e.Value;
            Publish(new ServiceMeasuringPointChanged(BlockFor(e.ServiceIdentifier), e.ServiceIdentifier, e.MeasuringPointIdentifier, e.Value));
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
