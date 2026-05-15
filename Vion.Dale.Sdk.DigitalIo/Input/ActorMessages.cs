namespace Vion.Dale.Sdk.DigitalIo.Input
{
    /// <summary>
    ///     Message from the <see cref="DigitalInputHandler" /> to a LogicBlock IO to notify about a digital input change.
    /// </summary>
    public readonly record struct DigitalInputChanged(bool Value);
}