using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>
    ///     Example preset: active power in kilowatts. Non-negative.
    ///     Integrators ship their own equivalents under their own namespace.
    /// </summary>
    public class Kilowatts : ServicePropertyAttribute
    {
        public Kilowatts()
        {
            Unit = "kW";
            Minimum = 0;
        }
    }
}
