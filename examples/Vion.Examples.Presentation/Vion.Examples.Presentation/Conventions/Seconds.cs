using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>Example preset: duration in seconds. Non-negative.</summary>
    public class Seconds : ServicePropertyAttribute
    {
        public Seconds()
        {
            Unit = "s";
            Minimum = 0;
        }
    }
}
