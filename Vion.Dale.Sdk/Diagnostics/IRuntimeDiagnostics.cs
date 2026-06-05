using System.Collections.Generic;

namespace Vion.Dale.Sdk.Diagnostics
{
    /// <summary>
    ///     The read surface of the vitals core: a point-in-time snapshot of every tracked actor's vitals.
    ///     Injected into a diagnostics logic block (RFC 0005 Sink 2) and read by the OTel exporter (Sink 1).
    /// </summary>
    public interface IRuntimeDiagnostics
    {
        /// <summary>A point-in-time copy of every tracked actor's vitals.</summary>
        IReadOnlyList<ActorVitals> Snapshot();
    }
}
