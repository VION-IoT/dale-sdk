using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Per-enum-member severity used with <c>[Presentation(StatusIndicator = true)]</c>.
    ///     The dashboard reads severity for each enum member to color the status pill.
    ///     Companion to <see cref="EnumLabelAttribute" /> which supplies the display label.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Field)]
    public class SeverityAttribute : Attribute
    {
        public StatusSeverity Severity { get; }

        public SeverityAttribute(StatusSeverity severity)
        {
            Severity = severity;
        }
    }
}
