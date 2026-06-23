using System;

namespace Vion.Dale.Sdk.Emission
{
    /// <summary>
    ///     A marker registered in the logic block's service provider by the TestKit's
    ///     <c>LogicBlockTestContextBuilder.WithEmissionPolicy(EmissionPolicyMode.FromAttributes)</c>.
    ///     <para>
    ///         RFC 0004 emission policy is normally gated off whenever the block runs on a
    ///         controllable (test) clock — otherwise virtual-time tests would silently throttle
    ///         their own emissions. When this marker is present, <c>LogicBlockBase</c> reads it at
    ///         <c>InitializeLogicBlock</c> and forces the policy on regardless of the clock by
    ///         setting <c>_forcePolicyFromAttributes = true</c>, so tests can exercise the
    ///         attribute-driven throttling deterministically.
    ///     </para>
    ///     <para>
    ///         Internal by design: it is part of the TestKit↔SDK contract (the SDK grants
    ///         <c>InternalsVisibleTo</c> to <c>Vion.Dale.Sdk.TestKit</c>), not a public knob.
    ///     </para>
    /// </summary>
    internal sealed class EmissionPolicyForceMarker
    {
    }
}