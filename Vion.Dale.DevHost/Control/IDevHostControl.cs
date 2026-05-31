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
