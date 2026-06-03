# RFC 0005: Logic-runtime observability — per-actor vitals, OTel export & a diagnostics block

Status: **Draft** — design-only, not implemented. Author: jonas.bertsch. Date: 2026-06-03.

> This RFC is the **measurement** counterpart to RFC 0004 (emission policy)'s **control** work; the two are deliberately separate (see [Relationship to RFC 0004](#relationship-to-rfc-0004-emission-policy)).

## Motivation

VION already has two mature observability planes, and **neither serves the integrator who authors logic blocks** when they need to understand runtime behaviour in production:

- **Plane A — OpenTelemetry** (`dale` → otel-collector → Loki/Tempo/Mimir → Grafana). Process-level (`dotnet.gc.*` / `dotnet.process.*`), MQTT spans with the high-frequency state topics deliberately excluded. Aimed at the **platform operator**. Nothing per-block.
- **Plane B — IoT data** (block → MQTT → mesh → RabbitMQ → TimescaleDB → dashboard). Measuring points, properties, component-health, sync-status, at a 1-min snapshot cadence (RFC/decision *measuring-point-snapshot-cadence*). Aimed at the **end-customer**. Product state, not runtime internals.

The integrator's only runtime-insight tools today are the **local** DevHost (`dale dev`, explicitly "not a substitute for the production cloud dashboard"), fleet-level Grafana viewer access, and `ssh + docker logs dale`. There is no per-block, production, integrator-scoped runtime view.

This is **not** a deficiency in the integrators' *domain* diagnostics: blocks legitimately instrument their own behaviour — `CycleDiagnostics`, `ResponseObserver`, `RegisterWriteTracker`, per-device `IsCommunicating()`/staleness, hardware-state FSMs — and where a pattern is reusable it is already factored out (e.g. `RegisterWriteTracker` in the library's `Common`). Those are domain-specific, well-placed, and stay where they are. The gap is one layer **below** them: the runtime/actor behaviour that no block can see no matter how well it instruments its own domain — and there the trouble scenarios are structurally invisible:

| Trouble | Why it's invisible today |
|---|---|
| Overload / backlog | Proto.Actor mailboxes are **unbounded**; no backpressure, no depth signal. |
| Slow handlers / latency | No per-message timing anywhere (only an internal ack `Stopwatch` logged at Debug). |
| Timeouts | Lifecycle timeouts are caught + logged; otherwise nothing. |
| Errors | `ActorMiddleware` **swallows** every handler exception, drops the message, actor continues — no count, no MQTT signal. |
| Restart loops | `RestartHandler` bounces the whole process; no per-block restart counter, no loop detection. |
| Floods | Publishers don't batch/coalesce; QoS0 retained → fast properties overwrite at the broker. |

This RFC adds a single **per-actor vitals core** in the SDK, exposed through **two sinks matched to two audiences**: OpenTelemetry (operator / 3rd-level support, fleet-bounded) and a pull-in **diagnostics logic block** (integrator + end-customer, dashboardable today via Plane B).

## Goals

- **One in-proc per-actor vitals core**, fed by seams that already exist, covering **both logic-block actors and the runtime's own actors** (the choke-points). `TimeProvider`-driven so the TestKit drives it deterministically.
- **Sink 1 — OTel**: per-actor metrics into the existing collector/Grafana, **cardinality-bounded by default** (block-type aggregates fleet-wide; per-instance detail opt-in per gateway).
- **Sink 2 — a diagnostics block**: a first-party published logic-block library the integrator drops into a configuration; emits a per-block diagnostics **table** (array-of-structs service property) + status + a runtime-health rollup, all on Plane B → TimescaleDB + dashboard with no new cloud work.
- **Minimal edge footprint**: cheap always-on scalars + sampling/opt-in for anything heavier.

## Non-goals (this RFC)

