using System;
using System.Globalization;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     Parses the small duration grammar used by the emission policy's <c>MinInterval</c> and the
    ///     <see cref="TimeSpanChangeThreshold" /> token: a number followed by an optional unit suffix.
    ///     Supported units: <c>us</c> (microseconds), <c>ms</c> (milliseconds), <c>s</c> (seconds),
    ///     <c>m</c> (minutes), <c>h</c> (hours). A bare number (no suffix) is treated as milliseconds.
    ///     All parsing is invariant-culture and case-insensitive on the suffix. A negative value is
    ///     rejected: both knobs the grammar serves are magnitudes (a spacing between emissions, a change
    ///     magnitude), and a negative one makes the gate it configures unconditional instead of
    ///     restrictive — the exact opposite of what was declared.
    /// </summary>
    internal static class DurationParser
    {
        /// <summary>
        ///     Non-throwing form of <see cref="Parse" />, for a caller that must keep working on a knob the
        ///     emission analyzers already reject. Introspection has to describe a block whose author
        ///     suppressed DALE036, so it asks whether a token is a duration instead of assuming it is.
        /// </summary>
        public static bool TryParse(string? token, out TimeSpan value)
        {
            value = default;
            if (token == null)
            {
                return false;
            }

            try
            {
                value = Parse(token);
                return true;
            }
            catch (FormatException)
            {
                // Malformed, negative, or an unknown unit — every rejection Parse makes.
                return false;
            }
            catch (OverflowException)
            {
                // A numeric part too large for the unit, e.g. "1e30h".
                return false;
            }
        }

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

            if (value < 0)
            {
                throw new FormatException($"Duration token '{token}' is negative; durations are magnitudes.");
            }

            double milliseconds;
            switch (unitPart)
            {
                case "":
                case "ms":
                    milliseconds = value;
                    break;
                case "us":
                    // 1 tick = 100 ns => 1 microsecond = 10 ticks.
                    return TimeSpan.FromTicks((long)Math.Round(value * 10.0));
                case "s":
                    milliseconds = value * 1_000.0;
                    break;
                case "m":
                    milliseconds = value * 60.0 * 1_000.0;
                    break;
                case "h":
                    milliseconds = value * 60.0 * 60.0 * 1_000.0;
                    break;
                default:
                    throw new FormatException($"Unknown duration unit '{unitPart}' in token '{token}'.");
            }

            // TimeSpan.From* throws an OverflowException naming nothing, which reads as a runtime fault
            // rather than the malformed knob it is. Reject it the way every other bad token is rejected.
            if (milliseconds > TimeSpan.MaxValue.TotalMilliseconds)
            {
                throw new FormatException($"Duration token '{token}' is larger than a duration can represent.");
            }

            return TimeSpan.FromMilliseconds(milliseconds);
        }
    }
}