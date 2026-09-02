using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>Built-in change threshold for <see cref="float" />: absolute delta >= parsed threshold.</summary>
    internal sealed class FloatChangeThreshold : IChangeThreshold<float>, IValidatedChangeThreshold
    {
        public bool Exceeds(in float lastEmitted, in float candidate, string threshold)
        {
            if (float.IsNaN(lastEmitted) || float.IsNaN(candidate))
            {
                // A NaN has no magnitude to compare, so the deadband cannot say the member has not moved.
                return true;
            }

            var minChange = ThresholdToken.ReadSingle(threshold);
            return Math.Abs(candidate - lastEmitted) >= minChange;
        }

        public void ValidateThreshold(string threshold)
        {
            ThresholdToken.ReadSingle(threshold);
        }
    }
}