# RFC 0003: Emission policy — throttling, deadband & dedup for service properties and measuring points

Status: **Draft** — design-only, not implemented. Author: jonas.bertsch. Date: 2026-05-31.

## Motivation

A logic block publishes observable state by assigning `[ServiceProperty]` / `[ServiceMeasuringPoint]` values. Today **every** assignment that changes the value propagates all the way to the outside world (MQTT in production, the DevHost UI in dev) with no rate control. Two concrete problems:

1. **No throttling / deadband.** A property fed by an uncontrolled input — e.g. a noisy analog input, or naive high-rate Modbus polling — floods the system with state updates. The developer has no declarative way to say "emit this at most every *N*, and only when it moves by at least *X*."
2. **Hand-rolled change guards.** Because there is no SDK mechanism, the reference consumer ([logic-block-libraries](../../../logic-block-libraries)) writes the same guard in every setter:

   ```csharp
   // RefControllableConsumer.cs — repeated per property
   set { if (Math.Abs(_x - value) < double.Epsilon) return; _x = value; EmitDataStateUpdate(); }
   ```

   These guards aren't even about INPC dedup (Metalama already does that — see Background); they gate the *domain* `EmitXxxStateUpdate()` projection. The SDK should own the emission side so this boilerplate disappears.

This RFC adds a declarative **emission policy** — leading-edge throttle + value-equality dedup + optional change-threshold (deadband) — configured on the property attribute and enforced inside the logic block actor.

## Goals

