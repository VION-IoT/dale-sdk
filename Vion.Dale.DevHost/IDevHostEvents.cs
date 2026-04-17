using System;

namespace Vion.Dale.DevHost
{
    /// <summary>
    ///     Events raised by the DevHost that external subscribers (like Web UI) can listen to
    /// </summary>
    public interface IDevHostEvents
    {
        event EventHandler<ServicePropertyChangedEventArgs>? ServicePropertyChanged;

        event EventHandler<ServiceMeasuringPointChangedEventArgs>? ServiceMeasuringPointChanged;

        event EventHandler<DigitalInputChangedEventArgs>? DigitalInputChanged;

        event EventHandler<DigitalOutputChangedEventArgs>? DigitalOutputChanged;

        event EventHandler<AnalogInputChangedEventArgs>? AnalogInputChanged;

        event EventHandler<AnalogOutputChangedEventArgs>? AnalogOutputChanged;
    }

    public class ServicePropertyChangedEventArgs : EventArgs
    {
        public string ServiceIdentifier { get; }

        public string PropertyIdentifier { get; }

        public object? Value { get; }

        public ServicePropertyChangedEventArgs(string serviceIdentifier, string propertyIdentifier, object? value)
        {
            ServiceIdentifier = serviceIdentifier;
            PropertyIdentifier = propertyIdentifier;
            Value = value;
        }
    }

    public class ServiceMeasuringPointChangedEventArgs : EventArgs
    {
        public string ServiceIdentifier { get; }

        public string MeasuringPointIdentifier { get; }

        public object? Value { get; }

        public ServiceMeasuringPointChangedEventArgs(string serviceIdentifier, string measuringPointIdentifier, object? value)
        {
            ServiceIdentifier = serviceIdentifier;
            MeasuringPointIdentifier = measuringPointIdentifier;
            Value = value;
        }
    }

    public class DigitalInputChangedEventArgs : EventArgs
    {
        public string ServiceProviderIdentifier { get; }

        public string ServiceIdentifier { get; }

        public string ContractIdentifier { get; }

        public bool Value { get; }

        public DigitalInputChangedEventArgs(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, bool value)
        {
            ServiceProviderIdentifier = serviceProviderIdentifier;
            ServiceIdentifier = serviceIdentifier;
            ContractIdentifier = contractIdentifier;
            Value = value;
        }
    }

    public class DigitalOutputChangedEventArgs : EventArgs
    {
        public string ServiceProviderIdentifier { get; }

        public string ServiceIdentifier { get; }

        public string ContractIdentifier { get; }

        public bool Value { get; }

        public DigitalOutputChangedEventArgs(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, bool value)
        {
            ServiceProviderIdentifier = serviceProviderIdentifier;
            ServiceIdentifier = serviceIdentifier;
            ContractIdentifier = contractIdentifier;
            Value = value;
        }
    }

    public class AnalogInputChangedEventArgs : EventArgs
    {
        public string ServiceProviderIdentifier { get; }

        public string ServiceIdentifier { get; }

        public string ContractIdentifier { get; }

        public double Value { get; }

        public AnalogInputChangedEventArgs(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, double value)
        {
            ServiceProviderIdentifier = serviceProviderIdentifier;
            ServiceIdentifier = serviceIdentifier;
            ContractIdentifier = contractIdentifier;
            Value = value;
        }
    }

    public class AnalogOutputChangedEventArgs : EventArgs
    {
        public string ServiceProviderIdentifier { get; }

        public string ServiceIdentifier { get; }

        public string ContractIdentifier { get; }

        public double Value { get; }

        public AnalogOutputChangedEventArgs(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, double value)
        {
            ServiceProviderIdentifier = serviceProviderIdentifier;
            ServiceIdentifier = serviceIdentifier;
            ContractIdentifier = contractIdentifier;
            Value = value;
        }
    }
}