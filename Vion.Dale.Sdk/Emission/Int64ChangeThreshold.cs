using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>Built-in change threshold for <see cref="long" />: absolute delta >= parsed threshold.</summary>
    internal sealed class Int64ChangeThreshold : IChangeThreshold<long>, IValidatedChangeThreshold
    {
        public bool Exceeds(in long lastEmitted, in long candidate, string threshold)
        {
            var minChange = ThresholdToken.ReadInteger(threshold);
            return Math.Abs((double)candidate - lastEmitted) >= minChange;
        }

        public void ValidateThreshold(string threshold)
        {
            ThresholdToken.ReadInteger(threshold);
        }
    }
}