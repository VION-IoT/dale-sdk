namespace Vion.Dale.Sdk.AnalogIo.Output
{
    /// <summary>
    ///     Message from a LogicBlock IO to the <see cref="AnalogOutputHandler" /> to set an analog output
    /// </summary>
    public readonly record struct SetAnalogOutput(double Value);

    /// <summary>
    ///     Message from the <see cref="AnalogOutputHandler" /> to a LogicBlock IO to notify about an analog output change
    /// </summary>
    public readonly record struct AnalogOutputChanged(double Value);
}