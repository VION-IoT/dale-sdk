using System.Globalization;
using Moq;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Extension methods to validate Moq Times constraints against actual invocation counts.
    /// </summary>
    [PublicApi]
    public static class TimesExtensions
    {
        /// <summary>
        ///     Throws a <see cref="TestKitVerificationException" /> unless <paramref name="actualCount" />
        ///     satisfies <paramref name="times" />. Every occurrence-count form Moq expresses is honoured,
        ///     because the check is Moq's own <c>Times.Validate</c> rather than a reading of the constraint's
        ///     rendered text.
        /// </summary>
        public static void AssertCount(this Times times, int actualCount, string assertMessage)
        {
            if (times.Validate(actualCount))
            {
                return;
            }

            // Invariant so the rendered count is the same text on every machine. An int formats identically
            // in every culture today, so this pins a rule rather than repairing a defect.
            throw new TestKitVerificationException(string.Format(CultureInfo.InvariantCulture, "{0}: Expected {1} but found {2}.", assertMessage, times, actualCount));
        }
    }
}