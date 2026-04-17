using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Marks a property as an operational status indicator.
    ///     The property should be an enum type where each value has a <see cref="StatusSeverityAttribute" />.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Property)]
    public class StatusIndicatorAttribute : Attribute
    {
        public Dictionary<string, object> Annotations
        {
            get =>
                new()
                {
                    ["StatusIndicator"] = true,
                };
        }
    }
}