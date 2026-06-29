# Vion.Examples.Emission

A one-block showcase of the **RFC 0004 emission policy** — the throttle / deadband / dedup knobs you put on
`[ServiceProperty]` and `[ServiceMeasuringPoint]` to cut MQTT chatter from fast-moving telemetry.

**Emission policy governs the outbound direction** — how a block re-publishes its *own* measured state. A
write *into* a writable property is always forwarded (applied immediately), so throttle/deadband belong on
**read-only computed/sensed values**, not on operator inputs. `SensorBlock` reflects that: `Setpoint` is a
plain writable input, and every gated member is a read-only reading.

## The members

| Member | Direction | Knobs | DevHost badge | Shows |
|---|---|---|---|---|
| `Setpoint` | writable input | *(none)* | *(no badge)* | a plain operator input — writes always forwarded |
| `Reading` | read-only (tracks setpoint) | `MinInterval="0", MinChange="0.5"` | `deadband Δ0.5` | deadband — sub-Δ0.5 moves suppressed on the wire |
| `PhaseCurrents` | read-only (tracks setpoint) | `MinInterval="0", MinChange="0.25"` + custom threshold | `deadband Δ0.25` | a custom `IChangeThreshold<ThreePhase>` for a non-built-in type |
| `Temperature` | read-only (sensed) | `MinInterval="2s", MinChange="0.5"` | `throttle 2s · Δ0.5` | throttle **and** deadband together |
| `Power` | read-only (sensed) | property `2s`; measuring-point `500ms + Δ1` | two badges | one value, two **independently** throttled streams |
| `LiveTick` | read-only | `Immediate=true` | `immediate` | bypass — emits on every change |
| `SampleCount` | read-only | *(none)* | *(no badge)* | the default 250 ms throttle — introspection omits the badge |

## Seeing it act — run in **free-run** (real clock)

```bash
dale dev          # real clock — emission policy is ACTIVE
```

> The emission policy is **intentionally off under the stepped / deterministic clock** (so scenarios stay exact).
> Do **not** use `dale dev --stepped` for this demo — you'd see nothing gated. The unit tests re-enable the policy
> explicitly via the TestKit (`WithEmissionPolicy(FromAttributes)`).

In the dashboard, a value chip **flashes only when a value passes the gate** — that flash is your tell for
"emitted vs. suppressed".

1. **Deadband (interactive):** drive `Setpoint` — it updates instantly (writes are forwarded). The read-only
   `Reading` tracks it, but **holds** when you nudge `Setpoint` by less than 0.5 (the sub-threshold re-emission is
   suppressed) and jumps once a change clears Δ0.5. `PhaseCurrents` does the same at Δ0.25. This is the whole point:
   the write lands, the *re-emission* is deadbanded.
2. **Throttle (watch it coalesce):** `Temperature` and `Power` move on their own every second but only emit on
   their interval — watch them hold and update in steps, next to `LiveTick` flashing every single tick.

There is no slider in the current UI (the number field commits one write on blur/Enter), so the *throttle* is best
watched on the auto-moving sensed signals; the *deadband* is the one you drive by hand via `Setpoint`.

## Verify it deterministically

```bash
dotnet test examples/Vion.Examples.Emission/Vion.Examples.Emission.Test
```

`SensorBlockShould` forces the policy on under a fake clock and asserts the gate on the read-only readings:
deadband drops sub-threshold moves, the dedup floor drops unchanged values, `Immediate` bypasses, the custom
threshold resolves, and `Power`'s two streams throttle independently.

## See also

- [RFC 0014 — Emission-policy showcase](../../docs/rfcs/0014-emission-showcase-example.md)
- [RFC 0004 — Emission policy](../../docs/rfcs/0004-emission-policy.md)
