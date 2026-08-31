using Vion.Dale.Sdk.Abstractions;

namespace Vion.Dale.Sdk.DigitalIo
{
    /// <summary>
    ///     A deliberately invalid <c>[ScenarioWire]</c> declaration: <see cref="NotAWireValue" /> is a class, so
    ///     the scenario codec cannot build it from a scenario value and DALE046 must reject it.
    ///     <para>
    ///         This file is excluded from every ordinary build. It compiles only under
    ///         <c>-p:DaleAnalyzerWiringProbe=true</c>, where the build is <b>expected to fail</b> — that failure
    ///         is what proves the Dale analyzers actually run over this project's declarations, rather than
    ///         being referenced and silently inert. <c>AnalyzerWiringShould</c> in
    ///         <c>Vion.Dale.Sdk.Generators.Test</c> is what runs it. Do not "fix" the declaration; do not add
    ///         it to the default compile items.
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
