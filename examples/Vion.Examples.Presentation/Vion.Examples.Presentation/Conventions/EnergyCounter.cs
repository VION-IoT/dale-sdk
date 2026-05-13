using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>
    ///     Example preset: a cumulative energy / total counter. Renders in the
    ///     <c>Metric</c> section with Secondary importance.
    /// </summary>
    public class EnergyCounter : PresentationAttribute
    {
        public EnergyCounter()
        {
            Group = PropertyGroup.Metric;
            Importance = Importance.Secondary;
        }
    }
}
