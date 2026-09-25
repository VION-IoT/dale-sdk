using System;

namespace Vion.Dale.Sdk.Core
{
    /// <summary>The three emission knobs, as flags of <see cref="IEmissionAttribute.Assigned" />.</summary>
    [Flags]
    internal enum EmissionKnob
    {
        None = 0,

        MinInterval = 1,

        MinChange = 2,

        Immediate = 4,
    }
}
