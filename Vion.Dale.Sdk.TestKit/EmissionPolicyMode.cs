using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.TestKit
{
    /// <summary>
    ///     Controls whether the RFC 0004 emission policy (attribute-driven throttling) is active
    ///     when a logic block runs under the TestKit's controllable fake clock.
    /// </summary>
    [PublicApi]
    public enum EmissionPolicyMode
    {
        /// <summary>
        ///     Default. The emission policy is gated OFF: every service-property / measuring-point
        ///     assignment flows straight through as a change message, so tests are not silently
        ///     throttled by min-interval / min-change rules.
        /// </summary>
        Off,

        /// <summary>
        ///     Forces the emission policy ON from the block's <c>[ServiceProperty]</c> /
        ///     <c>[ServiceMeasuringPoint]</c> throttle attributes, despite the fake clock. Use this
        ///     to exercise throttling deterministically with <see cref="LogicBlockTestContext{TLogicBlock}.AdvanceTime" />.
        /// </summary>
        FromAttributes,
    }
}