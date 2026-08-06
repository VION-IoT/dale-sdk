using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Block-level display metadata for a LogicBlock class.
    /// </summary>
    /// <remarks>
    ///     The block's <b>full type name — namespace included</b> — is the stable key the cloud files this
    ///     block's translated display strings under. Renaming the class, or moving it to another namespace,
    ///     mints new keys and orphans every translation authored against the old ones; re-attaching them is
    ///     manual work in the dashboard's Translations tab. See <c>docs/identifier-stability.md</c>.
    /// </remarks>
    [PublicApi]
    [AttributeUsage(AttributeTargets.Class)]
    public class LogicBlockAttribute : Attribute
    {
        /// <summary>
        ///     Human-readable name. Falls back to the C# class name. Translatable in the cloud — the
        ///     value here is the source default and the permanent fallback, keyed by the block's full
        ///     type name (see the remarks on this attribute).
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
        ///     A custom key doubles as its own section label and as that label's translation key —
        ///     editing the key orphans its translations (see <see cref="PresentationAttribute.Group" />).
        /// </summary>
        public string[]? Groups { get; init; }
    }
}