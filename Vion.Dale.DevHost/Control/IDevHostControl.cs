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
    ///         Covers topology, reading/writing state (service properties and measuring points), driving inputs,
    ///         observing the state-change stream, the inter-block message tap, and the log stream.
    ///     </para>
    /// </summary>
    public interface IDevHostControl
    {
        /// <summary>The logic blocks in the wired network, with their ids, names, type, and service identifiers.</summary>
        IReadOnlyList<LogicBlockInfo> ListLogicBlocks();

        /// <summary>
        ///     The full introspection of the wired network — services, property/measuring-point schemas,
        ///     presentation, contracts, and wiring. The heavyweight view the web UI renders; <see cref="ListLogicBlocks" />
        ///     is the lightweight topology.
        /// </summary>
        ConfigurationOutput GetConfiguration();

        /// <summary>
        ///     Read the last-known value of a logic block's <c>[ServiceProperty]</c> or
        ///     <c>[ServiceMeasuringPoint]</c>, keyed by the logic block name (assigned in <c>AddLogicBlock(name:)</c>)
        ///     or its id, plus the member name. Returns <c>null</c> if the member is unknown or hasn't produced a
        ///     value yet. This is the last <em>published</em> value — exactly what the web UI shows (RFC 0003).
        /// </summary>
        object? GetProperty(string logicBlockIdOrName, string propertyName);

        /// <summary>All last-known service-property and measuring-point values for a logic block, keyed by member name.</summary>
        IReadOnlyDictionary<string, object?> GetAllProperties(string logicBlockIdOrName);

        /// <summary>
        ///     Write a writable <c>[ServiceProperty]</c> ("knob") — the programmatic equivalent of the UI's edit
        ///     field. The returned task completes once the value has been applied and re-published, so a
        ///     subsequent <see cref="GetProperty" /> reflects the new value (read-after-write is reliable; no
        ///     separate <see cref="WaitForAsync{T}" /> is needed for the property you just set). To observe a
        ///     <em>downstream</em> change instead, register <see cref="WaitForAsync{T}" /> <em>before</em> calling this.
        /// </summary>
        Task SetPropertyAsync(string logicBlockIdOrName, string propertyName, object value);

        /// <summary>
        ///     Write a service property addressed by its service identifier (the GUID from
        ///     <see cref="GetConfiguration" />), accepting either a CLR value or a JSON value (a
        ///     <c>JsonElement</c> / <c>JsonNode</c> is decoded against the property schema). This is the
        ///     addressing the web UI uses; in-process callers usually prefer <see cref="SetPropertyAsync" />.
        ///     Like <see cref="SetPropertyAsync" />, the task completes once the value has been applied and
        ///     re-published.
        /// </summary>
        Task SetServicePropertyValueAsync(string serviceId, string propertyName, object value);

        /// <summary>Set a mocked digital input value, routed to the linked logic blocks just like the web UI's HAL control.</summary>
        Task SetDigitalInputAsync(string serviceProviderId, string serviceId, string contractId, bool value);

        /// <summary>Set a mocked analog input value, routed to the linked logic blocks just like the web UI's HAL control.</summary>
        Task SetAnalogInputAsync(string serviceProviderId, string serviceId, string contractId, double value);

        /// <summary>
        ///     Ask every mock handler to re-publish its current state — used by the web UI on (re)connect to
        ///     prime a fresh client. In-process callers rarely need this (the value cache is already warm).
        /// </summary>
        void PublishAllStates();

        /// <summary>
        ///     Subscribe to the normalized state-change event stream (the projection of <see cref="IDevHostEvents" />).
        ///     Dispose the returned token to unsubscribe.
        /// </summary>
        IDisposable Subscribe(Action<DevHostEvent> sink);

        /// <summary>
        ///     Wait until an event satisfies <paramref name="selector" /> (returns non-null), or until
        ///     <paramref name="timeout" /> elapses. Returns the selector's value, or <c>null</c> on timeout —
        ///     condition-based waiting, the multi-block runtime's substitute for synchronous time stepping
        ///     (RFC 0003, "the determinism trade-off"). Observes only events that occur after the call.
        /// </summary>
        Task<T?> WaitForAsync<T>(Func<DevHostEvent, T?> selector, TimeSpan timeout)
            where T : class;

        /// <summary>
        ///     The inter-actor messages a logic block received this run (the message tap) — the multi-block
        ///     analogue of TestKit's <c>Verify*</c>. Pass a logic block name or id to filter, or null for all.
        ///     Lets a test assert e.g. "device-x received a request", the highest-yield diagnostic for a
        ///     missing-poll bug.
        /// </summary>
        IReadOnlyList<TappedMessage> RecordedMessages(string? logicBlockIdOrName = null);

        /// <summary>Subscribe to live log lines. Dispose the returned token to unsubscribe.</summary>
        IDisposable SubscribeLogs(Action<LogLine> sink);

        /// <summary>The most recent captured log lines (bounded scrollback), oldest first.</summary>
        IReadOnlyList<LogLine> RecentLogs(int max = 500);
    }

    /// <summary>Topology entry for <see cref="IDevHostControl.ListLogicBlocks" />.</summary>
    public sealed record LogicBlockInfo(string Id, string Name, string TypeName, IReadOnlyList<string> ServiceIds);
}