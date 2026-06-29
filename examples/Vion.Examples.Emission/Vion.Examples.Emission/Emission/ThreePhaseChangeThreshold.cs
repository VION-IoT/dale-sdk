using System;
using System.Globalization;
using Vion.Dale.Sdk.Emission;

namespace Vion.Examples.Emission
{
    /// <summary>
    ///     Resolves the Δ deadband for <see cref="ThreePhase" />. The runtime discovers this by scanning the
    ///     block's assembly at start (DF-34), so any <see cref="ThreePhase" /> property can set <c>MinChange</c>
    ///     without a per-property registration. Must have a parameterless ctor. Returns true (emit) when ANY
    ///     phase moves by at least the threshold (absolute amps).
    /// </summary>
    public sealed class ThreePhaseChangeThreshold : IChangeThreshold<ThreePhase>
    {
        public bool Exceeds(in ThreePhase lastEmitted, in ThreePhase candidate, string threshold)
        {
            var min = double.Parse(threshold, NumberStyles.Float, CultureInfo.InvariantCulture);
            return Math.Abs(candidate.L1 - lastEmitted.L1) >= min || Math.Abs(candidate.L2 - lastEmitted.L2) >= min || Math.Abs(candidate.L3 - lastEmitted.L3) >= min;
        }
    }
}