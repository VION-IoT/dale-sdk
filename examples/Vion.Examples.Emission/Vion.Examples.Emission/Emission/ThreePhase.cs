using Vion.Dale.Sdk.Core;

namespace Vion.Examples.Emission
{
    /// <summary>
    ///     Three-phase current (A) — a custom struct value type. Its companion
    ///     <see cref="ThreePhaseChangeThreshold" /> lets a <c>MinChange</c> deadband resolve for a type the
    ///     SDK has no built-in threshold for (RFC 0004 / DF-34). The per-field <see cref="StructFieldAttribute" />
    ///     annotations make it render as three labelled inputs in the DevHost UI.
    /// </summary>
    public readonly record struct ThreePhase(
        [StructField(Title = "L1", Unit = "A")]
        double L1,
        [StructField(Title = "L2", Unit = "A")]
        double L2,
        [StructField(Title = "L3", Unit = "A")]
        double L3);
}