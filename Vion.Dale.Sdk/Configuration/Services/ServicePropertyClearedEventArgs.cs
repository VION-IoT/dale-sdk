using System;

namespace Vion.Dale.Sdk.Configuration.Services
{
    public class ServicePropertyClearedEventArgs : EventArgs
    {
        public string ServiceIdentifier { get; }

        public string PropertyIdentifier { get; }

        public ServicePropertyClearedEventArgs(string serviceIdentifier, string propertyIdentifier)
        {
            ServiceIdentifier = serviceIdentifier;
            PropertyIdentifier = propertyIdentifier;
        }
    }
}