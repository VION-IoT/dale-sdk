using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>Example preset: percentage from 0 to 100.</summary>
    public class Percent : ServicePropertyAttribute
    {
        public Percent()
        {
            Unit = "%";
            Minimum = 0;
            Maximum = 100;
        }
    }
}
