using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>Built-in change threshold for <see cref="decimal" />: absolute delta >= parsed threshold.</summary>
    internal sealed class DecimalChangeThreshold : IChangeThreshold<decimal>, IValidatedChangeThreshold
    {
        public bool Exceeds(in decimal lastEmitted, in decimal candidate, string threshold)
        {
            var minChange = ThresholdToken.ReadDecimal(threshold);
            return Math.Abs(candidate - lastEmitted) >= minChange;
        }

        public void ValidateThreshold(string threshold)
        {
            ThresholdToken.ReadDecimal(threshold);
        }
    }
}