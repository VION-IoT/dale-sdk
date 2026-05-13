using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>Example preset: temperature in degrees Celsius.</summary>
    public class Celsius : ServicePropertyAttribute
    {
        public Celsius()
        {
            Unit = "°C";
        }
    }
}
