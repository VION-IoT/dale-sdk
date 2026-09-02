using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>Built-in change threshold for <see cref="int" />: absolute delta >= parsed threshold.</summary>
    internal sealed class Int32ChangeThreshold : IChangeThreshold<int>, IValidatedChangeThreshold
    {
        public bool Exceeds(in int lastEmitted, in int candidate, string threshold)
        {
            var minChange = ThresholdToken.ReadInteger(threshold);
            return Math.Abs((long)candidate - lastEmitted) >= minChange;
        }

        public void ValidateThreshold(string threshold)
        {
            ThresholdToken.ReadInteger(threshold);
        }
    }
}