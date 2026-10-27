using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Presentation.Conventions
{
    /// <summary>
    ///     Example preset: marks a string property as a secret. The runtime publishes and answers the
    ///     redaction sentinel (<c>"***"</c>) instead of the real value, and keeps the value when a client
    ///     echoes the sentinel back. The block holds the real value, and persists it unencrypted.
    /// </summary>
    public class Secret : ServicePropertyAttribute
    {
        public Secret()
        {
            WriteOnly = true;
        }
    }
}