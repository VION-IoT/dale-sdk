# Vion.Examples.Gating

A **dashboard-UI test fixture** for **config-time structural gating** — it exercises *every*
gateable member kind, driven by a **number**, an **enum**, and a **string** `[InstantiationParameter]`, and
ships a topology matrix that shows each in both its **included** and **excluded** state so you can eyeball how
the dashboard renders (or omits) it.

Two blocks:

- **`ChargingStationBlock`** — an EV charging station whose shape is chosen at config time.
- **`ChargeController`** — a **second block** wired to the station's charge points over an inter-block
  interface, so gated **interface mappings** appear and disappear with the station's parameters.

## What is gated, by what

| Member | Gateable kind | Gate driver | Predicate |
|---|---|---|---|
| `Point2`, `Point3` | service **component** | number | `ChargePointCount >= 2` / `>= 3` |
| `Point2_IChargePoint`, `Point3_IChargePoint` | **interface binding** (to `ChargeController`) | number | same (each point is also an `IChargePoint` endpoint) |
| `Bay2Contactor` | **IO output** (`IDigitalOutput`) | number | `ChargePointCount >= 2` |
| `LoadManagement` | service **component** | enum | `Model in ['Plus', 'Pro']` |
| `GridFrequencyGuard` | **IO input** (`IDigitalInput`) | string | `Region in ['EU', 'UK']` |
| `Point1`, `MainContactor` | component / IO output | — | ungated (always present — the "included" baseline) |

`Region` (string), `Model` (enum) and `ChargePointCount` (int) are the three `[InstantiationParameter]`s. All
three ride as **wire-read-only** service properties on the root service, so the chosen values are visible in
the dashboard but not editable at runtime — they're config, not state.

**Structural, not cosmetic.** A gated-out member does not exist: no service, no interface endpoint, no IO
binding, no MQTT topic, no persistence. This is the hard-existence sibling of
`[Presentation(VisibleWhen = …)]`, which only *hides an existing member* in the UI. `[IncludedWhen]` gates
whole **bound units** (components, interface bindings, contract/IO bindings) — never a lone scalar; for that,
see `VisibleWhen` in [Vion.Examples.Presentation](../Vion.Examples.Presentation). Gated-out
`[ServiceProviderContractBinding]` (IO) properties are **`null`** at runtime (the binder constructs them), so
fan-out code null-guards them (`Bay2Contactor?.Set(…)`).

## The topology matrix — switch these live in the DevHost

`dale dev` boots `default`; use the topology switcher (⌘K) to flip between these and watch the surface change:

| Topology | count / model / region | Included | Excluded |
|---|---|---|---|
| `single-basic` | 1 / Basic / US | Point1 (+ interface), MainContactor | Point2/3, LoadManagement, Bay2Contactor, GridFrequencyGuard |
| `default` | 2 / Basic / EU | Point1/2 (+ interfaces), Bay2Contactor, GridFrequencyGuard, MainContactor | Point3, LoadManagement |
| `duo-plus` | 2 / Plus / UK | `default` **+ LoadManagement** (isolates the enum gate) | Point3 |
| `full-pro` | 3 / Pro / EU | **everything** — all 3 points/interfaces, both contactors, grid guard, load mgmt | — |

Comparisons that isolate one gate:
- **number** — `single-basic` → `default` → `full-pro` grows the point components, their interfaces, and the bay-2 contactor.
- **enum** — `default` (Basic) vs `duo-plus` (Plus, same count) — only `LoadManagement` appears.
- **string** — `single-basic` (US) vs any EU/UK topology — only `GridFrequencyGuard` (the IO input) appears.
- **interface mapping** — `single-basic` maps only `Point1`; `full-pro` maps all three.

> **Mapping to a gated-out endpoint is tolerated, not honoured.** A topology `interfaceMapping` whose target
> interface is gated out (e.g. mapping the `ChargeController` to `Point2` while `ChargePointCount` is 1) is
> accepted at load time and appears in the exported config. At runtime the link is skipped and any message
> routed to it is dropped with a warning — the receiving block stays up, exactly as it does for a contract
> mapping to a gated-out contract. Nothing arrives, so the wire is dead rather than broken: the editor flags
> it, and every topology here maps only to points the count includes.

## Verify it deterministically

```bash
dotnet test examples/Vion.Examples.Gating/Vion.Examples.Gating.Test
```

`ChargingStationBlockShould` (16 cases) applies each parameter via `WithInstantiationParameter` and asserts:
number-gated components/measuring-points publish exactly N (counts 1/2/3); the `Bay2Contactor` IO output is
non-null/drivable when included and **null when excluded**; the ungated `MainContactor` is always bound; the
`GridFrequencyGuard` IO **input** is present only in EU/UK; and `LoadManagement` binds only for Plus/Pro. The
gated inter-block **interface mappings** are cross-block wiring, verified end to end through the DevHost.

## See also

- [Config-time structural gating](../../docs/specs/config-gating.md) — the spec page this example demonstrates
- [The introspection document](../../docs/specs/introspection.md) — presentation-time visibility, the cosmetic sibling of a gate
