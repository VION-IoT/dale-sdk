using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     Built-in change threshold for <see cref="double" />: the absolute delta must be at least
    ///     the numeric threshold (parsed invariant-culture).
    /// </summary>
    internal sealed class DoubleChangeThreshold : IChangeThreshold<double>, IValidatedChangeThreshold
    {
        public bool Exceeds(in double lastEmitted, in double candidate, string threshold)
        {
            if (double.IsNaN(lastEmitted) || double.IsNaN(candidate))
            {
                // A NaN has no magnitude to compare, so the deadband cannot say the member has not moved.
                return true;
            }

            var minChange = ThresholdToken.ReadFloat(threshold);
            return Math.Abs(candidate - lastEmitted) >= minChange;
        }

        public void ValidateThreshold(string threshold)
        {
            ThresholdToken.ReadFloat(threshold);
        }
    }
}