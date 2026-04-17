using System;

namespace Vion.Dale.Sdk.Configuration.Services
{
    public class ServiceMeasuringPointClearedEventArgs : EventArgs
    {
        public string ServiceIdentifier { get; }

        public string MeasuringPointIdentifier { get; }

        public ServiceMeasuringPointClearedEventArgs(string serviceIdentifier, string measuringPointIdentifier)
        {
            ServiceIdentifier = serviceIdentifier;
            MeasuringPointIdentifier = measuringPointIdentifier;
        }
    }
}