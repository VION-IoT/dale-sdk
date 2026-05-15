using System;

namespace Vion.Dale.Sdk.Configuration.Services
{
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
}