using System;
using System.Globalization;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     Parses the small duration grammar used by the emission policy's <c>MinInterval</c> and the
    ///     <see cref="TimeSpanChangeThreshold" /> token: a number followed by an optional unit suffix.
    ///     Supported units: <c>us</c> (microseconds), <c>ms</c> (milliseconds), <c>s</c> (seconds),
    ///     <c>m</c> (minutes), <c>h</c> (hours). A bare number (no suffix) is treated as milliseconds.
    ///     All parsing is invariant-culture and case-insensitive on the suffix.
    /// </summary>
    internal static class DurationParser
    {
        public static TimeSpan Parse(string token)
        {
            if (token == null)
            {
                throw new ArgumentNullException(nameof(token));
            }

            var trimmed = token.Trim();
            if (trimmed.Length == 0)
            {
                throw new FormatException("Duration token is empty.");
            }

            // Split the numeric head from the (optional) alphabetic unit suffix.
            var splitIndex = trimmed.Length;
            for (var i = 0; i < trimmed.Length; i++)
            {
                var c = trimmed[i];
                var isNumeric = (c >= '0' && c <= '9') || c == '.' || c == '+' || c == '-';
                if (!isNumeric)
                {
                    splitIndex = i;
                    break;
                }
            }

            var numberPart = trimmed.Substring(0, splitIndex);
            var unitPart = trimmed.Substring(splitIndex).Trim().ToLowerInvariant();

            if (numberPart.Length == 0)
            {
                throw new FormatException($"Duration token '{token}' has no numeric part.");
            }

            var value = double.Parse(numberPart, NumberStyles.Float, CultureInfo.InvariantCulture);

            switch (unitPart)
            {
                case "":
                case "ms":
                    return TimeSpan.FromMilliseconds(value);
                case "us":
                    // 1 tick = 100 ns => 1 microsecond = 10 ticks.
                    return TimeSpan.FromTicks((long)Math.Round(value * 10.0));
                case "s":
                    return TimeSpan.FromSeconds(value);
                case "m":
                    return TimeSpan.FromMinutes(value);
                case "h":
                    return TimeSpan.FromHours(value);
                default:
                    throw new FormatException($"Unknown duration unit '{unitPart}' in token '{token}'.");
            }
        }
    }
}