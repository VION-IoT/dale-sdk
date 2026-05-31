namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Unified observation event surfaced by <see cref="IDevHostControl.Subscribe" /> and
    ///     <see cref="IDevHostControl.WaitForAsync{T}" />. A normalized projection of the six
    ///     <see cref="IDevHostEvents" /> change events into one stream that tests/agents can pattern-match.
    ///     <para>
    ///         <c>BlockId</c> is the name assigned in <c>DevConfigurationBuilder.AddLogicBlock(name:)</c> when
    ///         the originating service can be resolved to a block; otherwise it falls back to the raw service
    ///         identifier. (Full block resolution for a no-web headless boot arrives with the introspection
    ///         move — see RFC 0003.)
    ///     </para>
    /// </summary>
    public abstract record DevHostEvent;

    public sealed record ServicePropertyChanged(string BlockId, string ServiceId, string Property, object? Value) : DevHostEvent;

    public sealed record ServiceMeasuringPointChanged(string BlockId, string ServiceId, string MeasuringPoint, object? Value) : DevHostEvent;

    public sealed record DigitalInputChanged(string ServiceProviderId, string ServiceId, string ContractId, bool Value) : DevHostEvent;

    public sealed record DigitalOutputChanged(string ServiceProviderId, string ServiceId, string ContractId, bool Value) : DevHostEvent;

    public sealed record AnalogInputChanged(string ServiceProviderId, string ServiceId, string ContractId, double Value) : DevHostEvent;

    public sealed record AnalogOutputChanged(string ServiceProviderId, string ServiceId, string ContractId, double Value) : DevHostEvent;
}
