using Vion.Dale.Sdk.Core;

namespace Vion.Dale.Sdk.AnalogIo.Input
{
    /// <summary>
    ///     Message from the <see cref="AnalogInputHandler" /> to a LogicBlock IO to notify about an analog input change.
    /// </summary>
    [PublicApi]
    public readonly record struct AnalogInputChanged(double Value);
}