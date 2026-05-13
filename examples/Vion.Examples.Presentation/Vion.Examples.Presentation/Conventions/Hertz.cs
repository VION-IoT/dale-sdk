using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>Example preset: frequency in hertz. Non-negative.</summary>
    public class Hertz : ServicePropertyAttribute
    {
        public Hertz()
        {
            Unit = "Hz";
            Minimum = 0;
        }
    }
}
