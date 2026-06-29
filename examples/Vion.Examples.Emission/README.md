# Vion.Examples.Emission

A one-block showcase of the **RFC 0004 emission policy** — the throttle / deadband / dedup knobs you put on
`[ServiceProperty]` and `[ServiceMeasuringPoint]` to cut MQTT chatter from fast-moving telemetry. `SensorBlock`
exposes one member per knob, and two of them are **writable** so you can drive the gate by hand in the DevHost UI.

## The knobs

| Member | Drive? | Knobs | DevHost badge | Shows |
|---|---|---|---|---|
| `Setpoint` | writable | `MinInterval="0", MinChange="0.5"` | `deadband Δ0.5` | deadband only — sub-Δ0.5 changes are dropped |
| `Current` | writable | `MinInterval="0", MinChange="0.25"` + custom threshold | `deadband Δ0.25` | a custom `IChangeThreshold<ThreePhase>` for a non-built-in type |
| `Temperature` | read-only | `MinInterval="2s", MinChange="0.5"` | `throttle 2s · Δ0.5` | throttle **and** deadband together |
| `ThrottledEcho` | read-only | `MinInterval="3s"` | `throttle 3s` | time-throttle only (echoes `Setpoint`) |
| `LiveTick` | read-only | `Immediate=true` | `immediate` | bypass — emits on every change |
| `Power` | read-only | property `2s`; measuring-point `500ms + Δ1` | two badges | one value, two **independently** throttled streams |
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

1. **Deadband (interactive):** type into `Setpoint` — `25.0` flashes, `25.3` (below Δ0.5) is dropped with no flash,
   `25.6` flashes again. Same for `Current`: nudge a phase by < 0.25 → dropped; by ≥ 0.25 → emitted.
2. **Throttle (watch it coalesce):** `Temperature`, `ThrottledEcho`, and `Power` move every second but only emit on
   their interval — watch them hold and update in steps, next to `LiveTick` flashing every single tick.
3. **Drive the echo:** change `Setpoint` and watch `ThrottledEcho` follow it, but lagging on its 3 s throttle.

There is no slider in the current UI (the number field commits one write on blur/Enter), so the *time throttle* is
best watched on the auto-moving signals rather than hand-typed; the *deadband* is the fully hand-drivable one.

## Verify it deterministically

```bash
dotnet test examples/Vion.Examples.Emission/Vion.Examples.Emission.Test
```

`SensorBlockShould` forces the policy on under a fake clock and asserts the gate: deadband drops sub-threshold
changes, the dedup floor drops equal values, `Immediate` bypasses, the custom threshold resolves, and `Power`'s two
streams throttle independently.

## See also

- [RFC 0014 — Emission-policy showcase](../../docs/rfcs/0014-emission-showcase-example.md)
- [RFC 0004 — Emission policy](../../docs/rfcs/0004-emission-policy.md)
