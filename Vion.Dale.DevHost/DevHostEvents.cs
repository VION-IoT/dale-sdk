using System;
using System.Text.Json;

namespace Vion.Dale.DevHost
{
    /// <summary>
    ///     Singleton event aggregator for DevHost events
    /// </summary>
    public class DevHostEvents : IDevHostEvents
    {
        public event EventHandler<ServicePropertyChangedEventArgs>? ServicePropertyChanged;

        public event EventHandler<ServiceMeasuringPointChangedEventArgs>? ServiceMeasuringPointChanged;

        public event EventHandler<ServiceProviderContractChangedEventArgs>? ServiceProviderContractChanged;

        /// <summary>
        ///     A service-property write completed its round trip: the block applied the value and replied.
        ///     Deliberately on the concrete aggregator only (not <see cref="IDevHostEvents" />) — it is the
        ///     control surface's write-ack correlation signal, not part of the general observation contract.
        /// </summary>
        public event EventHandler<ServicePropertyWriteAcknowledgedEventArgs>? ServicePropertyWriteAcknowledged;

        public void RaiseServicePropertyWriteAcknowledged(string serviceIdentifier, string propertyIdentifier, object? value)
        {
            ServicePropertyWriteAcknowledged?.Invoke(this, new ServicePropertyWriteAcknowledgedEventArgs(serviceIdentifier, propertyIdentifier, value));
        }

        public void RaiseServicePropertyChanged(string serviceIdentifier, string propertyIdentifier, object? value)
        {
            ServicePropertyChanged?.Invoke(this, new ServicePropertyChangedEventArgs(serviceIdentifier, propertyIdentifier, value));
        }

        public void RaiseServiceMeasuringPointChanged(string serviceIdentifier, string measuringPointIdentifier, object? value)
        {
            ServiceMeasuringPointChanged?.Invoke(this, new ServiceMeasuringPointChangedEventArgs(serviceIdentifier, measuringPointIdentifier, value));
        }

        public void RaiseServiceProviderContractChanged(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, JsonElement value)
        {
            ServiceProviderContractChanged?.Invoke(this, new ServiceProviderContractChangedEventArgs(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, value));
        }
    }

    /// <summary>
    ///     Args for <see cref="DevHostEvents.ServicePropertyWriteAcknowledged" /> — the applied value as read back from
    ///     the block.
    /// </summary>
    public class ServicePropertyWriteAcknowledgedEventArgs : EventArgs
    {
        public string ServiceIdentifier { get; }

        public string PropertyIdentifier { get; }

        public object? Value { get; }

        public ServicePropertyWriteAcknowledgedEventArgs(string serviceIdentifier, string propertyIdentifier, object? value)
        {
            ServiceIdentifier = serviceIdentifier;
            PropertyIdentifier = propertyIdentifier;
            Value = value;
        }
    }
}