- Declarative per-property throttle (`MinInterval`) and change-threshold (`MinChange`) on the existing attributes.
- A safe value-equality dedup floor (closes Metalama's reference-equality gap for struct/reference-typed properties).
- **Eventual state always correct** — never drop the final value of a change burst.
- Use the injected `TimeProvider` / actor scheduling so the TestKit drives it deterministically.
- Minimal edge footprint (target: 1–2 core gateways, ≤ ~200 logic blocks).

## Non-goals (this RFC)

- Throttling **contract messages** (commands, request/response, state updates) — see [Adjacent areas](#adjacent-throttlingrating-areas-out-of-scope).
- Throttling at the **MQTT publish** boundary or the **input/service-provider forwarding** boundary — see [Adjacent areas](#adjacent-throttlingrating-areas-out-of-scope).
- Per-installation / per-logic-block-class configuration (dropped from v1; see [Future work](#future-work)).
- A public `Throttler<T>` building block (kept **internal** for v1; promoted in v1.1).

## Background — verified facts that constrain the design

**Metalama already dedups exact-equal sets.** The SDK applies the stock `Metalama.Patterns.Observability` `[Observable]` aspect ([MetalamaFabric.cs:34](../../Vion.Dale.Sdk/MetalamaFabric.cs#L34)). Its generated setter skips raising `PropertyChanged` when the value is unchanged — **but asymmetrically**: value types use `!=` (exact), reference types (incl. `string`) use `object.ReferenceEquals` (identity only). So a rebuilt-but-equal record / `ImmutableArray` / struct still fires every cycle. Our value-equality floor exists to close exactly that gap; we do **not** re-implement exact dedup for scalars.

**Topology: N block actors → 1 central publisher per kind.** Each logic block is its own actor; on change it does `_actorContext.SendTo(_servicePropertyHandlerActorRef, new ServicePropertyValueChanged(...))` ([LogicBlockBase.cs:454](../../Vion.Dale.Sdk/Core/LogicBlockBase.cs#L454)). The handler is a **single shared actor** — `MockServicePropertyHandler` in DevHost ([DevLogicSystemInitializer.cs:258](../../Vion.Dale.DevHost/DevLogicSystemInitializer.cs#L258)), `ServicePropertyHandler : IMqttHandlerActor` in the runtime (`dale/Dale/Mqtt/Handlers/ServicePropertyHandler.cs`). This is why per-block throttling (this RFC) lives in `LogicBlockBase`, and why a *central* ticker would instead belong in the runtime's publisher (rejected — see [Where it lives](#where-it-lives--approach-a1)).

**Scheduling substrate.** `SendToSelfAfter(delay)` is `ReenterAfter(Task.Delay(delay), …)` ([ActorContext.cs:38](../../Vion.Dale.ProtoActor/ActorContext.cs#L38)) — each delayed self-message allocates a timer + task. The existing `[Timer]` and persistence-save paths already self-reschedule this way ([LogicBlockBase.cs:602](../../Vion.Dale.Sdk/Core/LogicBlockBase.cs#L602)). The design **minimises the number of scheduled flushes** and reuses this seam (so TestKit `AdvanceTime` drives it — see RFC 0001).

## Semantics

**Throttle = leading-edge rate limit, never debounce.** The first change after an idle gap emits **immediately**. Further changes within `MinInterval` are *held* (latest-wins); at `lastEmit + MinInterval` the held value flushes (trailing edge). If nothing was held, no flush and no timer. There is never an initial delay.

The per-property gate, evaluated on each change the gate receives (already INPC-deduped by Metalama, on the actor thread):

```
Offer(value, now):
  1. if policy.Immediate            -> EmitNow
  2. if EqualityComparer.Default.Equals(lastEmitted, value)   -> Drop      # value-equality floor
  3. if policy.HasThreshold && !threshold.Exceeds(lastEmitted, value, minChange) -> Drop   # deadband
  4. if now - lastEmitAt >= policy.MinInterval -> EmitNow
  5. else -> Hold(deadline = lastEmitAt + MinInterval)
```

- Step 2 (floor) is always on; for value types it's a no-op (Metalama already filtered exact-equal), so its net effect is **value-equality for struct/reference types**.
- Step 3 (deadband) only when `MinChange` is set; it subsumes the floor.
- On `EmitNow`: emit, `lastEmitted = value`, `lastEmitAt = now`, clear pending.
- On `Hold`: store latest value as pending; ensure one flush is scheduled at `deadline`.
- On flush (`FlushDue`): emit pending, update `lastEmitted`/`lastEmitAt`, clear pending; if still bursting, reschedule.

## Configuration — attribute surface

Added to **both** `ServicePropertyAttribute` and `ServiceMeasuringPointAttribute` (shared internal helper to avoid duplication), alongside the existing `Minimum`/`Maximum` ([ServicePropertyAttribute.cs:23](../../Vion.Dale.Sdk/Core/ServicePropertyAttribute.cs#L23)):

| Knob | Type | Meaning | Default |
|---|---|---|---|
| `MinInterval` | `string` (`"250ms"`, `"1s"`) | min time between emissions (leading-edge throttle) | `"250ms"` |
| `MinChange` | `string` (`"0.1"`, `"1s"`) | change-threshold (deadband); interpreted by the type's `IChangeThreshold<T>` | unset (value-equality floor only) |
| `Immediate` | `bool` | bypass throttle **and** deadband — emit every change | `false` |

**Default policy = `{ MinInterval: 250ms, value-equality floor: on, deadband: none }`.** Throttling is **on by default** (mild) — acceptable given the closed, known user base; see [Backwards compatibility](#backwards-compatibility).

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

Throttling lives in `LogicBlockBase`'s emit handlers (`HandleServicePropertyValueChanged` / `HandleServiceMeasuringPointValueChanged`, around [LogicBlockBase.cs:440-484](../../Vion.Dale.Sdk/Core/LogicBlockBase.cs#L440)), **before** the `SendTo(handler)`. Each block holds one gate per throttled property and schedules **at most one** trailing flush to itself (`SendToSelfAfter(FlushDue)`), coalescing all its due properties into one wakeup.

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
// On Stopping: drain — emit any pending values.
```

**Why A1 (not a central ticker or a shared scheduler).** Three candidates were analysed:

| | Where | Timers | TestKit-coverable | Dev/prod parity |
|---|---|---|---|---|
| **A1** *(chosen)* | per block, self-scheduled | ≤1 per *bursting* block | ✅ | ✅ |
| A2 | per-block gates + 1 shared scheduler actor | 1 total | ✅ | ✅ (but bidirectional coupling + lifecycle + fan-out msgs) |
| B | runtime central `ServicePropertyHandler` | 1 total | ❌ (outside SDK) | ❌ (mock must replicate) |

Under the target workload (≤200 blocks, *few* properties ever exceed their interval, bursts concentrated in a handful of analog-IO blocks), the timer count for A1 is **a handful**, not N — gate *state* is per-property and cheap, but a *timer* is armed only for a property actively changing faster than its interval. A2's single-timer win (handful → 1) doesn't pay for its global component, bidirectional block↔scheduler coupling, lifecycle cleanup, and per-burst `RegisterFlush`/`FlushDue` messages. B forfeits TestKit coverage and DevHost parity (the priorities here) and needs policy shipped across the bind boundary. A1 is also idiomatic — it's the same self-scheduling pattern as `[Timer]` and the persistence save.

The gate is written **scheduler-agnostic** (it returns `Hold(deadline)`; it does not own a timer), so if simultaneous-burst count ever explodes, swapping per-block self-scheduling for A2's shared scheduler is a contained change at one call site. We do not build A2 now (YAGNI).

## Behaviour details

- **Initial publish is never throttled.** `ServiceBinder.PublishInitialStateUpdates` (startup) emits immediately; `lastEmitAt` seeds to start time.
- **Value-*cleared* bypasses the gate.** `ServicePropertyValueCleared` / `…MeasuringPointValueCleared` ([LogicBlockBase.cs:495](../../Vion.Dale.Sdk/Core/LogicBlockBase.cs#L495)) emit immediately and cancel any pending flush — a clear is a state-significant edge.
- **Drain on stop.** On `Stopping`, flush all pending values so the outside world sees the final state.
- **`Immediate` bypasses both** throttle and deadband — the escape hatch for safety/error flags.
- **Measuring points included** — same policy surface; measuring points remain read-only.
- **`WriteOnly` properties unaffected** — they publish a redaction sentinel, not values; no rate concern.
- **Persistence unaffected** — the periodic save records the property's *current* value, independent of emission timing.
- **Ordering** — last-value-wins per property is preserved (the gate holds only the latest).

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
- **Internal (v1):** `Throttler` / `Throttler<T>`, `ThrottlePolicy`, the `EmitNow`/`Hold`/`Drop` decision — ride under the "Throttle" umbrella; finalised when promoted to public in v1.1.

## Introspection / schema exposure

The effective policy is surfaced per property in the introspection schema (plumbed through `PropertyMetadataBuilder`, same path as `Minimum`/`Maximum`):

```json
"throttle": { "minInterval": "250ms", "minChange": "0.1", "immediate": false }
```

Consumed by the DevHost UI (badge + rate display) and available to the dashboard / agents.

## Testing

- **TestKit default: policy OFF** (deterministic) — otherwise emission-count assertions like `VerifyServicePropertyChangedWithTimes(Exactly(2))` ([LogicBlockTestContextShould.cs:98](../../Vion.Dale.Sdk.TestKit.Test/LogicBlockTestContextShould.cs#L98)) would coalesce. Opt in with `WithEmissionPolicy(FromAttributes)` to test the policy itself.
- Add `VerifyServicePropertyEmitted(...)` (policy applied) **alongside** the existing `VerifyServicePropertyChanged(...)` (raw INPC) — they answer different questions.
- The trailing-edge flush uses `SendToSelfAfter`, so `AdvanceTime` drives it through the same machinery as RFC 0001's virtual time.

## DevHost

- **Policy ON by default** (matches production).
- Surface a per-property **emission rate** so a developer *sees* an uncontrolled input flooding and adds `MinInterval` deliberately (rate is not knowable statically, so this belongs in the UI, not an analyzer).
- A per-property and global **"ignore policy"** toggle for debugging transient transitions.

## Analyzers (DALE0xxx)

- `MinChange` on a numeric / `TimeSpan` → parse-check the string for that type's format.
- `MinChange` on a type with **no resolvable `IChangeThreshold<T>`** → error.
- `MinChange` on `bool` → error (no magnitude).
- `MinInterval` unparseable or below a floor (e.g. < 1 ms) → error / warning.
- `Immediate = true` together with `MinInterval` / `MinChange` → warning (ignored).
- `MinChange` set but `MinInterval` omitted → info (deadband-without-throttle is valid).

## Naming

"Throttle" is the umbrella term — most universally understood, domain-neutral (the SDK is not energy-specific), and our exact leading-edge-plus-trailing semantics match **lodash `throttle`**. (Note: Rx confusingly names leading-edge throttling `Sample` and uses `Throttle` for debounce — we follow common/lodash usage.) The control-engineering term **deadband was rejected** as domain jargon in favour of `MinChange` / `IChangeThreshold<T>`. Knob names stay plain and self-describing (`MinInterval`, `MinChange`, `Immediate`) rather than embedding "Throttle".

## Adjacent throttling/rating areas (out of scope)

This RFC throttles **one** point in the pipeline — property *emission* (block → publisher). Two neighbouring points have their own rate-control needs and are **explicitly out of scope**; they are noted here so they aren't conflated or forgotten.

### 1. Input side — service-provider event forwarding (the uncontrolled-input risk)

A HAL service provider forwards each input change to **every** linked block as a contract message — e.g. `actorContext.SendTo(logicBlockActorRef, new ContractMessage<AnalogInputChanged>(…))` ([MockHalAnalogInputHandler.cs:68](../../Vion.Dale.DevHost/Mocking/MockHalAnalogInputHandler.cs#L68)), same for `DigitalInputChanged`. A flooding analog input therefore (a) crosses HAL→block per change, and (b) runs each block's contract handler + logic per change. **Emission throttling only caps the resulting output** — it does not prevent the wasted ingestion or per-event computation.

True protection against uncontrolled input wants **sampling/deadband at the service-provider boundary** (the single HAL handler, *before* fan-out — structurally the same "central" position as Approach B, but for inputs). It would reuse the same `Throttler<T>` / `IChangeThreshold<T>` primitives but with a different configuration surface (on the IO contract / HAL handler). Because input forwarding is contract-message-based, it is doubly out of scope here. **Recommended follow-up: a separate RFC for input-side sampling.**

### 2. Downstream — the MQTT publish layer (runtime)

The runtime's single `ServicePropertyHandler` (`IMqttHandlerActor`) publishes each update to MQTT (typically retained). Emission throttling reduces the **number of updates generated at the source** — the cheapest win, and it benefits MQTT for free (fewer publishes, less retained churn, less uplink traffic on metered/cellular links). Concerns that remain the **runtime's** to own (Approach B territory, not the SDK):

- **Connection-aware coalescing** — hold latest-per-property while disconnected, flush on reconnect. The block-side gate can't see connection state.
- **Outbound backpressure / socket rate-limit** on metered links; **packet batching**; per-property **QoS / retain** policy.

Emission throttling and MQTT-layer control are complementary; if both exist, emission throttling runs first (less work reaches the publisher).

### 3. Sender-side StateUpdate / command throttling

Contract messages are not auto-throttled (timer/request-driven; can't be skipped). For the one case a sender *wants* it — a `StateUpdate` on a hot path — the **same `Throttler<T>` + `IChangeThreshold<T>` primitives** are exposed for explicit, opt-in use (v1.1). This also subsumes the consumer's bespoke `EmitConfigStateUpdate`-style guard-and-project boilerplate with a reusable utility.

## Backwards compatibility

- **Default-on throttling is a behaviour change** (≤4 Hz/property, value-equality dedup). Accepted given the closed, known user base; called out in release notes. A property needing the old behaviour sets `MinInterval = "0"` or `Immediate = true`.
- Attribute additions are purely additive; `Minimum`/`Maximum`/etc. unchanged.
- Metalama `[Observable]` is untouched — the gate sits downstream of it.
- `IChangeThreshold<T>` and the TestKit additions are additive.

## Interaction with RFC 0001 / 0002

The gate's trailing-edge flush flows through `ActorContext.SendToSelfAfter` — the same primitive RFC 0001 virtualises via `FakeTimeProvider`. `AdvanceTime` / `FlushPendingActions` therefore drive throttling deterministically with no extra machinery. If the handler TestKit of RFC 0002 lands an `ActorTestContextBase`, the emission-policy tests reuse it unchanged.

## Future work

- **A2 scale-out** — per-block gates + a single shared scheduler actor, if simultaneous-burst count ever justifies one timer total. The gate is already scheduler-agnostic; this is a localized swap.
- **Member-level `[MinChange]` on struct fields** — source-generate the `IChangeThreshold<T>` (the generator already exists). Deferred until real demand.
- **Transition-aware throttle** — carry `(value, transitionsInWindow)` so a fast `false→true→false` flips are not just resolved to the final value. Requires a wire-format change; v2.
- **Per-installation / per-class config** — runtime overrides keyed by `(block, property)`. Re-add when a real multi-site tuning need appears.
- **Promote `Throttler<T>` to public** (v1.1) for sender-side use (Adjacent area 3).
- **Input-side sampling RFC** (Adjacent area 1).

## Decisions log

| # | Decision |
|---|---|
| 1 | Throttle = leading-edge (immediate first change), trailing flush of latest; never debounce. |
| 2 | Lives in `LogicBlockBase` (Approach **A1**), per-block self-scheduled single flush. |
| 3 | Plain C# in the SDK — **no custom Metalama aspect** (sidesteps Metalama commercial-aspect licensing). |
| 4 | Default policy **on**: `MinInterval = 250ms`, value-equality floor on, deadband off. |
| 5 | Knobs `MinInterval` / `MinChange` / `Immediate` as strings; on both SP & MP attributes. |
| 6 | One deadband mechanism: value-equality floor + `MinChange` + type-resolved `IChangeThreshold<T>` (built-ins for numeric/`TimeSpan`). |
| 7 | Contract messages excluded; sender-side throttling via the (v1.1) public `Throttler<T>`. |
| 8 | TestKit policy off by default; DevHost on + emission-rate display. |
| 9 | `Throttler<T>` internal for v1; `IChangeThreshold<T>` public. |
| 10 | Naming: "Throttle" umbrella; "deadband" rejected as domain jargon. |
