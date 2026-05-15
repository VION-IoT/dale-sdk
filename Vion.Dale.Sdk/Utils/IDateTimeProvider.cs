using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Utils
{
    /// <summary>
    ///     Provides an abstraction for date and time operations.
    /// </summary>
    [PublicApi]
    public interface IDateTimeProvider
    {
        /// <summary>
        ///     Gets the current date and time in UTC.
        /// </summary>
        public DateTime UtcNow { get; }

        /// <summary>
        ///     Adds the specified <see cref="TimeSpan" /> to the specified <see cref="DateTime" />.
        /// </summary>
        /// <param name="timestamp">The date and time to add to.</param>
        /// <param name="duration">The time span to add.</param>
        /// <returns>A <see cref="DateTime" /> that is the sum of <paramref name="timestamp" /> and <paramref name="duration" />.</returns>
        DateTime Add(DateTime timestamp, TimeSpan duration);

        /// <summary>
        ///     Gets the elapsed time since the specified <see cref="DateTime" />.
        /// </summary>
        /// <param name="since">The date and time to measure elapsed time from.</param>
        /// <returns>A <see cref="TimeSpan" /> representing the elapsed time since <paramref name="since" />.</returns>
        TimeSpan GetElapsedTime(DateTime since);
    }
}