using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.Test.TestHelpers
{
    /// <summary>
    ///     Stands in for the emission knobs an author declares on <c>[ServiceProperty]</c> /
    ///     <c>[ServiceMeasuringPoint]</c>, so a test can build a policy without reflecting over an
    ///     attribute. The defaults match the attributes' own.
    /// </summary>
    internal sealed class ThrottleKnobs : IThrottleConfigured
    {
        public string MinInterval { get; init; } = "250ms";

        public string? MinChange { get; init; }

        public bool Immediate { get; init; }
    }
}