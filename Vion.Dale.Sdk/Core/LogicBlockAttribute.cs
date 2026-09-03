using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Block-level display metadata for a LogicBlock class.
    /// </summary>
    /// <remarks>
    ///     Every display string this block declares is translatable in the cloud, under a key rooted in the
    ///     block's <b>full type name — namespace included</b>. Renaming or moving the class orphans those
    ///     translations. See <c>docs/specs/introspection.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class)]
    public class LogicBlockAttribute : Attribute
    {
        /// <summary>
        ///     Human-readable name. Falls back to the C# class name. Translatable in the cloud (see the
        ///     remarks on this attribute).
        /// </summary>
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
        ///     A custom key is also the translation key of its section label — editing it orphans that
        ///     label's translations.
        /// </summary>
        public string[]? Groups { get; init; }
    }
}