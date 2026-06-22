using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Threading;
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
        /// <summary>True while time-driven activity is paused (see <see cref="Pause" />).</summary>
        bool IsPaused { get; }

        /// <summary>
        ///     True when the registered <see cref="TimeProvider" /> is a controllable (fake) clock — one that
        ///     exposes a public <c>Advance(TimeSpan)</c> method, as <c>FakeTimeProvider</c> does. When true,
        ///     time-advancing helpers (e.g. <c>waitUntil</c> in scenarios) drive virtual time instead of waiting
        ///     on the real wall clock, making scenario execution deterministic and instant.
        /// </summary>
        bool IsStepped { get; }

        /// <summary>True when a supervisor capable of recycling the host is attached (see <see cref="TryRequestReset" />).</summary>
        bool CanReset { get; }

        /// <summary>The topology id the latest switch requested — read by the supervisor when the reset fires.</summary>
        string? RequestedTopology { get; }

        /// <summary>
        ///     The registered <see cref="TimeProvider" />'s current virtual time. Useful for tracking elapsed
        ///     virtual time during a <c>settle</c> loop. Returns the real UTC clock value when no
        ///     <c>FakeTimeProvider</c> is registered (e.g. on a non-stepped host).
        /// </summary>
        DateTimeOffset VirtualTimeUtc { get; }

        /// <summary>
        ///     True on a stepped host whose virtual clock has moved past the per-generation baseline captured
        ///     at construction — i.e. a prior <c>advance</c>/<c>step</c> or scenario run has dirtied this
        ///     generation, so it is no longer a clean slate. Always false on a non-stepped host (no virtual
        ///     clock to advance). Used to decide whether a scenario run can proceed in place or needs a recycle
        ///     for a reproducible result.
        /// </summary>
        bool HasAdvancedFromBaseline { get; }

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

        /// <summary>
        ///     Read the last-known value of a member qualified by its service identifier — the disambiguating
        ///     form for multi-service blocks (nested <c>[LogicBlockInterfaceBinding]</c> members), where the
        ///     flat per-block name map collapses duplicate member names last-service-wins. The service
        ///     identifier of a nested member equals the binding member name (RFC 0006 revision 5 name paths).
        /// </summary>
        object? GetProperty(string logicBlockIdOrName, string serviceIdentifier, string propertyName);

        /// <summary>All last-known service-property and measuring-point values for a logic block, keyed by member name.</summary>
        IReadOnlyDictionary<string, object?> GetAllProperties(string logicBlockIdOrName);

        /// <summary>
        ///     Write a writable <c>[ServiceProperty]</c> ("knob") — the programmatic equivalent of the UI's edit
        ///     field. The returned task completes once the value has been applied and re-published, so a
        ///     subsequent <see cref="GetProperty" /> reflects the new value (read-after-write is reliable; no
        ///     separate <see cref="WaitForAsync{T}" /> is needed for the property you just set). To observe a
        ///     <em>downstream</em> change instead, register <see cref="WaitForAsync{T}" /> <em>before</em> calling this.
        /// </summary>
        Task SetPropertyAsync(string logicBlockIdOrName, string propertyName, object? value);

        /// <summary>
        ///     Write a writable <c>[ServiceProperty]</c> qualified by its service identifier — the
        ///     disambiguating form for multi-service blocks, mirroring the three-argument
        ///     <see cref="GetProperty(string, string, string)" /> (RFC 0006 revision 5 name paths). Same ack
        ///     semantics as <see cref="SetPropertyAsync(string, string, object?)" />.
        /// </summary>
        Task SetPropertyAsync(string logicBlockIdOrName, string serviceIdentifier, string propertyName, object? value);

        /// <summary>
        ///     Write a service property addressed by its service identifier (the GUID from
        ///     <see cref="GetConfiguration" />), accepting either a CLR value or a JSON value (a
        ///     <c>JsonElement</c> / <c>JsonNode</c> is decoded against the property schema). This is the
        ///     addressing the web UI uses; in-process callers usually prefer
        ///     <see cref="SetPropertyAsync(string, string, object?)" />.
        ///     Like <see cref="SetPropertyAsync(string, string, object?)" />, the task completes once the value
        ///     has been applied and re-published.
        /// </summary>
        Task SetServicePropertyValueAsync(string serviceId, string propertyName, object? value);

        /// <summary>
        ///     Drive any <c>[ServiceProviderContractType]</c> value <em>input</em> contract (RFC 0010
        ///     <c>serviceProviderSet</c>): one path for digital, analog, and third-party struct contracts, with
        ///     no type-specific entry points. Addressed by the generic stand-in's actor name
        ///     (<paramref name="handlerName" />, the contract's <c>ContractHandlerActorName</c>) plus the mocked
        ///     endpoint ids, with the wire value as JSON (a scalar for digital/analog, an object for a struct
        ///     contract). The stand-in builds the exact contract message via its codec and forwards it to the
        ///     linked logic blocks, deterministically under stepping (one hop, the contract plane — never a
        ///     service-property setter). The web UI's manual HAL controls call this with the value they build
        ///     from the rendered control (a bool for a toggle, a number for a field).
        /// </summary>
        Task DriveServiceProviderContractAsync(string handlerName, string serviceProviderId, string serviceId, string contractId, JsonElement value);

        /// <summary>
        ///     Read the last value a logic block wrote on any <c>[ServiceProviderContractType]</c> value
        ///     <em>output</em> contract (RFC 0010 <c>serviceProviderExpect</c>) — one path for the four HAL
        ///     outputs and third-party scalar ones. Returns the captured value projected to a comparable scalar
        ///     (bool, double, string), or <c>null</c> if the output has never been Set.
        /// </summary>
        object? GetServiceProviderOutput(string serviceProviderId, string serviceId, string contractId);

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
        ///     Cancelling <paramref name="cancellationToken" /> also resolves <c>null</c> (and releases the
        ///     waiter immediately — callers that stop caring early should cancel rather than abandon).
        /// </summary>
        Task<T?> WaitForAsync<T>(Func<DevHostEvent, T?> selector, TimeSpan timeout, CancellationToken cancellationToken = default)
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

        /// <summary>
        ///     Pause time-driven activity: NEW <c>[Timer]</c> ticks and <c>InvokeSynchronizedAfter</c>
        ///     callbacks are held in a queue (already-scheduled fires still deliver, so each timer may tick
        ///     at most once more). Message processing — property sets, contract messages — continues: the
        ///     world stands still but remains pokeable. Wall-clock keeps running; blocks computing from the
        ///     current time observe the gap.
        /// </summary>
        void Pause();

        /// <summary>Resume: replay every held schedule, in order, with its original delay — self-rescheduling chains survive.</summary>
        void Resume();

        /// <summary>
        ///     Deterministic <em>next-event</em> virtual-time stepping. Advances the registered
        ///     <c>FakeTimeProvider</c> by <paramref name="virtualTime" /> of simulated time, firing every
        ///     <c>[Timer]</c> / <c>InvokeSynchronizedAfter</c> event due within it the correct number of times,
        ///     at the right virtual times, with no drift. Internally it advances to each next scheduled event
        ///     (read from the engine's virtual schedule) and waits for the actor system to quiesce before the
        ///     next advance, so an author can simply say "advance 5 virtual seconds" without knowing any timer
        ///     rate — a <c>[Timer(1)]</c> fires 5× under <c>AdvanceAsync(5s)</c>, and self-rescheduling /
        ///     dynamic-delay timers fire correctly too.
        ///     <para>
        ///         Quiescence is the EXACT predicate <c>Σ MailboxDepth == 0 AND InFlight == 0</c>: every
        ///         mailbox empty AND no user handler currently executing. Each event boundary therefore lands
        ///         on a settled, reproducible state, so a given (block set, budget) yields the same result
        ///         run-to-run.
        ///     </para>
        ///     <para>
        ///         Requires a <c>FakeTimeProvider</c> registered as the <see cref="TimeProvider" /> (throws
        ///         <see cref="InvalidOperationException" /> on a real clock — stepping a wall clock by hand is
        ///         meaningless). A system that never settles surfaces as a thrown
        ///         <see cref="TimeoutException" />, bounded by a generous real-clock safety budget — never an
        ///         infinite wait.
        ///     </para>
        /// </summary>
        Task AdvanceAsync(TimeSpan virtualTime, CancellationToken cancellationToken = default);

        /// <summary>
        ///     Advance to the single next scheduled event (one event hop) and wait for quiescence. When nothing
        ///     is scheduled, waits for the system to be idle and returns without moving the clock. The
        ///     primitive that condition-based <c>waitUntil</c> / <c>settle</c> helpers build on. Same clock
        ///     requirement and failure modes as <see cref="AdvanceAsync(TimeSpan, CancellationToken)" />.
        /// </summary>
        Task AdvanceToNextEventAsync(CancellationToken cancellationToken = default);

        /// <summary>
        ///     Ask the supervisor to recycle the host (dispose → rebuild → restart; the kill-and-`dale dev`
        ///     loop without the kill). Returns false when the host was started without a factory
        ///     (<c>DevHostWebRunner.RunAsync(hostFactory, …)</c>) — nothing can rebuild it.
        /// </summary>
        bool TryRequestReset();

        /// <summary>Supervisor hook: attach the reset handler. Dispose the token to detach.</summary>
        IDisposable OnResetRequested(Action handler);

        /// <summary>
        ///     Request a recycle into a different topology (RFC 0006 R5) — rides the reset signal; a
        ///     topology-aware supervisor (<c>DevHostWebRunner.RunAsync(Func&lt;string?, IDevHost&gt;, …)</c>)
        ///     builds the next generation from the requested id. Returns false when no supervisor is
        ///     attached.
        /// </summary>
        bool TryRequestTopologySwitch(string topologyId);
    }

    /// <summary>Topology entry for <see cref="IDevHostControl.ListLogicBlocks" />.</summary>
    public sealed record LogicBlockInfo(string Id, string Name, string TypeName, IReadOnlyList<string> ServiceIds);
}