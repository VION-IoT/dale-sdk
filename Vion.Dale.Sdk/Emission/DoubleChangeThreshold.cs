using System;
using System.Globalization;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     Built-in change threshold for <see cref="double" />: the absolute delta must be at least
    ///     the numeric threshold (parsed invariant-culture).
    /// </summary>
    internal sealed class DoubleChangeThreshold : IChangeThreshold<double>
    {
        public bool Exceeds(in double lastEmitted, in double candidate, string threshold)
        {
            var minChange = double.Parse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture);
            return Math.Abs(candidate - lastEmitted) >= minChange;
        }
    }
}