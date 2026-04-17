using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Declares the UI severity for an enum value used with <see cref="StatusIndicatorAttribute" />.
    ///     Use <see cref="EnumValueInfoAttribute" /> to provide a display name.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Field)]
    public class StatusSeverityAttribute : Attribute
    {
        public StatusSeverity Severity { get; }

        public StatusSeverityAttribute(StatusSeverity severity)
        {
            Severity = severity;
        }
    }
}