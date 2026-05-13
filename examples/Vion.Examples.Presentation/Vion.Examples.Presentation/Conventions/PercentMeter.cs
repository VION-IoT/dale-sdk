using Vion.Contracts.TypeRef;
using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>Example preset: instantaneous percentage measurement from 0 to 100.</summary>
    public class PercentMeter : ServiceMeasuringPointAttribute
    {
        public PercentMeter()
        {
            Unit = "%";
            Minimum = 0;
            Maximum = 100;
            Kind = MeasuringPointKind.Measurement;
        }
    }
}
