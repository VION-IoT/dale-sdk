using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    /// Built-in change threshold for <see cref="TimeSpan"/>: the magnitude of the delta
    /// (<c>(candidate - last).Duration()</c>) must be at least the duration-parsed threshold.
    /// </summary>
    internal sealed class TimeSpanChangeThreshold : IChangeThreshold<TimeSpan>
    {
        public bool Exceeds(in TimeSpan lastEmitted, in TimeSpan candidate, string threshold)
        {
            TimeSpan minChange = DurationParser.Parse(threshold);
            return (candidate - lastEmitted).Duration() >= minChange;
        }
    }
}
