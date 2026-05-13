namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Well-known property-group keys for <see cref="PresentationAttribute.Group" />.
    ///     The platform ships these; integrators may define their own constants in their own
    ///     static classes (e.g. <c>Acme.Vion.Conventions.PropertyGroup.Powertrain = "acme.powertrain"</c>)
    ///     and the dashboard renders unknown keys as a generic section with the raw key as
    ///     the header.
    /// </summary>
    [PublicApi]
    public static class PropertyGroup
    {
        /// <summary>Ungrouped — renders without a section header (fallback bucket).</summary>
        public const string None = "";

        /// <summary>
        ///     Static identification information — manufacturer, model number, serial number,
        ///     firmware version. Typically rendered in a header / about area.
        /// </summary>
        public const string Identity = "identity";

        /// <summary>Current live operational state — read-only values that reflect what the system is doing right now.</summary>
        public const string Status = "status";

        /// <summary>
        ///     Anything the operator can write — long-term settings, runtime controls, action
        ///     triggers (<c>UiHint = UiHints.Trigger</c>). Render type within the section is
        ///     driven by <see cref="PresentationAttribute.UiHint" />, not by group.
        /// </summary>
        public const string Configuration = "configuration";

        /// <summary>Counters, totals, accumulated values. Often rendered prominently for energy / billing-style data.</summary>
        public const string Metric = "metric";

        /// <summary>Troubleshooting and health information — last error, response time, connectivity. Usually a collapsed / secondary section.</summary>
        public const string Diagnostics = "diagnostics";

        /// <summary>Active alarm state, fault codes. Rendered with elevated visual treatment (banner / alert list) when active.</summary>
        public const string Alarm = "alarm";
    }
}
