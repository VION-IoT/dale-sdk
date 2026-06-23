using System;
using System.Globalization;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>Built-in change threshold for <see cref="decimal" />: absolute delta >= parsed threshold.</summary>
    internal sealed class DecimalChangeThreshold : IChangeThreshold<decimal>
    {
        public bool Exceeds(in decimal lastEmitted, in decimal candidate, string threshold)
        {
            var minChange = decimal.Parse(threshold, NumberStyles.Number, CultureInfo.InvariantCulture);
            return Math.Abs(candidate - lastEmitted) >= minChange;
        }
    }
}