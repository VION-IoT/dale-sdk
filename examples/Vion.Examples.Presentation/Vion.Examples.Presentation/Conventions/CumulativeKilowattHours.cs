using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>
    ///     Example preset: monotonically-increasing energy counter in kilowatt-hours.
    ///     The dashboard renders this as a rate-of-change chart by default
    ///     (<c>Transform = "difference"</c>).
    /// </summary>
    public class CumulativeKilowattHours : ServiceMeasuringPointAttribute
    {
        public CumulativeKilowattHours()
        {
            Unit = "kWh";
            Minimum = 0;
            Kind = MeasuringPointKind.TotalIncreasing;
        }
    }
}
