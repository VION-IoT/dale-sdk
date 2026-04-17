using System;
using System.Threading.Tasks;
using Vion.Dale.DevHost.Web.Api.Hubs;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;

namespace Vion.Dale.DevHost.Web.Services
{
    public class DevHostEventBroadcaster
    {
        private readonly IDevHostEvents _devHostEvents;

        private readonly IHubContext<DevHostHub> _hubContext;

        private readonly ILogger<DevHostEventBroadcaster> _logger;

        public DevHostEventBroadcaster(DevHostEvents devHostEvents, IHubContext<DevHostHub> hubContext, ILogger<DevHostEventBroadcaster> logger)
        {
            _devHostEvents = devHostEvents;
            _hubContext = hubContext;
            _logger = logger;

            _devHostEvents.ServicePropertyChanged += OnServicePropertyChanged;
            _devHostEvents.ServiceMeasuringPointChanged += OnServiceMeasuringPointChanged;
            _devHostEvents.DigitalInputChanged += OnDigitalInputChanged;
            _devHostEvents.DigitalOutputChanged += OnDigitalOutputChanged;
            _devHostEvents.AnalogInputChanged += OnAnalogInputChanged;
            _devHostEvents.AnalogOutputChanged += OnAnalogOutputChanged;
        }

        private void OnServicePropertyChanged(object? sender, ServicePropertyChangedEventArgs e)
        {
            _ = BroadcastAsync("PropertyValueChanged",
                               new
                               {
                                   serviceIdentifier = e.ServiceIdentifier,
                                   propertyIdentifier = e.PropertyIdentifier,
                                   value = e.Value,
                               });
        }

        private void OnServiceMeasuringPointChanged(object? sender, ServiceMeasuringPointChangedEventArgs e)
        {
            _ = BroadcastAsync("MeasuringPointValueChanged",
                               new
                               {
                                   serviceIdentifier = e.ServiceIdentifier,
                                   measuringPointIdentifier = e.MeasuringPointIdentifier,
                                   value = e.Value,
                               });
        }

        private void OnDigitalInputChanged(object? sender, DigitalInputChangedEventArgs e)
        {
            _ = BroadcastAsync("DigitalInputChanged",
                               new
                               {
                                   serviceProviderIdentifier = e.ServiceProviderIdentifier,
                                   serviceIdentifier = e.ServiceIdentifier,
                                   contractIdentifier = e.ContractIdentifier,
                                   value = e.Value,
                               });
        }

        private void OnDigitalOutputChanged(object? sender, DigitalOutputChangedEventArgs e)
        {
            _ = BroadcastAsync("DigitalOutputChanged",
                               new
                               {
                                   serviceProviderIdentifier = e.ServiceProviderIdentifier,
                                   serviceIdentifier = e.ServiceIdentifier,
                                   contractIdentifier = e.ContractIdentifier,
                                   value = e.Value,
                               });
        }

        private void OnAnalogInputChanged(object? sender, AnalogInputChangedEventArgs e)
        {
            _ = BroadcastAsync("AnalogInputChanged",
                               new
                               {
                                   serviceProviderIdentifier = e.ServiceProviderIdentifier,
                                   serviceIdentifier = e.ServiceIdentifier,
                                   contractIdentifier = e.ContractIdentifier,
                                   value = e.Value,
                               });
        }

        private void OnAnalogOutputChanged(object? sender, AnalogOutputChangedEventArgs e)
        {
            _ = BroadcastAsync("AnalogOutputChanged",
                               new
                               {
                                   serviceProviderIdentifier = e.ServiceProviderIdentifier,
                                   serviceIdentifier = e.ServiceIdentifier,
                                   contractIdentifier = e.ContractIdentifier,
                                   value = e.Value,
                               });
        }

        private async Task BroadcastAsync(string eventName, object payload)
        {
            try
            {
                await _hubContext.Clients.All.SendAsync(eventName, payload);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast {EventName} event. Payload: {Payload}", eventName, payload);
            }
        }
    }
}