using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>
    ///     Example preset: a live state metric — appears in the <c>Status</c> section as a
    ///     Primary tile with one-decimal display precision.
    /// </summary>
    public class StateMetric : PresentationAttribute
    {
        public StateMetric()
        {
            Group = PropertyGroup.Status;
            Importance = Importance.Primary;
            Decimals = 1;
        }
    }
}
