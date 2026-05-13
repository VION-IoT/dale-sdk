using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>
    ///     Example preset: a diagnostic / health-check value. Renders in the (collapsed by
    ///     default) <c>Diagnostics</c> section.
    /// </summary>
    public class Diagnostic : PresentationAttribute
    {
        public Diagnostic()
        {
            Group = PropertyGroup.Diagnostics;
        }
    }
}
