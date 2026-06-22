using System;
using System.Globalization;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>Built-in change threshold for <see cref="long"/>: absolute delta >= parsed threshold.</summary>
    internal sealed class Int64ChangeThreshold : IChangeThreshold<long>
    {
        public bool Exceeds(in long lastEmitted, in long candidate, string threshold)
        {
            long minChange = long.Parse(threshold, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return Math.Abs((double)candidate - lastEmitted) >= minChange;
        }
    }
}
