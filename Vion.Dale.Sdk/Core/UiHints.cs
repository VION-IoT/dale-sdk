namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Well-known UiHint values for <see cref="PresentationAttribute.UiHint" />.
    ///     Open set — the dashboard ignores unknown values and falls back to the default
    ///     renderer for the property's schema kind.
    /// </summary>
    [PublicApi]
    public static class UiHints
    {
        /// <summary>
        ///     Auto-emitted when <see cref="PresentationAttribute.StatusIndicator" /> is true.
        ///     Do not set directly; use the boolean field. Tile renders as a status pill / badge.
        /// </summary>
        public const string StatusIndicator = "statusIndicator";

        /// <summary>
        ///     Renders a writable bool property as a button instead of a toggle.
        ///     Click commits <c>true</c>; the property's getter should always return <c>false</c>.
        ///     Bridge for operator-triggered actions until a first-class action primitive ships.
        ///     Forbidden with <c>[Persistent]</c>.
        /// </summary>
        public const string Trigger = "trigger";

        /// <summary>Inline sparkline rendering for numeric arrays or numeric measuring points.</summary>
        public const string Sparkline = "sparkline";

        /// <summary>Renders a writable string property as a multi-line textarea.</summary>
        public const string Multiline = "multiline";

        /// <summary>Renders a writable string property as a code editor with JSON syntax highlighting. Implies multi-line.</summary>
        public const string Json = "json";

        /// <summary>Renders a writable numeric property with bounded Minimum AND Maximum as a slider control.</summary>
        public const string Slider = "slider";
    }
}
