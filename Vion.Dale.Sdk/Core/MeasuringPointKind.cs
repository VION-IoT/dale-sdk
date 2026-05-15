namespace Vion.Dale.Sdk.Core
{
    /// <summary>Time-series shape of a measuring point. Mirrors the wire enum in
    /// Vion.Contracts.TypeRef; mapped at the introspection boundary so integrators
    /// only reference Vion.Dale.Sdk.Core.</summary>
    [PublicApi]
    public enum MeasuringPointKind
    {
        Measurement = 0,
        Total = 1,
        TotalIncreasing = 2,
    }
}
