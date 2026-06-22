using System;
using System.Globalization;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>Built-in change threshold for <see cref="int" />: absolute delta >= parsed threshold.</summary>
    internal sealed class Int32ChangeThreshold : IChangeThreshold<int>
    {
        public bool Exceeds(in int lastEmitted, in int candidate, string threshold)
        {
            var minChange = int.Parse(threshold, NumberStyles.Integer, CultureInfo.InvariantCulture);
            return Math.Abs((long)candidate - lastEmitted) >= minChange;
        }
    }
}