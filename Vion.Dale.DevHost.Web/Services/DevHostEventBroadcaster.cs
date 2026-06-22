using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using Vion.Dale.DevHost.Web.Api.Hubs;

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
            _devHostEvents.ServiceProviderContractChanged += OnServiceProviderContractChanged;
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

        private void OnServiceProviderContractChanged(object? sender, ServiceProviderContractChangedEventArgs e)
        {
            // One event for every value contract — the SPA keys by (sp, svc, contract) and renders the JSON
            // value per the contract's own type. JsonElement serializes to its underlying JSON over the wire.
            _ = BroadcastAsync("ServiceProviderContractChanged",
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