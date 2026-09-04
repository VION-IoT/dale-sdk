using System.Collections.Generic;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     The read surface of the vitals core: a point-in-time snapshot of every tracked actor's vitals.
    ///     Injected into a diagnostics logic block and read by the OpenTelemetry exporter.
    /// </summary>
    public interface IRuntimeDiagnostics
    {
        /// <summary>A point-in-time copy of every tracked actor's vitals.</summary>
        IReadOnlyList<ActorVitals> Snapshot();
    }
}