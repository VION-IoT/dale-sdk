namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Unified observation event surfaced by <see cref="IDevHostControl.Subscribe" /> and
    ///     <see cref="IDevHostControl.WaitForAsync{T}" />. A normalized projection of the six
    ///     <see cref="IDevHostEvents" /> change events into one stream that tests/agents can pattern-match.
    ///     <para>
    ///         <c>LogicBlockId</c> is the name assigned in <c>DevConfigurationBuilder.AddLogicBlock(name:)</c>
    ///         when the originating service can be resolved to a logic block; otherwise it falls back to the raw
    ///         service identifier.
    ///     </para>
    /// </summary>
    public abstract record DevHostEvent;

    public sealed record ServicePropertyChanged(string LogicBlockId, string ServiceId, string Property, object? Value) : DevHostEvent;

    public sealed record ServiceMeasuringPointChanged(string LogicBlockId, string ServiceId, string MeasuringPoint, object? Value) : DevHostEvent;

    public sealed record DigitalInputChanged(string ServiceProviderId, string ServiceId, string ContractId, bool Value) : DevHostEvent;

    public sealed record DigitalOutputChanged(string ServiceProviderId, string ServiceId, string ContractId, bool Value) : DevHostEvent;

    public sealed record AnalogInputChanged(string ServiceProviderId, string ServiceId, string ContractId, double Value) : DevHostEvent;

    public sealed record AnalogOutputChanged(string ServiceProviderId, string ServiceId, string ContractId, double Value) : DevHostEvent;
}
