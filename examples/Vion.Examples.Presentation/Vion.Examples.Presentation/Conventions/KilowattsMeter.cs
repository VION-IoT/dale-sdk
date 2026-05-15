using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>Example preset: instantaneous power measurement in kilowatts. Non-negative.</summary>
    public class KilowattsMeter : ServiceMeasuringPointAttribute
    {
        public KilowattsMeter()
        {
            Unit = "kW";
            Minimum = 0;
            Kind = MeasuringPointKind.Measurement;
        }
    }
}
