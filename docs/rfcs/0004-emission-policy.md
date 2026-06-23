# RFC 0004: Emission policy — throttling, deadband & dedup for service properties and measuring points

Status: **Accepted** (revised 2026-06-22) — ready for implementation. Author: jonas.bertsch. Original draft: 2026-05-31.

> **Revision note (2026-06-22).** The original draft was written against a single, free-running
> wall-clock DevHost. Since then **deterministic stepping (RFC 0008)** and **scenarios (RFC 0006)**
> landed, the introspection model split into three documents (Schema / Presentation / Runtime), the
> typed `MockHal*Handler` classes were removed (de-HAL, RFC 0010), and the production cost path was
> verified end-to-end in the Dale runtime. The **core design is unchanged** — a per-property
> leading-edge throttle + value-equality dedup + optional deadband, enforced in `LogicBlockBase`
> (Approach A1). Three sections were updated — [Mode-aware activation](#mode-aware-activation) (new),
> [Introspection & DevHost display](#introspection--devhost-display) (now on `RuntimeMetadata`, a
> read-only settings badge), and [Testing](#testing) — plus refreshed file/line anchors throughout.
> The interactive DevHost **rate-meter** and **ignore-policy toggle** from the original draft are
> **dropped from v1** (see [Future work](#future-work)) — the motivation is real runtime cost, not
> DevHost ergonomics.

## Motivation

A logic block publishes observable state by assigning `[ServiceProperty]` / `[ServiceMeasuringPoint]` values. Today **every** assignment that changes the value propagates all the way to the outside world with no rate control. This was verified to be literally true on the production path: the single central `ServicePropertyHandler` actor receives a `ServicePropertyValueChanged` from **every** block and, per change, encodes and publishes a **retained** MQTT message ([`dale` `ServicePropertyHandler.cs:226-259`](../../../dale/Dale/Mqtt/Handlers/ServicePropertyHandler.cs)). That retained `…/state` stream is then *"forwarded schema-blind to the cloud broker"* by the mesh ([same file, :230](../../../dale/Dale/Mqtt/Handlers/ServicePropertyHandler.cs)). So one un-gated assignment costs, in order:

```
block actor ─(in-proc msg)→ ServicePropertyHandler ─(retained publish)→ nanomq ─(mesh, schema-blind)→ cloud broker → dashboard UI
```

There is **no throttle, deadband, coalescing, or batching anywhere on that path today** (verified). Two concrete problems follow:

1. **No throttling / deadband.** A property fed by an uncontrolled input — a noisy analog input, or naive high-rate Modbus polling — floods every hop above: the central actor's mailbox, the nanomq publish rate, the metered mesh→cloud uplink, and the dashboard. The developer has no declarative way to say "emit this at most every *N*, and only when it moves by at least *X*."
2. **Hand-rolled change guards.** Because there is no SDK mechanism, the reference consumer ([logic-block-libraries](../../../logic-block-libraries)) writes the same guard-and-project shape in its setters (representative):

   ```csharp
   // RefControllableConsumer.cs — representative; the guard-then-EmitXxxStateUpdate() shape recurs per setter
   set { if (Math.Abs(_requestedCurrentA - value) < double.Epsilon) return; _requestedCurrentA = value; EmitDataStateUpdate(); }
   ```

   These guards aren't even about INPC dedup (Metalama already does that — see [Background](#background--verified-facts-that-constrain-the-design)); they gate the *domain* `EmitXxxStateUpdate()` projection. The SDK should own the emission side so this boilerplate disappears.

This RFC adds a declarative **emission policy** — leading-edge throttle + value-equality dedup + optional change-threshold (deadband) — configured on the property attribute and enforced inside the logic block actor. **Cutting at the source is the cheapest possible win**: every suppressed emission is simultaneously one fewer in-proc message, one fewer retained publish, one fewer mesh→cloud forward, and one fewer dashboard update.

## Goals

- Declarative per-property throttle (`MinInterval`) and change-threshold (`MinChange`) on the existing attributes.
- A safe value-equality dedup floor (closes Metalama's reference-equality gap for struct/reference-typed properties).
- **Eventual state correct** — the throttle never drops the final value of a burst, and on stop the exact current value is drained. (A configured deadband intentionally suppresses *sub-threshold* changes during operation; see [Behaviour details](#behaviour-details).)
- Use the injected `TimeProvider` / actor scheduling so the TestKit (and DevHost stepping) drive it deterministically.
- Surface the effective policy in introspection (`RuntimeMetadata`) so the DevHost — and the dashboard / agents — can display it.

## Non-goals (this RFC)

- Throttling **contract messages** (commands, request/response, state updates) — see [Adjacent areas](#adjacent-throttlingrating-areas-out-of-scope).
- Throttling at the **MQTT publish** boundary or the **input/service-provider forwarding** boundary — see [Adjacent areas](#adjacent-throttlingrating-areas-out-of-scope).
- **Per-installation / per-block runtime configuration** of the policy, and **connection-aware coalescing** (hold-while-disconnected) — these are deployment-time / runtime concerns the block can't see. Confirmed out for v1; see [Future work](#future-work).
- The interactive DevHost **emission-rate meter** and **per-property/global ignore-policy toggle** (original draft) — dropped from v1; see [Future work](#future-work).
- A public `Throttler<T>` building block (kept **internal** for v1; promoted in v1.1).

## Background — verified facts that constrain the design

**Metalama already dedups exact-equal sets.** The SDK applies the stock `Metalama.Patterns.Observability` `[Observable]` aspect ([MetalamaFabric.cs](../../Vion.Dale.Sdk/MetalamaFabric.cs)). Its generated setter skips raising `PropertyChanged` when the value is unchanged — **but asymmetrically**: value types use `!=` (exact), reference types (incl. `string`) use `object.ReferenceEquals` (identity only). So a rebuilt-but-equal record / `ImmutableArray` / struct still fires every cycle. Our value-equality floor exists to close exactly that gap; we do **not** re-implement exact dedup for scalars.

**Single chokepoint per kind.** Every runtime property change reaches `LogicBlockBase` through one event: `ServiceBinder` subscribes to each source's `INotifyPropertyChanged` and raises `ServicePropertyValueChanged` (and the measuring-point equivalent), whose **sole** consumer is `HandleServicePropertyValueChanged` (subscribed in the `LogicBlockBase` ctor). That handler does a bare `SendTo(_servicePropertyHandlerActorRef, …)` today (`LogicBlockBase.cs:451`; measuring point `:468`). This is the right single place for the per-block gate — downstream of Metalama dedup, upstream of every network cost.

**Topology: N block actors → 1 central publisher per kind.** Each logic block is its own actor; on change it sends the central handler — `MockServicePropertyHandler` in DevHost (`DevLogicSystemInitializer.cs:177`), `ServicePropertyHandler : IMqttHandlerActor` in the runtime ([`dale` `ServicePropertyHandler.cs`](../../../dale/Dale/Mqtt/Handlers/ServicePropertyHandler.cs)). This is why per-block throttling (this RFC) lives in `LogicBlockBase`, and why a *central* gate would instead belong in the runtime's publisher (rejected — see [Where it lives](#where-it-lives--approach-a1)).

**Scheduling substrate (changed since the draft).** `SendToSelfAfter(delay)` is no longer a bare `ReenterAfter(Task.Delay)`. As of RFC 0008 it has three load-bearing branches ([ActorContext.cs:54-97](../../Vion.Dale.ProtoActor/ActorContext.cs)): (1) a **pause-hold** via `IDelayedSendGate` (DevHost pause holds and replays delayed sends); (2) a **stepped-delivery** path that registers the self-send with the deterministic stepper (`IVirtualSchedule.RegisterDelivery`, in `(due-time, registration)` order — DF-18) when a controllable (`FakeTimeProvider`-style) clock is active; (3) the production `ReenterAfter(Task.Delay(delay, _timeProvider))`. (A fourth path covers a real-clock host with an opt-in virtual schedule.) **Consequence:** the trailing-edge flush this RFC schedules is automatically deterministic under both the virtual-time TestKit (RFC 0001) and DevHost stepping (RFC 0008). How the scheduler detects a controllable clock — a public instance `Advance(TimeSpan)` returning void — is the same probe the gate reuses for [Mode-aware activation](#mode-aware-activation). The existing `[Timer]` and persistence-save paths already self-reschedule through this seam (`LogicBlockBase.cs:615-648`), so the gate's single flush is one more idiomatic self-message.

## Semantics

**Throttle = leading-edge rate limit, never debounce.** The first change after an idle gap emits **immediately**. Further changes within `MinInterval` are *held* (latest-wins); at `lastEmit + MinInterval` the held value flushes (trailing edge). If nothing was held, no flush and no timer. There is never an initial delay.

The per-property gate, evaluated on each change the gate receives (already INPC-deduped by Metalama, on the actor thread):

```
Offer(value, now):
  1. if EqualityComparer.Default.Equals(lastEmitted, value)   -> Drop      # value-equality floor — always on, incl. under Immediate
  2. if policy.Immediate            -> EmitNow                              # bypass throttle + deadband (not the floor)
  3. if policy.HasThreshold && !threshold.Exceeds(lastEmitted, value, minChange) -> Drop   # deadband
  4. if now - lastEmitAt >= policy.MinInterval -> EmitNow
  5. else -> Hold(deadline = lastEmitAt + MinInterval)
```

- Step 1 (floor) is **always on, including under `Immediate`**; for value types it's a no-op (Metalama already filtered exact-equal), so its net effect is **value-equality for struct/reference types** (emitting a rebuilt-but-equal value is never useful, even for an `Immediate` flag).
- Step 2 (`Immediate`) bypasses the throttle **and** the deadband, but not the floor.
- Step 3 (deadband) only when `MinChange` is set; it subsumes the floor. A sub-threshold change is `Drop`ped (not held), so the visible value may lag the current value by up to `MinChange` during operation — the deadband contract. The [stop-drain](#behaviour-details) restores exactness.
- On `EmitNow`: emit, `lastEmitted = value`, `lastEmitAt = now`, clear pending.
- On `Hold`: store latest value as pending; ensure one flush is scheduled at `deadline`.
- On flush (`FlushDue`): emit pending, update `lastEmitted`/`lastEmitAt`, clear pending; if still bursting, reschedule.

## Configuration — attribute surface

Added to **both** `ServicePropertyAttribute` and `ServiceMeasuringPointAttribute` (a new shared internal helper holds the three knobs — the two attributes have no common base today), alongside the existing `Minimum`/`Maximum`:

| Knob | Type | Meaning | Default |
|---|---|---|---|
| `MinInterval` | `string` (`"250ms"`, `"1s"`, `"0"`) | min time between emissions (leading-edge throttle); `"0"` / `"0ms"` **disables the time-throttle** (floor + any deadband still apply) | `"250ms"` |
| `MinChange` | `string` (`"0.1"`, `"1s"`) | change-threshold (deadband); interpreted by the type's `IChangeThreshold<T>` | unset (value-equality floor only) |
| `Immediate` | `bool` | bypass throttle **and** deadband — emit every change (the value-equality floor still applies) | `false` |

**Default policy = `{ MinInterval: 250ms, value-equality floor: on, deadband: none }`.** Throttling is **on by default** (mild) — acceptable given the closed, known user base, and the whole point is to cut cost without per-block opt-in; see [Backwards compatibility](#backwards-compatibility).

`MinChange` is a string so each type interprets units unambiguously (a `double` reads `"0.1"`; a `TimeSpan` reads `"1s"`). The change logic is **type-resolved**:

```csharp
public interface IChangeThreshold<T>
{
    // true => the change is large enough to emit
    bool Exceeds(in T lastEmitted, in T candidate, string threshold);
}
```

- SDK ships built-ins for the numeric types and `TimeSpan`.
- A custom struct registers `IChangeThreshold<T>` **once** (per type); every property of that type then just sets `MinChange`. No per-property `typeof`.

## Where it lives — Approach A1

Throttling lives in `LogicBlockBase`'s emit handlers (`HandleServicePropertyValueChanged` / `HandleServiceMeasuringPointValueChanged`), **before** the `SendTo(handler)`. Each block holds one gate per throttled property and schedules **at most one** trailing flush to itself (`SendToSelfAfter(FlushDue)`), coalescing all its due properties into one wakeup.

```csharp
// In LogicBlockBase (sketch)
private readonly Dictionary<EmissionKey, Throttler> _throttlers;   // resolved at startup from attributes

private void HandleServicePropertyValueChanged(object? s, ServicePropertyChangedEventArgs e)
{
    if (!_started) return;
    switch (_throttlers[Key(e)].Offer(e.Value, _timeProvider.GetUtcNow()))
    {
        case EmitNow:  Emit(e); break;
        case Drop:     break;
        case Hold h:   ScheduleFlush(h.Deadline); break;   // one outstanding SendToSelfAfter per block
    }
}
private void OnFlushDue() { /* flush all due throttlers in one turn; reschedule next-earliest */ }
// On Stopping: drain — emit each property's exact current value (bypassing throttle + deadband).
```

**Why A1 (not a central ticker or a shared scheduler), validated against the real cost path.** Three candidates were analysed:

| | Where | Relieves MQTT-actor ingress | Relieves publish/mesh/cloud/dashboard | Timers | TestKit + Dev parity |
|---|---|---|---|---|---|
| **A1** *(chosen)* | per block, self-scheduled | ✅ | ✅ | ≤1 per *bursting* block | ✅ |
| A2 | per-block gates + 1 shared scheduler actor | ✅ | ✅ | 1 total | ✅ (but bidirectional coupling + lifecycle + fan-out msgs) |
| B | runtime central `ServicePropertyHandler` | ❌ (message still sent) | ✅ | 1 total | ❌ (outside SDK; mock must replicate) |

Cutting at the **source** (A1) is the only option that also relieves the central actor's **ingress** — under B the block still sends the message, so the bottleneck actor still serializes and processes it; B only suppresses the downstream publish. So **A1 strictly dominates B on cost** *and* keeps dev/prod parity + TestKit coverage. Under the target workload (≤ ~200 blocks, *few* properties ever exceed their interval, bursts concentrated in a handful of analog-IO blocks), A1's timer count is **a handful**, not N — gate *state* is per-property and cheap, but a *timer* is armed only for a property actively changing faster than its interval. A2's single-timer win doesn't pay for its global component + bidirectional coupling + lifecycle + per-burst messages. A1 is also idiomatic — the same self-scheduling pattern as `[Timer]` and the persistence save.

The gate is written **scheduler-agnostic** (it returns `Hold(deadline)`; it does not own a timer), so if simultaneous-burst count ever explodes, swapping per-block self-scheduling for A2's shared scheduler is a contained change at one call site. We do not build A2 now (YAGNI).

*(Deployment-time tuning and connection-aware coalescing — the genuine capabilities A1 cannot provide because the block can't see broker/uplink state — are a separate runtime concern, intentionally out; see [Adjacent areas](#adjacent-throttlingrating-areas-out-of-scope) and [Future work](#future-work).)*

## Behaviour details

- **Initial publish is never throttled.** `ServiceBinder.PublishInitialStateUpdates` (startup) emits immediately; `lastEmitAt` seeds to start time. Note `_started` is already `true` when `PublishInitialStateUpdates` runs (`StartLogicBlockRequest` sets it just before), so the initial publish flows through the same `HandleServicePropertyValueChanged` gate — A1 special-cases it (seed + force-emit).
- **Value-*cleared* bypasses the gate.** `ServicePropertyValueCleared` / `…MeasuringPointValueCleared` (`LogicBlockBase.cs:480`/`:492`) emit immediately and cancel any pending flush — a clear is a state-significant edge. (These handlers already have no `_started` guard, so the bypass is consistent with today's behaviour.)
- **Drain on stop.** On `Stopping`, emit each throttled property's **exact current value** if it differs from `lastEmitted` (bypassing throttle **and** deadband), so the final retained state is exact — then `_started=false` / `ClearRetainedMessages`. The deadband suppresses sub-threshold changes only *during operation*; the shutdown drain restores exactness.
- **`Immediate` bypasses both** throttle and deadband (not the floor) — the escape hatch for safety/error flags.
- **Measuring points included** — same policy surface; measuring points remain read-only.
- **`WriteOnly` properties unaffected** — they publish a redaction sentinel, not values; no rate concern.
- **Persistence unaffected** — the periodic save records the property's *current* value, independent of emission timing.
- **Ordering** — last-value-wins per property is preserved (the gate holds only the latest).

## Mode-aware activation

The flush rides `SendToSelfAfter` (pause-gated + stepper-aware — see [Background](#background--verified-facts-that-constrain-the-design)), so the gate is deterministic under virtual time *for free*. But default-on throttling **changes what an observer of the emit stream sees**, and several contexts need it off for determinism.

**The activation rule is the clock.** The gate forces `EmitNow` whenever its injected `_timeProvider` is a **controllable** clock — re-derived with the same structural probe the scheduler already uses (`IsSteppedClock`: a public instance `Advance(TimeSpan)` returning `void` ⇒ a `FakeTimeProvider`-style virtual clock), computed once at start and cached, exactly as `ActorContext` / `ActorSystem` / `DevHostControl` do today. One rule covers every context that wants determinism:

| Context | Clock | Policy |
|---|---|---|
| Production (Dale runtime) | real | **ON** — the entire point |
| DevHost, free-run | real | **ON** — dev/prod parity |
| DevHost, stepped (`--stepped`) + deterministic (stepped) scenario runs | controllable | **OFF** — preserves the deterministic clock + RFC 0008's "assert state, not the schedule" |
| TestKit | controllable (`FakeTimeProvider`) | **OFF** — so emission-count assertions don't coalesce |

This resolves the scenario-run question precisely: a *deterministic* scenario runs on the controllable clock → policy off; a free-run (real-clock) scenario is non-deterministic by nature → policy on, consistent with everything else on a real clock. The TestKit default falls out of the *same* rule (its `FakeTimeProvider` is a controllable clock), so there is no second off-switch to build.

**Reachability (why not a host flag).** The SDK cannot consult a host-level `IsStepped`: `IDevHostControl.IsStepped` lives in the DevHost assembly the `netstandard2.1` SDK must not reference, and `ActorContext._stepped` is private and not on `IActorContext`. Re-deriving from the injected `_timeProvider` is the reachable path — a fourth instance of the existing probe, or (preferably) a shared SDK helper the scheduler also calls.

**The one explicit override.** `WithEmissionPolicy(FromAttributes)` in the TestKit forces the policy **active despite** the controllable clock (a per-context flag the gate checks *before* the clock probe), so a test can exercise throttling itself. `VerifyServicePropertyEmitted` then observes the throttled stream; without the override the fake clock keeps the policy off and `VerifyServicePropertyChanged` sees raw INPC. This override is the only knob the mode logic needs beyond the clock probe.

## Public API surface (v1)

```csharp
[ServiceProperty(MinInterval = "250ms", MinChange = "0.1")]   // numeric -> built-in threshold
public double Voltage { get; set; }

[ServiceProperty(Immediate = true)]                            // safety/error flag, never throttled
public bool OverTemperatureFault { get; private set; }

// custom struct: register the change logic once, reuse on every property of that type
public sealed class ThreePhaseCurrentChangeThreshold : IChangeThreshold<ThreePhaseCurrent>
{
    public bool Exceeds(in ThreePhaseCurrent last, in ThreePhaseCurrent now, string threshold) => /* … */;
}
```

- **Public:** the three knobs on the attributes; `IChangeThreshold<T>`.
- **Internal (v1):** `Throttler`, `ThrottlePolicy`, the `EmitNow`/`Hold`/`Drop` decision — finalised when promoted to public in v1.1.

## Introspection & DevHost display

The effective policy is surfaced per property so the DevHost (and the dashboard / agents) can display it. **It belongs on the existing `RuntimeMetadata` document, not the schema** — it is *behaviour*, not data-shape. The introspection model split into three sibling documents since the draft: `Schema` (`TypeAnnotations` — where `Minimum`/`Maximum` live), `Presentation`, and `Runtime` (`RuntimeMetadata`, currently carrying only `Persistent`). The throttle block extends `RuntimeMetadata`:

```json
"runtime": { "persistent": true, "throttle": { "minInterval": "250ms", "minChange": "0.1", "immediate": false } }
```

The introspection-surfacing piece is the only cross-repo dependency; see [Cross-repo sequencing](#cross-repo-sequencing). The pieces:

1. **`Vion.Contracts`** (separate repo, consumed here as a NuGet `PackageReference`): add the `throttle` sub-record to `RuntimeMetadata` + its codec + the `IsEmpty` update; release a new version.
2. **`dale-sdk`**: `PropertyMetadataBuilder.ExtractRuntime` reads `MinInterval`/`MinChange`/`Immediate` off the attribute (same reflection path it uses for `Minimum`/`Maximum`) and populates the block. Omitted when the policy is the default (`IsEmpty`), to keep introspection lean. *(Gated on step 1's release.)*
3. **DevHost**: the SPA's `badgeList()` (in **`components.js`**, which today reads only `item.schema` / `item.presentation`) gains a `push('throttle', …)` branch reading `item.runtime?.throttle` — its **first** runtime read — rendering a **read-only settings badge** (e.g. `throttle 250ms · Δ0.1`). The raw `runtime` JSON already appears in `DocsRow`'s runtime panel for free; the badge is the deliverable. No new endpoint; consistent with the no-build discipline. *(Gated on step 1's release.)*

## Testing

- **TestKit default: policy OFF** — falls out of the [clock probe](#mode-aware-activation) (the TestKit hosts a `FakeTimeProvider`), so emission-count assertions like `VerifyServicePropertyChanged(Exactly(2))` stay deterministic with no extra mechanism.
- Opt in with `WithEmissionPolicy(FromAttributes)` to test the policy itself — this **overrides** the clock-off rule (forces the policy active despite the fake clock). Add `VerifyServicePropertyEmitted(...)` (policy applied) **alongside** the existing `VerifyServicePropertyChanged(...)` (raw INPC) — they answer different questions, and the TestKit's emission capture sits downstream of the gate, so coalescing is observable.
- The trailing-edge flush uses `SendToSelfAfter`, so the TestKit's `AdvanceTime` / `FlushPendingActions` (RFC 0001 virtual time) drive it deterministically — the single-SUT unit lane RFC 0008 explicitly leaves unchanged.
- Throttle/deadband behaviour is a **timing** property, which a settle-based scenario (RFC 0008) deliberately abstracts away — so emission-policy correctness is tested in the TestKit unit lane (with the override), **not** as a stepped scenario. Stepped scenarios run with the policy clock-off (see [Mode-aware activation](#mode-aware-activation)).

## Analyzers (DALE0xxx)

- `MinChange` on a numeric / `TimeSpan` → parse-check the string for that type's format.
- `MinChange` on a type with **no resolvable `IChangeThreshold<T>`** → error.
- `MinChange` on `bool` → error (no magnitude).
- `MinInterval` unparseable or below a floor (e.g. < 1 ms) → error / warning — **except** the explicit `"0"` / `"0ms"` sentinel (throttle disabled), which is recognized and exempt.
- `Immediate = true` together with `MinInterval` / `MinChange` → warning (ignored).
- `MinChange` set but `MinInterval` omitted → info (deadband-without-throttle is valid).

## Naming

"Throttle" is the umbrella term — most universally understood, domain-neutral (the SDK is not energy-specific), and our exact leading-edge-plus-trailing semantics match **lodash `throttle`**. (Note: Rx confusingly names leading-edge throttling `Sample` and uses `Throttle` for debounce — we follow common/lodash usage.) The control-engineering term **deadband was rejected** as domain jargon in favour of `MinChange` / `IChangeThreshold<T>`. Knob names stay plain and self-describing (`MinInterval`, `MinChange`, `Immediate`) rather than embedding "Throttle".

## Adjacent throttling/rating areas (out of scope)

This RFC throttles **one** point in the pipeline — property *emission* (block → publisher). Two neighbouring points have their own rate-control needs and are **explicitly out of scope**.

### 1. Input side — service-provider event forwarding (the uncontrolled-input risk)

A service provider forwards each input change to **every** linked block as a contract message. Since the de-HAL (RFC 0010) this is **one generic** `ServiceProviderContractHandler` (the four typed `MockHal*Handler` classes were removed) — structurally an even better *central* position for input-side sampling than the original draft assumed. A flooding analog input still (a) crosses provider→block per change and (b) runs each block's contract handler per change; **emission throttling only caps the resulting output**, not the ingestion. True protection wants **sampling/deadband at the service-provider boundary** (the single generic handler, before fan-out), reusing the same `Throttler<T>` / `IChangeThreshold<T>` primitives with a different configuration surface. Because input forwarding is contract-message-based, it is doubly out of scope here. **Recommended follow-up: a separate RFC for input-side sampling, on the post-de-HAL `[ScenarioWire]` value-contract plane.**

### 2. Downstream — the MQTT publish layer (runtime)

The runtime's single `ServicePropertyHandler` publishes each update to MQTT (retained). Emission throttling reduces the **number of updates generated at the source** — the cheapest win, benefiting MQTT for free (fewer publishes, less retained churn, less metered mesh→cloud uplink). Concerns that remain the **runtime's** to own (Approach B territory, not the SDK):

- **Connection-aware coalescing** — hold latest-per-property while the mesh→cloud link is down, flush on reconnect. The block-side gate can't see connection state.
- **Per-installation tuning** — operators tuning rates per deployment without recompiling blocks.
- **Outbound backpressure / socket rate-limit** on metered links; **packet batching**; per-property **QoS / retain** policy.

Emission throttling and MQTT-layer control are complementary; if both exist, emission throttling runs first (less work reaches the publisher).

### 3. Sender-side StateUpdate / command throttling

Contract messages are not auto-throttled (timer/request-driven; can't be skipped). For the one case a sender *wants* it — a `StateUpdate` on a hot path — the **same `Throttler<T>` + `IChangeThreshold<T>` primitives** are exposed for explicit, opt-in use (v1.1).

## Backwards compatibility

- **Default-on throttling is a behaviour change** (≤4 Hz/property, value-equality dedup). Accepted given the closed, known user base; called out in release notes. To disable throttling on a property, set `MinInterval = "0"` (disables the time-throttle; the value-equality floor + any deadband still apply) or `Immediate = true` (bypass throttle and deadband; the floor still applies).
- Attribute additions are purely additive; `Minimum`/`Maximum`/etc. unchanged.
- Metalama `[Observable]` is untouched — the gate sits downstream of it.
- `IChangeThreshold<T>` and the TestKit additions are additive.
- The `RuntimeMetadata.throttle` field is additive in `Vion.Contracts` (`IsEmpty` when default), so older consumers ignore it.

## Cross-repo sequencing

The introspection block is the only piece crossing the `Vion.Contracts` boundary. Everything else — the gate, attributes, `IChangeThreshold`, mode-aware activation, and TestKit support — carries the cost win with **no cross-repo dependency** and can land first.

1. **`Vion.Contracts`:** add `RuntimeMetadata.throttle` (+ codec + `IsEmpty`). **Note the version reality:** dale-sdk currently references `Vion.Contracts` **1.1.0**, but `Vion.Contracts` has already tagged **v2.0.0** — a *breaking* release that removed the property/measuring-point get-topics. The throttle field is additive on top of v2.0.0 and ships as **2.1.0**.
2. **`dale-sdk`:** bumping the reference `1.1.0 → 2.1.0` also pulls in the get-topics removal — **a migration prerequisite, not an additive minor.** Verify nothing in dale-sdk consumes the removed get-topic APIs before bumping. Only the `PropertyMetadataBuilder.ExtractRuntime` populate + the DevHost badge depend on this bump; the rest of the feature is independent of it.

## Future work

- **A2 scale-out** — per-block gates + a single shared scheduler actor, if simultaneous-burst count ever justifies one timer total. The gate is already scheduler-agnostic; a localized swap.
- **Runtime / deployment-time control** — per-installation override of the policy + connection-aware coalescing + aggregate uplink control, owned by the Dale runtime (Adjacent area 2). The capability A1 structurally cannot provide.
- **DevHost emission-rate meter + ignore-policy toggle** (original draft) — a live per-property rate readout (needs a *pre-gate* measurement the block emits, since the SPA coalesces at ~10 Hz and DevHost only sees the post-gate stream) and an interactive bypass (needs a control message to the block actor, as the gate lives in the actor). Both deferred — the v1 introspection badge already shows the *configured* policy.
- **Member-level `[MinChange]` on struct fields** — source-generate the `IChangeThreshold<T>`. Deferred until real demand.
- **Transition-aware throttle** — carry `(value, transitionsInWindow)` so a fast `false→true→false` is not just resolved to the final value. Requires a wire-format change; v2.
- **Promote `Throttler<T>` to public** (v1.1) for sender-side use (Adjacent area 3).
- **Input-side sampling RFC** (Adjacent area 1).

## Decisions log

| # | Decision |
|---|---|
| 1 | Throttle = leading-edge (immediate first change), trailing flush of latest; never debounce. |
| 2 | Lives in `LogicBlockBase` (Approach **A1**), per-block self-scheduled single flush. **Validated against the real Dale path: A1 strictly dominates a runtime-central gate (B) on cost — only A1 relieves the central actor's ingress, not just the publish.** |
| 3 | Plain C# in the SDK — **no custom Metalama aspect** (sidesteps Metalama commercial-aspect licensing). |
| 4 | Default policy **on**: `MinInterval = 250ms`, value-equality floor on, deadband off. |
| 5 | Knobs `MinInterval` / `MinChange` / `Immediate` as strings; on both SP & MP attributes. `"0"`/`"0ms"` disables the throttle (analyzer-recognized sentinel). |
| 6 | One deadband mechanism: value-equality floor + `MinChange` + type-resolved `IChangeThreshold<T>` (built-ins for numeric/`TimeSpan`). |
| 7 | **Offer() order: the value-equality floor is step 1 and always on, including under `Immediate`** (emitting a rebuilt-but-equal value is never useful). `Immediate` bypasses only the throttle + deadband. |
| 8 | **Eventual-state guarantee (revised):** the throttle never drops a burst's final value; the deadband suppresses sub-threshold changes *during operation* only; **drain-on-stop emits each property's exact current value** (bypassing throttle + deadband) so the final retained state is exact. |
| 9 | Contract messages excluded; sender-side throttling via the (v1.1) public `Throttler<T>`. |
| 10 | **Mode-aware activation via the clock probe (revised):** policy forced OFF whenever the injected `_timeProvider` is a controllable (`FakeTimeProvider`-style) clock — one rule covering stepped DevHost, deterministic scenario runs, and the TestKit default. ON on a real clock (production + free-run DevHost). The SDK re-derives this from `_timeProvider` (no reachable host-level `IsStepped`). `WithEmissionPolicy(FromAttributes)` is the single explicit override (force ON despite a fake clock). |
| 11 | `Throttler<T>` internal for v1; `IChangeThreshold<T>` public. |
| 12 | Naming: "Throttle" umbrella; "deadband" rejected as domain jargon. |
| 13 | **Introspection on `RuntimeMetadata` (revised):** the policy is behaviour, surfaced on the existing runtime document, consumed by a **read-only DevHost settings badge** (`badgeList()` in `components.js`, the first `item.runtime` read). |
| 14 | **DevHost v1 = display only (revised):** the live rate-meter and interactive ignore-policy toggle are dropped from v1 (Future work); the motivation is production cost, not DevHost ergonomics. |
| 15 | **Deployment-time tuning + connection-aware coalescing are runtime concerns, out of scope** — A1 can't see broker/uplink state. |
| 16 | **Cross-repo (revised):** the `Vion.Contracts` bump is `1.1.0 → 2.1.0`, which carries the breaking get-topics removal (v2.0.0) — a migration prerequisite, not an additive minor. The rest of the feature is independent of it. |
