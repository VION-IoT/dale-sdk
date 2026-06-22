namespace Vion.Dale.Sdk.Core
{
    /// <summary>
    ///     Internal shared surface for the per-element emission-throttle knobs declared on
    ///     <see cref="ServicePropertyAttribute" /> and <see cref="ServiceMeasuringPointAttribute" />.
    ///     The emission gate (RFC 0004) reads the throttle configuration through this interface so it
    ///     does not have to special-case which attribute carried the member.
    /// </summary>
    internal interface IThrottleConfigured
    {
        /// <summary>
        ///     Minimum spacing between two emitted values, as a duration string
        ///     (e.g. <c>"250ms"</c>, <c>"1s"</c>, <c>"500us"</c>, <c>"0"</c>). Defaults to <c>"250ms"</c>.
        ///     <c>"0"</c> / <c>"0ms"</c> disables throttling for the member.
        /// </summary>
        string MinInterval { get; }

        /// <summary>
        ///     Optional minimum change a candidate value must clear (relative to the last emitted value)
        ///     before it is allowed through, resolved against a registered change-threshold for the
        ///     member's value type. <c>null</c> means no change gate.
        /// </summary>
        string? MinChange { get; }

        /// <summary>
        ///     When <c>true</c>, every observed change is emitted immediately, bypassing the interval and
        ///     change gates. Defaults to <c>false</c>.
        /// </summary>
        bool Immediate { get; }
    }
}
