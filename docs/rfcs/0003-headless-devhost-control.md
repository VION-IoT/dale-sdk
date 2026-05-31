# RFC 0003: Headless, scriptable control surface for DevHost

Status: **Draft** — design-only, not implemented. Author: jonas.bertsch. Date: 2026-05-29.
Revision 2: recentred on the in-process API after a consistency review against the `dale` CLI; the
HTTP/CLI tier shrank from a new endpoint + `--headless` flag to "extend the existing `/api` + an env var".
Revision 3: added **log streaming** as a first-class v1 capability (it was missing — events ≠ logs, and the
console stream is the core agent-debugging surface); promoted the determinism non-goal to an explicit,
named trade-off against the requester's stated "key enabler".
Revision 4: added a doc-ready **Example usage** section (CI integration test, interactive agent `/api` session,
MCP-tool mapping) that doubles as an implementer checklist; added open question 7 (expose the message tap over
`/api`); made the set-knob calls consistently `SetPropertyAsync`.

## Motivation

There are two testing tiers today and a gap between them:

- **TestKit** ([Vion.Dale.Sdk.TestKit](../../Vion.Dale.Sdk.TestKit)) — excellent for a single SUT: inject collaborator
  responses by hand (`HandleResponse`), drive virtual time (`AdvanceTime` + `FakeTimeProvider`, RFC 0001),
  assert outbound messages (`VerifySendCommand` / `VerifySendRequest`). But it is *not* a multi-block runtime —
  every collaborator is stubbed; real blocks are never wired together.
- **DevHost** ([Vion.Dale.DevHost](../../Vion.Dale.DevHost)) — boots the real multi-block network
  (`DevConfigurationBuilder` + DI + `AutoConnect`) with actual inter-block messaging. But the only
  control/observation surface is the JS web UI: a human reads probes and clicks knobs.

A bug that manifests **only in the wired, real-messaging, multi-block path** can be debugged only by a person
clicking through the UI. The motivating case: an orchestrator block polls N device blocks each control cycle
(`SendRequest` → `DataResponse`) and allocates. Every unit/integration test passed — because they inject the
`DataResponse`s directly via `HandleResponse`. In the real DevHost closed loop nothing was allocated: the
orchestrator's outbound poll was an unimplemented stub, so it never asked for the measurements the tests were
hand-delivering. **No single-SUT test can catch this** — the tests supply the very responses the missing poll
was meant to fetch. It was found only by booting DevHost and reading the console by hand.

This RFC proposes a headless, scriptable tier: boot the real network, set inputs, let the loop run, observe
state and inter-block messages — primarily from C# (CI / agent-authored tests), secondarily over the localhost
HTTP surface DevHost already exposes.

## What already exists (and what doesn't)

