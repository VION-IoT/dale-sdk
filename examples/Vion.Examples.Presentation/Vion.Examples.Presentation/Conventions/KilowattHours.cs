using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>Example preset: energy in kilowatt-hours. Non-negative.</summary>
    public class KilowattHours : ServicePropertyAttribute
    {
        public KilowattHours()
        {
            Unit = "kWh";
            Minimum = 0;
        }
    }
}
