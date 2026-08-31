using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.DigitalIo.Output
{
    /// <summary>
    ///     Message from a LogicBlock IO to the <see cref="DigitalOutputHandler" /> to set a digital output
    /// </summary>
    [PublicApi]
    public readonly record struct SetDigitalOutput(bool Value);

    /// <summary>
    ///     Message from the <see cref="DigitalOutputHandler" /> to a LogicBlock IO to notify about a digital output change
    /// </summary>
    [PublicApi]
    public readonly record struct DigitalOutputChanged(bool Value);
}