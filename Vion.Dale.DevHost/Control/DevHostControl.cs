using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Control
{
    /// <inheritdoc />
    internal sealed class DevHostControl : IDevHostControl, IDisposable
    {
        private readonly DevConfiguration _configuration;

        private readonly DevHostEvents _events;

        private readonly DevHostLogSink _logSink;

        // Event-stream fan-out + pending WaitForAsync waiters share one lock — both touched from actor threads.
        private readonly object _gate = new();

        private readonly List<Action<DevHostEvent>> _subscribers = new();

        private readonly List<Func<DevHostEvent, bool>> _waiters = new();

        // service-id (GUID carried on the events) → block name assigned in DevConfigurationBuilder.
        private readonly Dictionary<string, string> _serviceToBlock;

        public DevHostControl(DevConfiguration configuration, DevHostEvents events, DevHostLogSink logSink)
        {
            _configuration = configuration;
            _events = events;
            _logSink = logSink;

            _serviceToBlock = new Dictionary<string, string>();
            foreach (var block in configuration.LogicBlocks)
            {
                foreach (var service in block.Services)
                {
                    _serviceToBlock[service.Id] = block.Name;
                }
            }

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
            return _serviceToBlock.TryGetValue(serviceId, out var name) ? name : serviceId;
        }

        private void OnServiceProperty(object? sender, ServicePropertyChangedEventArgs e)
        {
            Publish(new ServicePropertyChanged(BlockFor(e.ServiceIdentifier), e.ServiceIdentifier, e.PropertyIdentifier, e.Value));
        }

        private void OnMeasuringPoint(object? sender, ServiceMeasuringPointChangedEventArgs e)
        {
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
