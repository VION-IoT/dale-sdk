using System;
using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Utils
{
    /// <summary>
    ///     Default implementation of <see cref="IDateTimeProvider" /> using system time.
    /// </summary>
    [InternalApi]
    public class DateTimeProvider : IDateTimeProvider
    {
        /// <inheritdoc />
        public DateTime UtcNow
        {
            get => DateTime.UtcNow;
        }

        /// <inheritdoc />
        public DateTime Add(DateTime timestamp, TimeSpan duration)
        {
            return timestamp.Add(duration);
        }

        /// <inheritdoc />
        public TimeSpan GetElapsedTime(DateTime since)
        {
            return DateTime.UtcNow - since;
        }
    }
}