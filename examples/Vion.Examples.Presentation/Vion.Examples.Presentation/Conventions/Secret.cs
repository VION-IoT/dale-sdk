using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>
    ///     Example preset: marks a string property as a secret. The runtime publishes the
    ///     redaction sentinel (<c>"***"</c>) on the state channel instead of the real value.
    /// </summary>
    public class Secret : ServicePropertyAttribute
    {
        public Secret()
        {
            WriteOnly = true;
        }
    }
}