| Capability | State today | Cite |
|---|---|---|
| Boot wired network without web server | ✅ `.WithWebUi()` is an *optional* extension; the core boot path has no web dependency | [DevHostBuilderExtensions.cs:8](../../Vion.Dale.DevHost.Web/DevHostBuilderExtensions.cs#L8) |
| Set `[ServiceProperty]` / digital / analog input | ✅ Implemented on `IActorSystem`, no ASP.NET dependency in the logic | [DevHostStateProvider.cs:186-223](../../Vion.Dale.DevHost.Web/Services/DevHostStateProvider.cs#L186) |
| Read **topology / schema** | ✅ `GetConfigurationAsync()` returns blocks, services, property schemas, links | [DevHostStateProvider.cs:43](../../Vion.Dale.DevHost.Web/Services/DevHostStateProvider.cs#L43) |
| Read **live property values** | ❌ **No pull API.** The UI learns values only by subscribing to the push stream (`PublishAllStatesAsync` → `IDevHostEvents` → SignalR). Values live in the mock handlers' dictionaries. | [IDevHostStateProvider.cs](../../Vion.Dale.DevHost.Web/Services/IDevHostStateProvider.cs), [IDevHostEvents.cs](../../Vion.Dale.DevHost/IDevHostEvents.cs) |
| State-change event stream | ✅ `IDevHostEvents` exposes 6 change events (ServiceProperty, MeasuringPoint, DI/DO, AI/AO) | [IDevHostEvents.cs:8](../../Vion.Dale.DevHost/IDevHostEvents.cs#L8) |
| Stream the **log** output | ❌ **Not programmatic.** Logging is standard `ILogger` → console (`AddConsole`); there is no sink to subscribe to. `IDevHostEvents` carries *state changes*, not log lines — they are different streams. | [DevHostBuilder.cs](../../Vion.Dale.DevHost/DevHostBuilder.cs) |
| Existing localhost HTTP `/api` | ✅ REST controller (configuration + set property/DI/AI) + SignalR push — already the machine surface behind the SPA | [DevHostController.cs](../../Vion.Dale.DevHost.Web/Api/Controllers/DevHostController.cs) |
| Reach the control surface in-process | ❌ `IDevHost` exposes only `Start/Run/Stop`; `DevHost` is `internal` and never surfaces its `IServiceProvider`. `IDevHostStateProvider` is registered only by `.WithWebUi()`. | [IDevHost.cs](../../Vion.Dale.DevHost/IDevHost.cs), [DevHost.cs:12](../../Vion.Dale.DevHost/DevHost.cs#L12) |
| Deterministic time stepping | ❌ DevHost runs wall-clock; `RunAsync` is `Task.Delay(Infinite)`. No `TimeProvider` injection, no scheduler hook. | [DevHost.cs:78](../../Vion.Dale.DevHost/DevHost.cs#L78) |
| Tap inter-block messages | ❌ `IDevHostEvents` carries *state changes*, not commands/requests/responses between blocks | — |

**The request's premise "the UI backend already does get/set/step" is only one-third right**: set is reusable;
get-live-value is a *push stream*, not a pull read; step (time control) does not exist at all.

## Consistency with the `dale` CLI (drives the design)

This section is new in revision 2 and is the reason the design changed. The `dale` CLI has two deliberate,
documented design decisions ([Vion.Dale.Cli/CLAUDE.md](../../Vion.Dale.Cli/CLAUDE.md)) that an agent/automation
feature must respect:

1. **`-o json` is the agent surface.** `--output`/`-o` is a *global* option
   ([Program.cs:36](../../Vion.Dale.Cli/Program.cs#L36), driving `DaleConsole.JsonMode` at
   [Program.cs:89](../../Vion.Dale.Cli/Program.cs#L89)); CLAUDE.md states *"JSON mode … makes the CLI
   agent-friendly."* The established agent model is **stateless command → structured JSON on stdout → exit**,
   not a long-lived HTTP server.
2. **"No SDK dependency. The CLI operates on files and processes."** Every command shells out:
   `dale dev` → `dotnet run` on the consumer's project ([DevCommand.cs:35](../../Vion.Dale.Cli/Commands/DevCommand.cs#L35)),
   `dale test` → `dotnet test` ([TestCommand.cs:24](../../Vion.Dale.Cli/Commands/TestCommand.cs#L24)).

**The structural mismatch:** the CLI is *stateless-command*-oriented; driving a live network is inherently
*stateful-session*-oriented. `dale dev` is the only long-running command and it is a dumb passthrough — a
stateless `dale dev set-property …` could never reach state living inside a *different* running process. So the
only two CLI-consistent bridges to a live network are:

- **(a) an in-process API consumed in a test**, run via `dale test` → `dotnet test` — stateless from the CLI's
  point of view, and the parallel-safe, port-free, readiness-race-free option; and
- **(b) talking to the already-running `dale dev` process out-of-band over the HTTP `/api` the SPA already uses.**

Revision 1 proposed a *third* thing — a new `.WithControlEndpoint()` plus a `dale dev --headless` flag — which is
less consistent than either and carries a leaky-flag problem (below). Revision 2 drops it in favour of (a) as the
primary path and a minimal extension of the existing `/api` for (b).

### Why not a `dale dev --headless` flag

`dale dev` shells out to the **consumer-owned** `Program.cs` (generated from the template), which hard-codes
`.WithWebUi()` + `OpenBrowser()` ([template Program.cs:22-34](../../templates/vion-iot-library/VionIotLibraryTemplate.DevHost/Program.cs#L22)).
A `--headless` flag's *actual effect* therefore lives in that generated file — so it would **silently no-op on
every project generated before this ships**, until the consumer regenerates or hand-edits. A CLI flag decoupled
from its effect across both a process boundary and a codegen-version boundary is a confusing mental model. We
avoid it.

## The determinism trade-off (an explicit "no" to a stated key requirement)

The request names deterministic stepping — "inject a `TimeProvider` and `AdvanceTime(delta)` … as TestKit
already does" — as "**the key enabler for CI**." v1 does **not** deliver it in that form, and this is the single
biggest divergence from the request, so it is called out here rather than buried in Out of scope.

**Why the TestKit model does not port.** TestKit's `AdvanceTime` is deterministic because the whole context is
single-threaded and synchronous — `FlushPendingActions` drains a queue on the test thread in deadline order
([LogicBlockTestContext.cs:279](../../Vion.Dale.Sdk.TestKit/LogicBlockTestContext.cs#L279)). DevHost runs the
real Proto.Actor system on real threads. Injecting a `FakeTimeProvider` makes a block's *clock reads*
deterministic, but the **scheduling of messages between actors stays asynchronous across threads** — so
"`AdvanceTime(5s)`, then assert immediately" cannot hold: the messages that 5 s was supposed to provoke may not
have been delivered yet. Making it hold would require driving the entire actor system off a virtual scheduler —
a deep runtime change, not a TestKit-style shim.

**What v1 gives instead — condition-based waiting.** `WaitForAsync(predicate, timeout)` against a network on
wall-clock. This yields *reproducible* tests (the assertion is "X happened within N seconds"), not
*bit-deterministic* ones (instant, fixed-order replay).

| | TestKit (single-SUT) | DevHost v1 (multi-block) |
|---|---|---|
| Time model | virtual, synchronous `AdvanceTime` | wall-clock + `WaitForAsync(timeout)` |
| Run speed | instant | real elapsed time (bounded by timeout) |
| Flake risk | none | timeout-tuning sensitivity |
| Catches the motivating bug? | n/a (can't wire blocks) | **yes** — set trigger → WaitFor → assert `RecordedMessages("device-x")` |

**Partial mitigation.** Blocks that take `TimeProvider` in their ctor can still be handed a *controllable* clock
for their **reads** (e.g. to exercise a "every 15 min" branch without waiting 15 min), even though cross-actor
*scheduling* remains wall-clock. So time-of-day / interval logic is testable; what's not deterministic is the
end-to-end message-delivery ordering.

**Follow-on trigger.** If fast bit-deterministic replay becomes load-bearing (large suites where wall-clock
waits dominate, or flaky-timeout pain), that justifies its own RFC to run DevHost on a virtual scheduler. Out of
scope here.

## Proposed design

### Primary: in-process control API (CI and agent-authored tests)

This is the centerpiece and the most CLI-consistent piece — it adds **zero** CLI surface and runs under the
existing `dale test` / `dotnet test`. A control facade, defined in **core `Vion.Dale.DevHost`** (not `.Web`),
reachable from the host.

**Surface the facade from `IDevHost`:**

```csharp
public interface IDevHost
{
    Task StartAsync(CancellationToken ct = default);
    Task RunAsync(CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);

    IDevHostControl Control { get; }   // NEW — non-null after StartAsync
}
```

**The control interface** (lives in core; the set-logic moves down from `.Web`, which only *registers* it today
— the logic already depends on `IActorSystem`, not Kestrel):

```csharp
public interface IDevHostControl
{
    // Topology / introspection
    IReadOnlyList<BlockInfo> ListBlocks();
    ConfigurationOutput GetConfiguration();

    // Read live values — backed by a cache that subscribes to IDevHostEvents,
    // i.e. exactly how the web UI already learns values today.
    object? GetProperty(string blockId, string propertyName);
    IReadOnlyDictionary<string, object?> GetAllProperties(string blockId);

    // Write knobs / inputs (extraction of the existing DevHostStateProvider set-path)
    Task SetPropertyAsync(string blockId, string propertyName, object value);
    Task SetDigitalInputAsync(string spId, string svcId, string contractId, bool value);
    Task SetAnalogInputAsync(string spId, string svcId, string contractId, double value);

    // Observe state — reuse IDevHostEvents; condition-based waiting (not synchronous time stepping)
    IDisposable Subscribe(Action<DevHostEvent> sink);
    Task<T> WaitForAsync<T>(Func<DevHostEvent, T?> selector, TimeSpan timeout) where T : class;

    // Observe logs — the programmatic equivalent of reading the DevHost console by hand (see below).
    // Live subscription plus a bounded scrollback buffer so an agent connecting after StartAsync
    // still sees what already happened.
    IDisposable SubscribeLogs(Action<LogLine> sink);
    IReadOnlyList<LogLine> RecentLogs(int max = 500);

    // Message tap — the multi-block analogue of TestKit's Verify*; see below
    IReadOnlyList<TappedMessage> RecordedMessages(string? blockId = null);
}
```

**The missing live-value read** is a small `LastKnownStateCache` that subscribes to `IDevHostEvents` at boot and
records the latest value per `(blockId, propertyName)` — honest, because it returns exactly what the UI shows
(both are fed by the same stream). `PublishAllStatesAsync()` is invoked once on start to warm it.

**Message tap.** The motivating bug is "an outbound poll was never *sent*" — a state read can't show that
directly, but a tap on inter-block messages can ("assert device-x received a `DataRequest` this run"). This is
net-new (the event stream carries only state changes) and requires an interception point in the Dev actor wiring
/ mock handlers; it is the single highest-yield diagnostic for the motivating scenario, so it is in the primary
surface rather than deferred. (If the interception seam turns out non-trivial, it can split into its own slice —
see open question 3.)

**Log streaming.** The motivating workflow was literally "boot DevHost and read the console by hand", so the
**log stream is the core agent-debugging surface** — and it is a *different* stream from `IDevHostEvents`
(state changes). DevHost logs through `ILogger` → console today with no programmatic sink. v1 adds a custom
`ILoggerProvider` that the host registers unconditionally into the logging pipeline; it feeds `SubscribeLogs`
(live) and a bounded ring buffer behind `RecentLogs` (scrollback, so an agent that attaches after `StartAsync`
still sees the boot + early-cycle logs). `LogLine` carries level, category, timestamp, message, and any
exception — structured, so an agent can filter (e.g. warnings/errors only) without scraping console text. This
is what lets an agent reproduce the original "eyeball the console" debugging session programmatically.

**Illustrative C# integration test (runs under `dale test`):**

```csharp
await using var host = DevHostBuilder.Create()
    .WithDi<MyDi>().WithConfiguration(config)
    .Build();                       // open question 1 — Control is always wired, no BuildHeadless()
await host.StartAsync();

await host.Control.SetPropertyAsync("consumer-a", "RequestedCurrentA", 16.0);    // set a knob

var budgets = await host.Control.WaitForAsync(                                    // observe (condition-based)
    e => e is ServicePropertyChanged { BlockId: "energy-manager", Property: "LastBudgets" } sp ? sp.Value : null,
    timeout: TimeSpan.FromSeconds(5));

// budgets != null  AND  host.Control.RecordedMessages("device-x") non-empty  ⇒  the poll actually fired.
// Full xUnit / MSTest forms + an interactive agent session: see "Example usage" below.
```

No port, no process, no readiness race, parallel-safe — which is exactly why this, not HTTP, is the CI path.

### Secondary: extend the existing `/api` for live/interactive use

For external tools, agents driving a *running* network, and the "commissioning rehearsal — script drives while a
human watches the UI" scenario, reuse the HTTP surface DevHost **already** exposes rather than invent a new one:

- **Add GET-state routes to the existing [`DevHostController`](../../Vion.Dale.DevHost.Web/Api/Controllers/DevHostController.cs)**
  (`GET /api/state`, `GET /api/state/{blockId}/{property}`), backed by the same `LastKnownStateCache`. The set
  routes and configuration route already exist. This ships in the `Vion.Dale.DevHost.Web` package, so consumers
  get it by **upgrading the package — no `Program.cs` change**.
- **Add a log stream over HTTP** — `GET /api/logs` as SSE (live tail) plus `GET /api/logs/recent` (scrollback),
  fed by the same `ILoggerProvider` sink as the in-process `SubscribeLogs`. This is the agent's remote
  equivalent of the console an interactive `dale dev` user reads. Also ships in `.Web`, no `Program.cs` change.
- **Expose the message tap** — `GET /api/messages?block={blockId}` (and unfiltered `GET /api/messages`), fed by
  the same tap as the in-process `RecordedMessages`. This is what lets the interactive agent path *root-cause*
  message bugs (assert "device-x received no `DataRequest`"), not just observe symptoms — see example B below.
  Ships in `.Web`, no `Program.cs` change.
- **Browser suppression** is the one thing that needs consumer cooperation, because `OpenBrowser()` lives in the
  consumer's `Program.cs` ([template Program.cs:46](../../templates/vion-iot-library/VionIotLibraryTemplate.DevHost/Program.cs#L46)),
  not the SDK. Solve it with an **env var the template's `Program.cs` reads** (`DALE_DEVHOST_NO_BROWSER=1`):
  `DALE_DEVHOST_NO_BROWSER=1 dale dev` boots the network, serves `/api`, opens no browser. On an
  already-generated project the env var is simply ignored (browser still opens) — graceful degradation, no
  silent-wrong-behaviour.
- **The web server itself stays.** "No web server at all" (from the request) is treated as a non-requirement: the
  SPA is static files + cheap middleware; the real pains are the browser popup (solved above) and overhead
  (negligible). Reusing one HTTP surface for human + machine is *more* consistent and directly serves the
  watch-while-scripting scenario.
- **No new `dale dev` flag is load-bearing.** The CLI can stay dumb (env var + existing passthrough). A
  `--headless` flag could later be added purely as sugar that sets `DALE_DEVHOST_NO_BROWSER` for the spawned
  process, but it is optional and explicitly not the mechanism.

## Backwards compatibility

- **`IDevHost` gains a member** (`Control`). It is implemented only by the internal `DevHost`, so this is not a
  breaking change for external implementers (there are none). `Start/Run/Stop` callers are unaffected.
- **Moving `IDevHostStateProvider`'s set-logic from `.Web` into core**: the type stays public; `.Web` keeps a
  thin registration. Existing `.WithWebUi()` callers are unaffected.
- **GET-state and log routes** ship in `.Web` — consumers get them on package upgrade, no `Program.cs` change.
- **The log `ILoggerProvider` sink** is additive: the host registers it into the existing logging pipeline
  alongside `AddConsole` (console output is unchanged; the sink just also feeds `SubscribeLogs` / the buffer).
  In-process callers get it via `IDevHostControl`; the HTTP routes get it in `.Web`.
- **`DALE_DEVHOST_NO_BROWSER`** needs a template `Program.cs` change, but **degrades gracefully**: an
  already-generated project ignores the env var (browser still opens). No silent no-op of a CLI flag — there is
  no flag. Call out in release notes that headless-no-browser requires the new template (regenerate or hand-edit).
- **No change to TestKit, the web UI, runtime, or contract surfaces.**

## Interaction with TestKit (RFC 0001 / 0002)

This is the **multi-block** complement to TestKit's **single-SUT** harness — not a replacement. `WaitForAsync` /
`RecordedMessages` are the live-network analogues of `AdvanceTime` / `VerifySend*`, but deliberately use
condition-based waiting rather than synchronous virtual time because the substrate is a real threaded actor
system. No shared base class is proposed; the models are intentionally different.

## Example usage

These are illustrative (nothing is implemented yet), but every call below maps to a member of `IDevHostControl`
or an `/api` route defined above — they double as an **implementer checklist** (the surface must make these
compile / resolve) and as **draft documentation** for the docs repo, `dale dev --help`, and a future MCP server.

### A. CI / regression — in-process integration test (the motivating bug, as a permanent guard)

xUnit:

```csharp
public class WiredLoopTests
{
    private readonly ITestOutputHelper _output;
    public WiredLoopTests(ITestOutputHelper output) => _output = output;

    [Fact]
    public async Task EnergyManager_polls_devices_and_allocates_budgets()
    {
        // Same wired network dale dev boots — built via DevConfigurationBuilder (AddLogicBlock + AutoConnect).
        await using var host = DevHostBuilder.Create()
            .WithDi<MyDi>().WithConfiguration(config)
            .Build();
        await host.StartAsync();

        await host.Control.SetPropertyAsync("consumer-a", "RequestedCurrentA", 16.0);

        var budgets = await host.Control.WaitForAsync(
            e => e is ServicePropertyChanged { BlockId: "energy-manager", Property: "LastBudgets" } sp ? sp.Value : null,
            timeout: TimeSpan.FromSeconds(5));

        if (budgets is null)                                  // on failure: the console, programmatically
            foreach (var line in host.Control.RecentLogs())
                _output.WriteLine(line.ToString());

        Assert.NotNull(budgets);                             // the loop produced budgets
        Assert.NotEmpty(host.Control.RecordedMessages("device-x"));   // ← the poll was actually issued (catches the stub)
    }
}
```

MSTest is the same body; swap the framework layer only: ctor-injected `ITestOutputHelper` → `TestContext`,
`Assert.NotNull` → `Assert.IsNotNull`, `Assert.NotEmpty(...)` → `CollectionAssert.IsNotEmpty(...)`. The
`host.Control.*` surface is framework-neutral.

### B. Interactive debugging — an agent driving the live network over `/api`

The agent's version of "click through the UI and eyeball the console". Spawns `dale dev` headless, then drives
the same `/api` the SPA uses. (A future MCP server wraps exactly these calls — see the mapping in C.)

```jsonc
// 1. Boot headless; block until the host signals readiness (open question 4).
$ DALE_DEVHOST_NO_BROWSER=1 dale dev
   … {"ready": true, "port": 5000}

// 2. Learn the topology — block ids + property names.
GET /api/configuration
   → { "blocks": ["energy-manager", "consumer-a", "device-x"],
       "energy-manager": { "properties": ["LastBudgets", ...] }, ... }

// 3. Drive the knob (existing route).
POST /api/dale/property/consumer-a/RequestedCurrentA   { "value": 16.0 }
   → 200 OK

// 4. Read live state (new GET-state route) — poll on wall-clock (no synchronous step over HTTP).
GET /api/state/energy-manager/LastBudgets
   → {}            // still empty after several seconds — symptom reproduced

// 5. Tail the logs (new log route) — the machine-readable console.
GET /api/logs?level=Debug         // SSE stream
   → consumer-a RequestedCurrentA = 16
   → (… nothing from energy-manager about polling device-x …)   // the smoking gun: the poll never logs

// 6. Confirm the root cause — the message tap.
//    If open question 7 is accepted, directly over HTTP:
GET /api/messages?block=device-x
   → []            // device-x received no DataRequest → the outbound poll is a stub
//    Otherwise, drop to the in-process test in (A): RecordedMessages("device-x") is in-process only today.
```

The two modes compose, and that composition *is* the workflow: **HTTP to explore** ("what's wrong?"),
**in-process test to confirm + lock in CI** ("prove it, keep it"). Example A is the regression guard you leave
behind after example B finds the bug.

### C. The `/api` surface as future MCP tools

The deferred MCP server (Out of scope) is a thin wrapper over the routes in B — listed here so the HTTP design
is verifiably MCP-shaped:

| MCP tool | `/api` route | `IDevHostControl` equivalent |
|---|---|---|
| `list_blocks` | `GET /api/configuration` | `ListBlocks()` / `GetConfiguration()` |
| `get_state` | `GET /api/state/{block}/{prop}` | `GetProperty(block, prop)` |
| `set_property` | `POST /api/dale/property/{svc}/{prop}` | `SetPropertyAsync(...)` |
| `read_log` | `GET /api/logs` (SSE) / `/api/logs/recent` | `SubscribeLogs(...)` / `RecentLogs(...)` |
| `recorded_messages` | `GET /api/messages?block=` *(open question 7)* | `RecordedMessages(block)` |

`step` is deliberately absent — there is no synchronous time step (see the determinism trade-off); an agent waits
on state/logs over wall-clock, exactly as in B.

## Open questions

1. **`BuildHeadless()` vs always-wire.** (Lean, strengthened in rev 2: **always wire `Control`**, drop
   `BuildHeadless()`. `.WithWebUi()` just adds the SPA + `/api` on top. The control facade is cheap and useful
   even alongside the UI — e.g. a test that drives a network a human is also watching.)
2. **Live-value cache vs true live read.** The event-cache returns last-*published* values, which can lag a value
   that changed but wasn't published. (Lean: cache for v1; revisit only if a consumer hits the lag.)
3. **Message-tap interception seam.** Where does the tap hook in — the Dev mock handlers, or a wrapping
   `IActorContext`? Needs a spike before committing it to the v1 primary surface; may split into its own slice.
4. **HTTP readiness signal.** `dale dev` prints the URL *before* the process is actually listening
   ([DevCommand.cs:31](../../Vion.Dale.Cli/Commands/DevCommand.cs#L31)), so an agent polling `/api` races
   startup. Do we emit a machine-readable readiness line on stdout (e.g. `{"ready":true,"port":5000}`, matching
   the `-o json` convention) when no-browser mode is active? (Lean: yes — it's cheap and it's the only safe way
   for an external agent to know when to connect. In-process callers don't need it; `StartAsync` awaits readiness.)
5. **Parallel runs / port.** The web `/api` is fixed at 5000, so parallel `dale dev` instances collide — fine for
   a single human, wrong for parallel CI over HTTP. This is a *positive* argument for steering CI to the
   in-process API (no port). Should the secondary HTTP path support an ephemeral/configurable port + readiness
   port-report for the rare parallel-HTTP case, or do we simply document "HTTP is single-instance; use the
   in-process API for parallel CI"? (Lean: document the limitation; don't build ephemeral-port plumbing in v1.)
6. **Result acknowledgement.** `SetPropertyAsync` is fire-and-forget into the actor system today. (Lean: the
   `WaitForAsync` pattern on the resulting change event is sufficient; don't add acks.)
7. **Expose the message tap over `/api`?** **Resolved — accepted.** `GET /api/messages?block={blockId}` is in
   the secondary HTTP design above, fed by the same tap as the in-process `RecordedMessages`, so the interactive
   agent path can root-cause *message* bugs (assert "no `DataRequest` reached device-x"), not just observe
   symptoms. Still contingent on the tap seam (open question 3) being clean; if that seam proves expensive, the
   in-process `RecordedMessages` ships first and the HTTP route follows.

## Out of scope

- **MCP server.** Deferred. If an interactive agent transport is later wanted, **stdio JSON-RPC** fits the CLI's
  "structured JSON on stdout" instinct far better than HTTP+SSE, avoids the port/readiness/parallel problems
  entirely (the agent owns the child process and its pipes), and is the natural MCP transport — so the future
  direction is a `dale dev`-spawned stdio-RPC session, not a bespoke HTTP/SSE channel. Not built in v1; the
  extended `/api` covers the near-term external-tool need.
- **Deterministic virtual-time stepping across the live actor system.** Its own RFC if a real need appears; v1
  uses condition-based waiting.
- **Action / trigger invocation beyond writable properties.** Mostly already covered: a UI "trigger" is a
  writable `bool` property the dashboard commits `true` to on click (DALE023), so `SetPropertyAsync(block,
  trigger, true)` invokes it. Genuinely distinct *actions* (non-property side-effects) are deferred until the
  property/input loop is proven.
- **Auth / remote exposure.** Localhost dev-only in v1, as the request agrees.
- **Sending stop messages to blocks on shutdown.** Pre-existing TODO ([DevHost.cs:108](../../Vion.Dale.DevHost/DevHost.cs#L108)),
  orthogonal to this RFC.
