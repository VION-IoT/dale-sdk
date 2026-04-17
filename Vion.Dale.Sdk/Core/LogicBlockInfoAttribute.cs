using System;
using System.Collections.Generic;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Provides block-level display metadata for a logic block class.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class)]
    public class LogicBlockInfoAttribute : Attribute
    {
        public string? DefaultName { get; }

        /// <summary>
        ///     Icon identifier used by the frontend to render a block icon.
        ///     Use Remixicon names without the "ri-" prefix (e.g. "charging-pile-line", "battery-2-line").
        ///     See https://remixicon.com for available icons.
        ///     The frontend will render a default fallback icon for unknown or missing values.
        /// </summary>
        public string? Icon { get; }

        public Dictionary<string, object> Annotations
        {
            get
            {
                var annotations = new Dictionary<string, object>();

                if (!string.IsNullOrEmpty(DefaultName))
                {
                    annotations["DefaultName"] = DefaultName;
                }

                if (!string.IsNullOrEmpty(Icon))
                {
                    annotations["Icon"] = Icon;
                }

                return annotations;
            }
        }

        public LogicBlockInfoAttribute(string? defaultName = null, string? icon = null)
        {
            DefaultName = defaultName;
            Icon = icon;
        }
    }
}