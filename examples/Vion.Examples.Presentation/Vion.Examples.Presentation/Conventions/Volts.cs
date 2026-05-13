using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>Example preset: voltage in volts.</summary>
    public class Volts : ServicePropertyAttribute
    {
        public Volts()
        {
            Unit = "V";
        }
    }
}
