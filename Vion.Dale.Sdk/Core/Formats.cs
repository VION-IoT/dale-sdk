namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Well-known values for <see cref="PresentationAttribute.Format" />. Two reserved
    ///     sentinels (<see cref="Relative" />, <see cref="Humanize" />) plus shortcuts for the
    ///     common moment.js / day.js format tokens.
    ///     <para />
    ///     Open set — the renderer accepts any moment-compatible token string directly. These
    ///     constants exist for discoverability and IntelliSense, not as a closed vocabulary.
    /// </summary>
    [PublicApi]
    public static class Formats
    {
        // ── Reserved sentinels ──

        /// <summary>
        ///     Auto-updating relative date display. The renderer calls <c>moment(value).fromNow()</c>
        ///     and refreshes on a timer. Locale-aware: "3 Minuten" in German, "il y a 3 minutes"
        ///     in French.
        /// </summary>
        public const string Relative = "relative";

        /// <summary>
        ///     Humanized duration display. The renderer calls <c>moment.duration(value).humanize()</c>
        ///     to produce rough natural-language output like "a few seconds" / "3 hours" /
        ///     "2 days". Locale-aware.
        /// </summary>
        public const string Humanize = "humanize";

        // ── Locale-aware date / time tokens ──

        /// <summary>Locale-aware full date + time with weekday, e.g. "Monday, September 4, 1986 8:30 PM".</summary>
        public const string LocaleFull = "LLLL";

        /// <summary>Locale-aware date + time, e.g. "September 4, 1986 8:30 PM".</summary>
        public const string LocaleLong = "LLL";

        /// <summary>Locale-aware short date + time, e.g. "Sep 4, 1986 8:30 PM".</summary>
        public const string LocaleShort = "lll";

        /// <summary>Locale-aware date only, e.g. "09/04/1986".</summary>
        public const string LocaleDate = "L";

        /// <summary>Locale-aware time only with seconds, e.g. "8:30:25 PM".</summary>
        public const string LocaleTime = "LTS";

        // ── ISO-ish explicit tokens ──

        /// <summary>"2026-05-13 14:32:05" — explicit ISO-ish date-time without millis.</summary>
        public const string Iso = "YYYY-MM-DD HH:mm:ss";

        /// <summary>"2026-05-13 14:32:05.123" — explicit ISO-ish with millisecond precision.</summary>
        public const string IsoMillis = "YYYY-MM-DD HH:mm:ss.SSS";

        // ── Duration / clock tokens ──

        /// <summary>"01:23:45" — clock-style duration without millis.</summary>
        public const string Clock = "HH:mm:ss";

        /// <summary>"01:23:45.123" — clock-style duration with millisecond precision.</summary>
        public const string ClockMillis = "HH:mm:ss.SSS";
    }
}
