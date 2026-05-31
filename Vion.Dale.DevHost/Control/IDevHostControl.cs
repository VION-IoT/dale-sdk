using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Vion.Dale.DevHost.Control
{
    /// <summary>
    ///     Headless, scriptable control surface for a running DevHost network — the in-process complement to
    ///     the web UI, for CI / integration tests / agents. Reachable via <see cref="IDevHost.Control" /> after
    ///     <see cref="IDevHost.StartAsync" />. See RFC 0003.
    ///     <para>
    ///         This is the observation surface (topology, state-change events, logs). Get/set of properties and
    ///         the inter-block message tap are added in later increments — see RFC 0003.
    ///     </para>
    /// </summary>
    public interface IDevHostControl
    {
        /// <summary>The blocks in the wired network, with their ids, names, type, and service identifiers.</summary>
        IReadOnlyList<BlockInfo> ListBlocks();

        /// <summary>
        ///     Read the last-known value of a block's <c>[ServiceProperty]</c> / <c>[ServiceMeasuringPoint]</c>,
        ///     keyed by the block name (assigned in <c>AddLogicBlock(name:)</c>) or its id, plus the member
        ///     name. Returns <c>null</c> if the property is unknown or hasn't produced a value yet. This is the
        ///     last <em>published</em> value — exactly what the web UI shows (RFC 0003).
        /// </summary>
        object? GetProperty(string blockIdOrName, string propertyName);

        /// <summary>All last-known property/measuring-point values for a block, keyed by member name.</summary>
        IReadOnlyDictionary<string, object?> GetAllProperties(string blockIdOrName);

        /// <summary>Write a writable <c>[ServiceProperty]</c> ("knob") — the programmatic equivalent of the UI's edit field.</summary>
        System.Threading.Tasks.Task SetPropertyAsync(string blockIdOrName, string propertyName, object value);

        /// <summary>Set a mocked digital input value, routed to the linked blocks just like the web UI's HAL control.</summary>
        System.Threading.Tasks.Task SetDigitalInputAsync(string serviceProviderId, string serviceId, string contractId, bool value);

        /// <summary>Set a mocked analog input value, routed to the linked blocks just like the web UI's HAL control.</summary>
        System.Threading.Tasks.Task SetAnalogInputAsync(string serviceProviderId, string serviceId, string contractId, double value);

        /// <summary>
        ///     Subscribe to the normalized state-change event stream (the projection of <see cref="IDevHostEvents" />).
        ///     Dispose the returned token to unsubscribe.
        /// </summary>
        IDisposable Subscribe(Action<DevHostEvent> sink);

        /// <summary>
        ///     Wait until an event satisfies <paramref name="selector" /> (returns non-null), or until
        ///     <paramref name="timeout" /> elapses. Returns the selector's value, or <c>null</c> on timeout —
        ///     condition-based waiting, the multi-block runtime's substitute for synchronous time stepping
        ///     (RFC 0003, "the determinism trade-off").
        /// </summary>
        Task<T?> WaitForAsync<T>(Func<DevHostEvent, T?> selector, TimeSpan timeout)
            where T : class;

        /// <summary>Subscribe to live log lines. Dispose the returned token to unsubscribe.</summary>
        IDisposable SubscribeLogs(Action<LogLine> sink);

        /// <summary>The most recent captured log lines (bounded scrollback), oldest first.</summary>
        IReadOnlyList<LogLine> RecentLogs(int max = 500);
    }

    /// <summary>Topology entry for <see cref="IDevHostControl.ListBlocks" />.</summary>
    public sealed record BlockInfo(string Id, string Name, string TypeName, IReadOnlyList<string> ServiceIds);
}
