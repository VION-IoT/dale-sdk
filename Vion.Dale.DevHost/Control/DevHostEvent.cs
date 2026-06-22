using System.Text.Json;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Unified observation event surfaced by <see cref="IDevHostControl.Subscribe" /> and
    ///     <see cref="IDevHostControl.WaitForAsync{T}" />. A normalized projection of the
    ///     <see cref="IDevHostEvents" /> change events into one stream that tests/agents can pattern-match.
    ///     <para>
    ///         <c>LogicBlockId</c> is the name assigned in <c>DevConfigurationBuilder.AddLogicBlock(name:)</c>
    ///         when the originating service can be resolved to a logic block; otherwise it falls back to the raw
    ///         service identifier.
    ///     </para>
    /// </summary>
    public abstract record DevHostEvent;

    public sealed record ServicePropertyChanged(string LogicBlockId, string ServiceId, string Property, object? Value) : DevHostEvent;

    /// <summary>
    ///     A service-property write completed its round trip (applied by the block, value read back).
    ///     Exists only per actual write — the control surface's ack correlation, immune to stale
    ///     in-flight publishes; fires for no-op writes too (which raise no <see cref="ServicePropertyChanged" />).
    /// </summary>
    public sealed record ServicePropertyWriteAcknowledged(string LogicBlockId, string ServiceId, string Property, object? Value) : DevHostEvent;

    public sealed record ServiceMeasuringPointChanged(string LogicBlockId, string ServiceId, string MeasuringPoint, object? Value) : DevHostEvent;

    /// <summary>
    ///     A service-provider value contract's current value changed (its wire JSON) — an input was driven or an
    ///     output was written. One event for every <c>[ServiceProviderContractType]</c> value contract; the
    ///     subscriber knows the contract's direction and type from the configuration (RFC 0010).
    /// </summary>
    public sealed record ServiceProviderContractChanged(string ServiceProviderId, string ServiceId, string ContractId, JsonElement Value) : DevHostEvent;
}