- **Rate-limiting / control of any kind** — emission throttling, input-side sampling, MQTT coalescing, bounded mailboxes. That is the **RFC 0004 family**; this RFC only *measures*. (See [Relationship to RFC 0004](#relationship-to-rfc-0004-emission-policy).)
- **Watchdog *policy*** — overrun ("ran too long") / starvation ("didn't run") semantics and alarming. This RFC defines the **seams** and ships the **raw** per-`[Timer]` vitals; the policy starts as a hand-rolled spike to be harvested later (see [Watchdog seams](#watchdog-seams--raw-now-policy-later)).
- **Absorbing integrators' domain diagnostics into the SDK** (read-health, write-tracking, response-latency, hardware-FSM). These are legitimate, often block-specific, and integrators already DRY the reusable ones within their own libraries (e.g. `RegisterWriteTracker` in `Common`). They are orthogonal to the runtime/actor layer this RFC targets — explicitly not a goal.
- **New cloud / dashboard infrastructure.** Reuse Plane A (OTel) and Plane B (measuring points / properties) as they are.
- **`wait`-vs-`process` latency split** — distinguishing mailbox-queue time from handler time needs an enqueue timestamp on messages; deferred (see [Future work](#future-work)).

## Background — verified facts that constrain the design

**All actors — logic blocks *and* runtime actors — share one middleware seam.** Every actor is spawned through `IActorSystem.CreateRootActorFromDi` (`Vion.Dale.ProtoActor/ActorSystem.cs:147-190`), which wires `ActorMiddleware.ReceiveMiddleware(logger, _messageObserver)` (`:186`). The runtime's `MqttClient` (`dale/Dale/Mqtt/MqttClient.cs:28`, `IActorReceiver`, spawned `Program.cs:79`) and every `IMqttHandlerActor` (spawned `Program.cs:169-183`) go through the **same** path as logic blocks. So one seam instruments everything.

**The observer seam exists and is unused in production.** `IActorMessageObserver` (`Vion.Dale.Sdk/Abstractions/IActorMessageObserver.cs`) is resolved from DI and called by the middleware (`ActorMiddleware.cs:18-28`); it is **null in the production runtime** (only DevHost registers one, for RFC 0003's message tap). Today it exposes only `OnReceived(actorName, message)` — *before* dispatch. Message **rate/type** are therefore already capturable; **handler duration + exceptions** require a small additive extension around `await next(...)` / the catch (`ActorMiddleware.cs:39-60`).

**Mailbox depth comes from Proto, not the middleware.** Proto.Actor emits native OpenTelemetry metrics under meter **`Proto.Actor`** (enabled by `AddProtoActorInstrumentation()`, which is equivalent to adding that meter name):

| Metric | Type | Labels |
|---|---|---|
| `protoactor_actor_mailbox_length` | Gauge | id, address, actortype |
| `protoactor_actor_messagereceive_duration` | Histogram | id, address, actortype, **messagetype** |
| `protoactor_actor_restarted_count` / `_failure_count` / `_spawn_count` / `_stopped_count` | Counter | id, address, actortype |
| `protoactor_deadletter_count` | Counter | id, address, messagetype |
| `protoactor_threadpool_latency_duration` | Histogram | id, address |
| `protoactor_future_started_count` / `_completed_count` / `_timedout_count` | Counter | id, address |

`id` is the per-instance actor name (the block name / handler name); `actortype` is the SDK's `Actor<TReceiver>` **wrapper** type, so it does **not** cleanly identify the logic-block class. The `messagereceive_duration` histogram carries both `id` and `messagetype` — the cardinality bomb (see [Sink 1](#sink-1--opentelemetry-operator--3rd-level-support)).

**Mailbox depth is observable in-proc via Proto mailbox statistics.** `IMailboxStatistics` (`MailboxStarted` / `MailboxEmpty` / `MessagePosted` / `MessageReceived`) attaches through `props.WithMailbox(() => UnboundedMailbox.Create(stats…))` — multiple stats are allowed, so ours coexists with Proto's own. Queue length is not directly readable; depth is derived as `MessagePosted − MessageReceived`, tracked atomically per actor. `BoundedMailbox` and dropping-tail/head variants exist (overflow → dead-letters).

**The export pipeline is a shared package with the right extension points.** `dale` wires OTel via `vion-telemetry`'s `AddVionTelemetryExport` (`dale/Dale/Program.cs:157`). Its metrics pipeline does `AddView("*", Drop)` + an explicit allow-list of 7 runtime instruments (`Vion.Telemetry.Export/ServiceCollectionExtensions.cs:99-106`), 60s export interval (`:127`). It already exposes two options — **`MeterNames`** and **`MetricViews`** (`VionTelemetryExportOptions`) — and a specific `AddView(name, config)` re-enables an instrument through the wildcard drop (exactly how the 7 runtime metrics survive). `MetricStreamConfiguration.TagKeys` is the per-instrument **label allow-list**. So both our meter and `"Proto.Actor"` plug in **purely via options at `Program.cs:157`** — no change to `vion-telemetry`.

**Blocks are constructed by DI.** `ActivatorUtilities.CreateInstance(_serviceProvider, …)` (`ActorSystem.cs:158/176/179`) resolves any registered service into a block's constructor — so a diagnostics block can constructor-inject a vitals-core singleton, exactly as blocks inject `TimeProvider` (`Vion.Dale.Sdk/ServiceCollectionExtensions.cs:19`).

**Actor topology is already enumerable.** `IActorSystem.FindByName(Regex)` / `LookupByName` (`ActorSystem.cs:280/287`) list actors by name — the basis for a filter without wiring.

**Configurations carry no per-block settings.** `SetLogicConfigurationPayload.LogicBlockInstance` (`vion-contracts/.../SetLogicConfigurationPayload.cs`) holds only `Id/PackageId/PackageVersion/TypeFullName/Name/Services`; wiring lives in separate `InterfaceMappings`/`ContractMappings`. There is **no author-time value/settings bag** — property values are runtime-set (dashboard property-set) and persisted. So a diagnostics filter is a runtime-set property, not a wire, and not an author-time config.

**Rich types support an array-of-structs property = a diagnostics table.** `[ServiceProperty]` accepts `ImmutableArray<T>` of a flat `readonly record struct` (rich-types design); the dashboard renders it as a `<StructArray>` **table** (one row per element, columns per struct field), read-only, with a per-row severity color via the status-enum `statusMappings`. `ResponseObserver` already does exactly this with `ImmutableArray<ConsumerResponseLatencyStats>`.

## Architecture — one readable core, two sinks

```
                         ┌───────────────────────── Vitals core (SDK, in-proc) ──────────────────────────┐
  every actor's messages │  thread-safe per-actor aggregate, keyed by actor identity                      │
  (blocks + runtime)     │   • logic-block actors  → { block.type, block.id, library }                    │
        │                │   • runtime actors      → { kind=runtime, role=MqttClient|ServicePropertyHandler… } │
        ▼                │  signals: msg rate, handler duration, error count, mailbox depth, restarts,    │
  ActorMiddleware ──────▶│           deadletters, per-[Timer] duration + jitter, emission rate            │
  (+ IActorMessageObserver│  TimeProvider-driven · striped/lock-free on the hot path · snapshot on read    │
   extension)            └───────────────┬───────────────────────────────────────┬──────────────────────┘
  Proto IMailboxStatistics ──────────────┘                                        │
                                          ▼                                        ▼
                          Sink 1 — OpenTelemetry (operator)        Sink 2 — Diagnostics block (integrator + enduser)
                          observable instruments read the core      a logic block reads the core on its [Timer]
                          → vion-telemetry → Mimir → Grafana         → measuring points / properties → Plane B → dashboard
```

The **core is the single readable source**; both sinks are *observers* of it. This sidesteps the fact that OTel instruments aren't readable back in-proc (Sink 1 uses **observable** instruments whose callbacks read the core at export; Sink 2 reads the core directly). DevHost gets a vitals view from the same core for free.

### The vitals core (SDK)

- A thread-safe in-proc aggregate, one entry per actor, holding current gauges (mailbox depth, last-activity) and rolling/counted stats (message rate, handler duration, error/restart/deadletter counts, per-`[Timer]` duration + scheduler jitter, emission rate per property).
- **Two actor categories**, one schema:
  - **logic-block** — dimensions `block.type` (the real block class, known at construction from `actorReceiverType`), `block.id` (instance name), `library` (package).
  - **runtime** — `actor.kind = runtime`, `role` = the handler/transport name (`MqttClient`, `ServicePropertyHandler`, …).
- **Fed by three sources:**
  1. **Extended `IActorMessageObserver`** — add `OnHandled(actorName, message, elapsed, exception)` called around `await next(...)` and in the catch (`ActorMiddleware.cs:39-60`). Yields rate, handler duration, error count per actor. (Generalise the single-observer slot to a **composite** so DevHost's tap and the vitals collector coexist.)
  2. **A Proto `IMailboxStatistics` hook** at the spawn site (`props.WithMailbox(…)` in `ActorSystem.CreateRootActorFromDi`) — mailbox depth as `MessagePosted − MessageReceived` (atomic, per actor); coexists with Proto's own stats, off the OTel export path.
  3. **Proto's native counters** (restart/deadletter/future-timeout) — consumed directly by Sink 1; mirrored into the core where Sink 2 needs them.
- **Hot-path discipline:** writes happen on actor threads, so per-actor counters are striped/lock-free; reads (export + the diagnostics block's timer) take a snapshot copy. All timing uses the injected `TimeProvider`.
- Identity mapping: the middleware sees the actor *name* (`context.Self.Id`); the core maps name → category/dimensions using the registry the SDK already builds at spawn (block name/type are known where `CreateRootActorFromDi` is called).

### Sink 1 — OpenTelemetry (operator / 3rd-level support)

A `vion.actor.*` meter exposes **observable** instruments whose callbacks read the core at the 60s export tick. Wired entirely through `dale`'s existing call site (`Program.cs:157`):

```csharp
new VionTelemetryExportOptions(
    // …existing…
    MeterNames: ["Vion.Dale.Actors", "Proto.Actor"],
    MetricViews: [ /* allow-list + label-shaping per the tiers below */ ]);
```

**Two cardinality tiers** (≤200 blocks/gateway, fleet up to ~1000 gateways → one `vion` Mimir tenant, no metric budget today):

| Tier | What | Labels | Fleet series | Default |
|---|---|---|---|---|
| **Fleet** | logic-block scalars aggregated to **block-type**; runtime actors at full per-actor detail | `{block.type}` / `{role}` + `gateway` (drop `block.id`, drop `messagetype`) | ~160–300 k | **always-on** |
| **Focus** | per-**instance** block detail + handler-duration histogram | `{block.id}` + histogram (prefer OTel **exponential**) | ~50 k for ~10 enabled gateways | **opt-in per gateway, default-off** |

- Runtime actors are singletons (~10/gateway, ~10 k series fleet-wide) → **always-on, full detail**; they are the cheapest and highest-value signals (choke-point saturation).
- The label shaping is a `MetricView` with `TagKeys` (drop `block.id`/`messagetype` for Fleet). This **is** the missing "metric budget", enforced at the edge.
- **Never** export the raw `protoactor_actor_messagereceive_duration` (histogram × `id` × `messagetype`) fleet-wide; it belongs to the Focus tier only.

### Sink 2 — the diagnostics block (integrator + end-customer)

A first-party logic-block **library** (`Vion.Diagnostics`, published under the VION integrator — see [Packaging, ownership & publishing](#packaging-ownership--publishing)) that the integrator instantiates in a configuration like any other block. Its block injects the SDK's `IRuntimeDiagnostics` + `IActorSystem`, reads them on its own `[Timer]`, and republishes on Plane B.

- **Selection without wiring.** A runtime-set `Filter` `[ServiceProperty]` (name glob/regex), resolved via `IActorSystem.FindByName` + the core. One block, **zero connections**, nothing imposed on other blocks. Default = watch-all / rollup. (Set once via the dashboard property-set; persisted. The config has no author-time value path — by design.)
- **Output — a dashboard-native diagnostics table + rollups:**

```csharp
// rows = filtered blocks; renders as <StructArray>; status column colors each row
[ServiceProperty(Group = "Diagnostics")]
public ImmutableArray<BlockVitals> Blocks { get; private set; } = ImmutableArray<BlockVitals>.Empty;

[ServiceProperty(Group = "Diagnostics")]   // the pill
public DiagnosticsStatus Overall { get; private set; }

[ServiceProperty(Group = "Diagnostics")]   // the choke-points, for the integrator/enduser
public RuntimeHealth Runtime { get; private set; }

public readonly record struct BlockVitals(
    string BlockName,
    double MessageRatePerSec,
    [StructField(Unit = "ms")] double HandlerMsMax,
    int MailboxDepth,
    int Errors,
    int Restarts,
    DateTime LastActivityUtc,
    BlockHealth Status);          // enum → statusMappings → per-row severity color

public readonly record struct RuntimeHealth(
    int MqttIngressBacklog,       // MqttClient mailbox depth
    int PublisherBacklog,         // ServiceProperty/MeasuringPoint handler mailbox depth
    bool MqttConnected,
    double PublishErrorsPerSec);

public enum BlockHealth { Ok, Warning, Critical }
public enum DiagnosticsStatus { Healthy, Degraded, Overloaded }
```

- **Cheap on Plane B.** Per-instance detail is **one array-valued property per gateway** (one retained MQTT message / one dashboard table), not N per-block measuring points — so it dodges the Mimir cardinality problem entirely and rides the existing 1-min property/state path. Its own emissions are subject to RFC 0004 throttling.
- **Trended aggregates**, where time-series charting is wanted (e.g. max handler latency, total restarts), are a few scalar `[ServiceMeasuringPoint]`s (→ TimescaleDB hypertable). The per-block *table* stays a service property (compound values are not time-series-stored, and the `<StructArray>` render is the point).
- **DevHost** renders the same core as a live vitals view, and the diagnostics block runs there too — dev-time parity.

### Runtime-actor coverage (free, high-value)

Because `MqttClient` and the handlers are actors on the same seam, the core captures them automatically. The valuable signals:

- **`MqttClient` mailbox depth** = MQTT **ingress** saturation (all inbound messages enqueue there, `MqttClient.cs:99`).
- **`ServicePropertyHandler` / `ServiceMeasuringPointHandler` mailbox depth + handler duration** = **egress** saturation — these are the single shared publishers (N blocks → 1), with no batching/coalescing and QoS0 retained.

This is what distinguishes "a block is slow" from "the runtime egress is the bottleneck." Surfaced full-detail in Sink 1 (cheap, singletons) and as the compact `RuntimeHealth` in Sink 2.

### Watchdog seams — raw now, policy later

- The core **auto-measures** per-`[Timer]` callback duration and scheduler jitter (requested vs actual delay) at `HandleTimerTickMessage` (`LogicBlockBase.cs:~551`) — timer methods have identity, so this is clean. `InvokeSynchronizedAfter` actions are measurable in aggregate but anonymous (no stable name), so attribution is best-effort.
- **The policy** — what counts as an overrun, starvation/"omitted cycle", and what to alarm — is **out of scope here**. Prototype it as a hand-rolled spike in `energy-manager` (extend the existing `CycleDiagnostics` with cycle duration + an overrun flag — it records cycle *start* but no *duration* today), and harvest into the SDK once the semantics have settled. Framed in the integrators' own terms: PLC scan-time + watchdog (Siemens OB80) and CODESYS task monitoring (last/avg/max/min + jitter, "ran too long" vs "didn't run").

## Performance & concurrency (implementation guardrails)

**Observer effect — the instrumentation must never become the bottleneck it measures.** A hard constraint to re-verify during implementation, not a nicety:

- Timing via cheap monotonic reads; **gauges (mailbox depth) sampled at ~1 Hz, never per message**; counters striped/atomic with **no locks on the message hot path**; reads take a snapshot. Bound how often each sink updates — Sink 1 at the 60 s OTel export, Sink 2 on the diagnostics block's `[Timer]` (not per-change).
- Reference budget (Lightbend Cinnamon, comparable per-actor instrumentation): ~2 µs/message and ~3 % throughput with *everything* enabled — affordable, but the discipline is **selective-by-default** (scalars always-on, histograms opt-in). Re-measure on the edge target during implementation.
- Sink 1's Fleet tier is the de-facto cardinality budget; Sink 2 is bounded by Plane B's 1-min cadence.

**Unbounded mailboxes are an OOM hazard — flag for implementation.** Proto.Actor mailboxes are **unbounded** by default, so a flooded or wedged actor grows its mailbox without limit: observing mailbox depth *reveals* the backlog but does not *prevent* the out-of-memory it can cause. Proto offers `BoundedMailbox` / dropping-tail / dropping-head variants (overflow → dead-letters, which the deadletter counter already sees), attachable at the same `props.WithMailbox(...)` seam as the stats hook. **Whether to bound mailboxes is a control decision (RFC 0004 family) and is not settled here — but it must be evaluated during implementation**, since this RFC is what makes the hazard visible.

## Testing

- **TestKit determinism.** All core timing uses the injected `TimeProvider`; `FakeTimeProvider.AdvanceTime` drives rate windows, durations, and the diagnostics block's `[Timer]` exactly as RFC 0001 virtualises time. Default the vitals core **off** in the TestKit so existing emission-count assertions don't see diagnostics traffic; opt in to test it.
- Unit-test the extended `IActorMessageObserver` (rate/duration/error capture) and the composite-observer fan-out (DevHost tap + vitals collector coexist).
- Test the diagnostics block: filter resolution via `FindByName`, the `BlockVitals` table contents, and that `RuntimeHealth` reflects the runtime actors.
- Document/test the Sink 1 `MetricView` `TagKeys` shaping (Fleet drops `block.id`/`messagetype`).

## Where it lives

- **Vitals core, observer extension, `IMailboxStatistics` hook** — `Vion.Dale.ProtoActor` / `Vion.Dale.Sdk` (shared) so DevHost and the runtime share it and the TestKit drives it. This is the **same A1 argument as RFC 0004**: keep it in the SDK for DevHost parity + TestKit coverage, not in the runtime.
- **Sink 1 wiring** — `dale` only, as options at `Program.cs:157`. No change to `vion-telemetry`.
- **Diagnostics block** — a first-party logic-block library `Vion.Diagnostics` (homed at `dale-sdk/libraries/Vion.Diagnostics/`), published under the VION integrator; see [Packaging, ownership & publishing](#packaging-ownership--publishing). The SDK interface it depends on (`IRuntimeDiagnostics`) lives in `Vion.Dale.Sdk`.

## Packaging, ownership & publishing

The diagnostics block is **not** an SDK NuGet package — it is a **logic-block library**: content packed as a `.nupkg` and uploaded to cloud-api as a *LogicBlockLibraryVersion* owned by an Integrator account (`dale upload` → `POST /Integrator/{integratorId}/LogicBlockLibraryVersions`, `Vion.Dale.Cli/Commands/UploadCommand.cs:243`). It is the same mechanism the dale-sdk `examples/` use, promoted to a product-grade lane.

**Two artifacts, two pipelines:**

| Artifact | Package | Pipeline | Trigger |
|---|---|---|---|
| `IRuntimeDiagnostics` + vitals core | `Vion.Dale.Sdk` (existing NuGet) | `publish.yml` → nuget.org + private feed | `v*` tag |
| The diagnostics block | `Vion.Diagnostics` (logic-block library) | **new lane** → `dale upload` | per-library tag `Vion.Diagnostics/vX.Y.Z` |

- **Home.** `dale-sdk/libraries/Vion.Diagnostics/` — a new first-party-**library** area, parallel to `examples/` (which are test-tenant samples, uploaded on every `main` push). Co-located with the `IRuntimeDiagnostics` it pins, on the standard library project shape (`<Lib>/`, `<Lib>.DevHost/`, `<Lib>.Test/` + co-located `spec/`).
- **Owner / publisher.** The VION first-party **Integrator** account — the same identity that uploads the examples (Keycloak service-account, CI creds `DALE_CI_CLIENT_ID/SECRET`), with rights to the target environment(s).
- **Release lane.** A dedicated workflow, distinct from `publish.yml` (SDK NuGet) and `examples.yml` (test examples): on tag `Vion.Diagnostics/vX.Y.Z`, validate tag↔`<Version>`, build + test, then `dale upload --integrator-id <vion-guid> --environment <env> --skip-duplicate`. `dale upload` keys on `(packageId, version)`, so `<Version>` bumps on every content change (per `release-cascade.md`).
- **SDK coupling.** The library pins `Vion.Dale.Sdk` and therefore releases *after* the SDK version that introduces `IRuntimeDiagnostics` — cascade order `vion-contracts` → `dale-sdk` → `Vion.Diagnostics`.
- **Availability.** Platform-provided / public libraries are the cloud **default today**, so a library published under the VION integrator is pull-in-able by any integrator/tenant out of the box. (Cloud licensing may gate first-party libraries later — a cloud-side visibility policy with no SDK/RFC impact.)
- **Privilege.** The block injects `IRuntimeDiagnostics` from the runtime container, so it can read every actor's vitals. Acceptable for the closed user base; if cross-integrator isolation is ever needed, the core can expose a filtered/read-only view rather than the raw aggregate.

## Relationship to RFC 0004 (emission policy)

RFC 0004 *controls* one point (property emission rate); this RFC *measures* the whole runtime. They are complementary halves of "see the flood, then shape it," and share primitives in spirit (declarative surface, introspection plumbing, `TimeProvider`/TestKit determinism, DevHost surfacing). Kept separate per the user's scoping: 0004 ships control; 0005 ships measurement. Where 0004 surfaces a per-property emission **rate** in DevHost, this RFC generalises it into the per-actor vitals (emission rate is one core signal).

## Future work

- **Watchdog policy** (overrun / starvation / "omitted cycle") — harvested from the `energy-manager` spike once settled.
- **Dynamic per-device measuring points** — the SDK gap that forced `ResponseObserver` into a flat array property instead of per-device time-series.
- **`wait`-vs-`process` latency split** — carry an enqueue timestamp on messages to separate mailbox-queue time from handler time (the single most diagnostic split; needs a message-envelope change).
- **Per-block / per-namespace log level** — today log level is process-wide; a per-actor knob would let an integrator turn up just their block.
- **Bounded mailboxes + drop counter** — a measurement-adjacent control that would make backlog a hard signal (belongs to the 0004 control family).

## Decisions log

| # | Decision |
|---|---|
| 1 | Measurement-only; all rate-limiting/control stays in the RFC 0004 family. |
| 2 | One in-proc **vitals core** in the SDK; two **sinks** (OTel, diagnostics block) observe it. |
| 3 | Core covers **runtime actors** (MqttClient, publishers, handlers) as well as logic blocks — same middleware seam. |
| 4 | Roll our own `vion.actor.*` meter (clean `block.type`/`block.id`) rather than rely on Proto's coarse `actortype`; enable Proto's meter too for mailbox depth + lifecycle counts. |
| 5 | Sink 1 two-tier: block-**type** aggregates always-on fleet-wide; per-**instance** + histograms opt-in per gateway. Enforced via `MetricView` `TagKeys` at `dale`'s call site — no `vion-telemetry` change. |
| 6 | Diagnostics block selects targets via a runtime-set **filter property** (`FindByName`), never wiring. |
| 7 | Diagnostics output is an **`ImmutableArray<BlockVitals>` service property** (a `<StructArray>` table) + status + `RuntimeHealth`; trends as scalar measuring points. Cheap on Plane B. |
| 8 | Watchdog: ship **raw** per-`[Timer]` duration/jitter; **policy** is a hand-rolled `energy-manager` spike to harvest later. |
| 9 | Integrators' domain diagnostics are **not** absorbed into the SDK — they're legitimate, often block-specific, and reusable ones are DRY'd within the integrator's own libraries (e.g. `RegisterWriteTracker`). Orthogonal to the runtime/actor layer; explicitly not a goal. |
| 10 | Core is `TimeProvider`-driven; TestKit defaults it off (parity with 0004's TestKit stance). |
| 11 | The diagnostics block ships as a **logic-block library** `Vion.Diagnostics` (not an SDK package), homed at `dale-sdk/libraries/`, published under the VION integrator via `dale upload` on its own per-library-tag lane. `IRuntimeDiagnostics` stays in `Vion.Dale.Sdk`. Platform-provided/public libraries are the cloud default (may be license-gated later). |
