using System;

namespace Vion.Dale.Sdk.Configuration.Services
{
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
}