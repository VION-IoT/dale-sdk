using System;
using System.Globalization;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     The numeric <c>MinChange</c> grammar the built-in deadbands share: an invariant-culture number
    ///     that is not negative. A deadband is compared against the magnitude of a change, so a negative
    ///     threshold is cleared by every change and the declared deadband would never suppress anything —
    ///     rejected here rather than accepted and ignored.
    /// </summary>
    internal static class ThresholdToken
    {
        public static double ReadFloat(string token)
        {
            if (!double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) || value < 0)
            {
                throw Reject(token, "a number that is not negative");
            }

            return value;
        }

        public static float ReadSingle(string token)
        {
            return (float)ReadFloat(token);
        }

        public static decimal ReadDecimal(string token)
        {
            if (!decimal.TryParse(token, NumberStyles.Number, CultureInfo.InvariantCulture, out var value) || value < 0)
            {
                throw Reject(token, "a number that is not negative");
            }

            return value;
        }

        public static long ReadInteger(string token)
        {
            if (!long.TryParse(token, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) || value < 0)
            {
                throw Reject(token, "a whole number that is not negative");
            }

            return value;
        }

        private static FormatException Reject(string token, string expectation)
        {
            return new FormatException($"MinChange token '{token}' is not usable by this deadband; {expectation} is expected.");
        }
    }
}