using System;

namespace Vion.Dale.DevHost
{
    /// <summary>
    ///     Singleton event aggregator for DevHost events
    /// </summary>
    public class DevHostEvents : IDevHostEvents
    {
        public event EventHandler<ServicePropertyChangedEventArgs>? ServicePropertyChanged;

        public event EventHandler<ServiceMeasuringPointChangedEventArgs>? ServiceMeasuringPointChanged;

        public event EventHandler<DigitalInputChangedEventArgs>? DigitalInputChanged;

        public event EventHandler<DigitalOutputChangedEventArgs>? DigitalOutputChanged;

        public event EventHandler<AnalogInputChangedEventArgs>? AnalogInputChanged;

        public event EventHandler<AnalogOutputChangedEventArgs>? AnalogOutputChanged;

        public void RaiseServicePropertyChanged(string serviceIdentifier, string propertyIdentifier, object? value)
        {
            ServicePropertyChanged?.Invoke(this, new ServicePropertyChangedEventArgs(serviceIdentifier, propertyIdentifier, value));
        }

        public void RaiseServiceMeasuringPointChanged(string serviceIdentifier, string measuringPointIdentifier, object? value)
        {
            ServiceMeasuringPointChanged?.Invoke(this, new ServiceMeasuringPointChangedEventArgs(serviceIdentifier, measuringPointIdentifier, value));
        }

        public void RaiseDigitalInputChanged(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, bool value)
        {
            DigitalInputChanged?.Invoke(this, new DigitalInputChangedEventArgs(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, value));
        }

        public void RaiseDigitalOutputChanged(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, bool value)
        {
            DigitalOutputChanged?.Invoke(this, new DigitalOutputChangedEventArgs(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, value));
        }

        public void RaiseAnalogInputChanged(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, double value)
        {
            AnalogInputChanged?.Invoke(this, new AnalogInputChangedEventArgs(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, value));
        }

        public void RaiseAnalogOutputChanged(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, double value)
        {
            AnalogOutputChanged?.Invoke(this, new AnalogOutputChangedEventArgs(serviceProviderIdentifier, serviceIdentifier, contractIdentifier, value));
        }
    }
}
