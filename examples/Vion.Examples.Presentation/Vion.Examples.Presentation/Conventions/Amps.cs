using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>Example preset: current in amperes.</summary>
    public class Amps : ServicePropertyAttribute
    {
        public Amps()
        {
            Unit = "A";
        }
    }
}
