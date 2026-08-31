using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.AnalyzerWiring
{
    /// <summary>
    ///     A deliberately invalid <c>[ScenarioWire]</c> declaration: <see cref="NotAWireValue" /> is a class, so
    ///     the scenario codec cannot build it from a scenario value and DALE046 must reject it.
    ///     <para>
    ///         Nothing compiles this file by default — not this project, which has no reference to the SDK it
    ///         names, and not the projects it is compiled into. <see cref="AnalyzerWiringShould" /> builds
    ///         <c>Vion.Dale.Sdk.DigitalIo</c> and <c>.AnalogIo</c> with <c>-p:DaleAnalyzerWiringProbe=true</c>,
    ///         which links this one file into those compilations and <b>expects the build to fail</b>. That
    ///         failure is the proof the Dale analyzers actually run over those projects rather than being
    ///         referenced and silently inert.
    ///     </para>
    ///     <para>
    ///         So: do not "fix" the declaration, and do not add it to any project's default compile items. It
    ///         lives here, beside the test that owns it, so no shipped project carries a source file that
    ///         exists only to fail.
    ///     </para>
    /// </summary>
    [ScenarioWire(Inbound = typeof(NotAWireValue))]
    internal sealed class ScenarioWireWiringProbe
    {
    }

    /// <summary>A reference type, which is exactly what makes the probe above invalid.</summary>
    internal sealed class NotAWireValue
    {
        public bool Value { get; set; }
    }
}
