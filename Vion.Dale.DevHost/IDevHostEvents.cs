using System;
using System.Text.Json;

namespace Vion.Dale.DevHost
{
    /// <summary>
    ///     Events raised by the DevHost that external subscribers (like the Web UI) can listen to.
    /// </summary>
    public interface IDevHostEvents
    {
        event EventHandler<ServicePropertyChangedEventArgs>? ServicePropertyChanged;

        event EventHandler<ServiceMeasuringPointChangedEventArgs>? ServiceMeasuringPointChanged;

        /// <summary>
        ///     A service-provider value contract's current value changed — an input was driven or an output was
        ///     written. Generic over every <c>[ServiceProviderContractType]</c> value contract (the four HAL
        ///     families and third-party struct contracts alike); the value is the contract's wire JSON, and the
        ///     subscriber (the SPA wiring panel) renders it per the contract's own type. Replaces the former
        ///     digital/analog input/output-specific events (RFC 0010).
        /// </summary>
        event EventHandler<ServiceProviderContractChangedEventArgs>? ServiceProviderContractChanged;
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

    /// <summary>
    ///     The current value of a service-provider value contract (its wire JSON), keyed by the mocked endpoint's
    ///     service-provider / service / contract identifiers. Direction-agnostic — the consumer knows whether the
    ///     contract is an input or output from the configuration.
    /// </summary>
    public class ServiceProviderContractChangedEventArgs : EventArgs
    {
        public string ServiceProviderIdentifier { get; }

        public string ServiceIdentifier { get; }

        public string ContractIdentifier { get; }

        public JsonElement Value { get; }

        public ServiceProviderContractChangedEventArgs(string serviceProviderIdentifier, string serviceIdentifier, string contractIdentifier, JsonElement value)
        {
            ServiceProviderIdentifier = serviceProviderIdentifier;
            ServiceIdentifier = serviceIdentifier;
            ContractIdentifier = contractIdentifier;
            Value = value;
        }
    }
}