using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Block-level display metadata for a LogicBlock class.
    /// </summary>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class LogicBlockAttribute : Attribute
    {
        /// <summary>Human-readable name. Falls back to the C# class name.</summary>
        public string? Name { get; init; }

        /// <summary>
        ///     Icon identifier. Use Remixicon names without the "ri-" prefix
        ///     (e.g. "charging-pile-line", "battery-2-line"). See https://remixicon.com.
        ///     Dashboard renders a default fallback icon for unknown / missing values.
        /// </summary>
        public string? Icon { get; init; }

        /// <summary>
        ///     Order in which the dashboard renders group sections in the full block view.
        ///     Values are the same string keys as <see cref="PresentationAttribute.Group" /> —
        ///     well-known constants from <see cref="PropertyGroup" /> and/or integrator-supplied
        ///     custom keys. Groups not listed appear last in the platform default order.
        ///     When unset, defaults to [Alarm, Status, Metric, Configuration, Diagnostics, Identity, None].
        /// </summary>
        public string[]? Groups { get; init; }
    }
}
