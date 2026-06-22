using System;
using System.Globalization;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>Built-in change threshold for <see cref="float" />: absolute delta >= parsed threshold.</summary>
    internal sealed class FloatChangeThreshold : IChangeThreshold<float>
    {
        public bool Exceeds(in float lastEmitted, in float candidate, string threshold)
        {
            var minChange = float.Parse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture);
            return Math.Abs(candidate - lastEmitted) >= minChange;
        }
    }
}