namespace Vion.Dale.Sdk.Core
{
    /// <summary>Time-series shape of a measuring point. Mirrors the wire enum in
    /// Vion.Contracts.TypeRef; mapped at the introspection boundary so integrators
    /// only reference Vion.Dale.Sdk.Core.</summary>
    [PublicApi]
    public enum MeasuringPointKind
    {
        /// <summary>
        ///     Instantaneous value at a moment in time. Each sample is independent
        ///     of previous samples — reflects current state, not running aggregate.
        ///     Examples: active power (kW), voltage, temperature, state of charge (%).
        ///     Chart default: line. Aggregation: avg per bucket.
        /// </summary>
        Measurement = 0,

        /// <summary>
        ///     Cumulative running aggregate that can both increase and decrease
        ///     (without a hardware reset). Each sample is the absolute cumulative
        ///     value at that moment. Examples: battery stored energy (kWh, absolute),
        ///     water tank volume, daily energy import (resets at midnight).
        ///     Chart default: cumulative line. Aggregation: last per bucket.
        /// </summary>
        Total = 1,

        /// <summary>
        ///     Monotonically-increasing counter. Only goes up; a drop is anomalous
        ///     (overflow or hardware/firmware reset) and the platform applies
        ///     correction (clamp to zero). Examples: lifetime energy meter,
        ///     odometer, total operating hours, cycle count.
        ///     Chart default: derivative (rate per bucket). Aggregation: last - first.
        /// </summary>
        TotalIncreasing = 2,
    }
}
