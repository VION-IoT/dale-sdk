# RFC 0014 — Emission-policy showcase example + re-release examples at v0.9.4

> Status: proposed · 2026-06-29
> Type: **implementation / release plan** (not an architecture change — RFC 0004 already
> designed the emission policy; this RFC plans a *showcase example* for it plus a routine
> version re-release). Filed under `docs/rfcs/` to keep design docs committed and discoverable.

## Background

RFC 0004 (emission policy: throttling / deadband / dedup) shipped in **v0.9.0-preview.7 / v0.9.0**
and is fully present in the latest tag **v0.9.4** (which is also current `main` HEAD). The follow-up
fixes are all in v0.9.4 too: DF-33/DF-34 (interface-declared throttle + custom `IChangeThreshold<T>`,
#101), DF-35 (interface-inherited throttle in introspection, #102), and #104 (independent per-stream
gating for dual-annotated members).

Two gaps motivate this work:

1. **Version skew.** All six examples (`Energy`, `ModbusRtu`, `PingPong`, `ToggleLight`, `RichTypes`,
   `Presentation`), the first-party library `Vion.Diagnostics`, and the `dale new` template are still
   pinned at **`0.9.0-preview.6`** — one preview *before* emission policy existed, four releases behind,
   and still resolving `Vion.Contracts 1.1.0` transitively. They have been stuck here since PR #95.
2. **No example demonstrates emission policy.** None of the examples use any emission knob. A plain
   version bump silently gives every property the default 250 ms throttle, but introspection *omits*
   the badge when the policy equals the default — so nothing is visibly demonstrated.

## Goals

- Re-release all examples, the `Vion.Diagnostics` library, and the template at **v0.9.4**.
- Add one new example, **`Vion.Examples.Emission`**, that is the canonical, self-documenting
  reference for RFC 0004 — every member-level knob, each with a visible DevHost badge and a
  deterministic behavioral test.

## Non-goals

- **Not** adopting emission policy into the existing examples (decided: *new example only*).
- **Not** demonstrating interface-inherited throttle (DF-33/DF-35) — deliberately scoped out to keep
  the example one focused block (decided). Member-level knobs only.
- **Not** cutting a new SDK release — v0.9.4 already contains everything needed.
- No SDK/runtime source changes. No analyzer/introspection/DevHost changes.

## Part 1 — Re-release at v0.9.4

Single mechanical step, but with real verification weight:

```pwsh
pwsh scripts/set-version.ps1 -Version 0.9.4
```

This rewrites, across the whole repo, every `Vion.Dale.*` `PackageReference` and the own-`<Version>`
of the example/library main projects from `0.9.0-preview.6` → `0.9.4`, covering the 6 examples, the
template (3 projects), and `Vion.Diagnostics` (3 projects). The new `Vion.Examples.Emission` projects
are authored directly at `0.9.4`, and once registered in `set-version.ps1` (Part 2) future bumps cover
them automatically.

**Risk:** this is a 4-release jump that pulls `Vion.Contracts 1.1.0 → 2.1.0` transitively across the
whole `Vion.Dale.Sdk.sln`. It is gated on a full solution build + test at 0.9.4 (see Verification).
**Prerequisite:** restore must reach the private Azure DevOps feed where 0.9.4 is published.

No existing example behavior changes — they only gain the (invisible-by-design) default 250 ms throttle.

## Part 2 — `Vion.Examples.Emission`

### Idiom and structure

Mirrors the `RichTypes`/`Presentation` showcase idiom (one timer-driven block, one property per knob,
XML-doc on each member explaining what it demonstrates, grouped) and the `ToggleLight` project skeleton.
References only `Vion.Dale.Sdk` (no I/O packages) + the two netstandard2.1 shims; the Test project
references only `Vion.Dale.Sdk.TestKit`.

```
examples/Vion.Examples.Emission/
  Vion.Examples.Emission/
    Vion.Examples.Emission.csproj           # netstandard2.1; PackageId/Version=0.9.4; Vion.Dale.Sdk + IsExternalInit + RequiredMemberAttribute shims; IsPackable=false; EmitCompilerGeneratedFiles
    DependencyInjection.cs                   # IConfigureServices -> services.AddTransient<SensorBlock>()
    LogicBlocks/SensorBlock.cs               # the showcase block (one property per knob)
    Emission/ThreePhase.cs                   # readonly record struct (custom-threshold value type)
    Emission/ThreePhaseChangeThreshold.cs    # IChangeThreshold<ThreePhase>, parameterless ctor (DF-34 discovery)
  Vion.Examples.Emission.DevHost/
    Vion.Examples.Emission.DevHost.csproj    # net10.0; Exe; Vion.Dale.DevHost.Web; ProjectReference to library
    Program.cs                               # ToggleLight's verbatim, namespace swapped; port 5000
  Vion.Examples.Emission.Test/
    Vion.Examples.Emission.Test.csproj       # net10.0; Vion.Dale.Sdk.TestKit + xunit.v3 stack
    SensorBlockShould.cs                     # deterministic throttle/deadband/immediate/dedup/custom-threshold assertions
  Vion.Examples.Emission.sln                 # flat 3-project sln (Debug/Release|Any CPU)
  topologies/default.topology.json           # single SensorBlock instance
  scenarios/emission.scenario.json           # validates value-production logic (+ scenarios/.dale/scenario.schema.json)
  README.md                                  # short getting-started + knob table + free-run drive walkthrough (clock-mode, Setpoint deadband steps, watch-the-flash)
```

### `SensorBlock` — one property per emission feature

A synthetic sensor driven by `[Timer(1)]` (1 s ticks) whose read-only values are **deterministic
functions of tick count** (sine + a deterministic secondary-frequency "jitter"), so scenarios reproduce
exactly while still giving throttle/deadband something to act on in free-run. Throttle intervals are set
**longer than the 1 s tick** so the gate visibly coalesces multiple ticks into one emission. Two members
are **writable** so the gate can be exercised interactively in the DevHost UI (see "Seeing it live").

> Verify the `[Timer]` minimum granularity during implementation; the rule is *tick interval < throttle
> interval* so coalescing is visible. If 1 s is the floor, the 2–3 s throttles below hold 1–2 ticks each.

| Property | W/R | Knobs | DevHost badge | Demonstrates / interaction |
|---|---|---|---|---|
| `Setpoint` | **writable** | `MinInterval="0", MinChange="0.5"` | `deadband Δ0.5` | **interactive deadband** — type sub-Δ0.5 values → dropped (no flash); type ≥Δ0.5 → flashes |
| `Temperature` | read-only | `MinInterval="2s", MinChange="0.5"` | `throttle 2s · Δ0.5` | throttle **and** deadband together (auto, timer-driven) |
| `ThrottledEcho` | read-only | `MinInterval="3s"` | `throttle 3s` | time-throttle only; echoes `Setpoint` + noise each tick → drive `Setpoint`, watch the echo lag; also auto-moves |
| `LiveTick` | read-only | `Immediate=true` | `immediate` | ungated bypass — flashes every tick, the visual contrast to `ThrottledEcho` (doc-note: also the right knob for a bool safety flag, which has no magnitude to deadband) |
| `SampleCount` | read-only | *(none)* | *(no badge)* | the default policy is already 250 ms; documents the invisible default |
| `Power` | read-only | dual-annotated; property `MinInterval="2s"`, measuring-point `MinInterval="500ms", MinChange="1"` | two badges | **independent per-stream** throttling (the #104 fix) |
| `Current` | read-only | `MinChange="0.25"` + registered `IChangeThreshold<ThreePhase>` | `deadband Δ0.25` | **custom** change-threshold discovery (DF-34) |

All knobs use valid duration/threshold tokens so the example stays clean of the DALE034–DALE039
analyzer diagnostics.

### Seeing it live in the DevHost UI

The RFC 0004 gate sits on the same in-process change message that feeds the DevHost observe panel (it is
**not** MQTT-publish-only): a held or dropped value emits no `ServicePropertyValueChanged`, so the value
chip simply does not update — and the chip's 500 ms flash fires **only** when a value passes the gate.
That makes the flash the visual tell for "emitted vs. suppressed". Verified:
[`LogicBlockBase.cs:780-813`](../../Vion.Dale.Sdk/Core/LogicBlockBase.cs),
[`store.js:439-459`](../../Vion.Dale.DevHost.Web/wwwroot/store.js).

**This only works in free-run / real-clock mode.** Emission policy is force-disabled under the controllable
(stepped) clock — `_emissionPolicyActive = !ControllableClock.Detect(timeProvider)`
([`LogicBlockBase.cs:179-180`](../../Vion.Dale.Sdk/Core/LogicBlockBase.cs)) — so the README and the live
smoke must use free-run (`dale dev` without `--stepped`, or the RFC 0012 clock-mode toggle). The
`emission.scenario.json` (stepped clock) therefore validates value-production logic only, **not** gate timing.

Two interactions the example exposes:

- **Deadband (crisp, fully interactive):** drive `Setpoint` in its number field — `25.0` flashes, `25.3`
  (< Δ0.5) is dropped with no flash, `25.6` flashes. A driven write goes through the same gate as any
  internal change ([`DevHostControl.cs:287-330`](../../Vion.Dale.DevHost/Control/DevHostControl.cs)).
- **Throttle (auto-driven + echo):** the timer mutates `Temperature`/`ThrottledEcho`/`Power` every second
  while their 2–3 s throttles emit less often — watch the chips hold and flash on the coalesced cadence,
  next to `LiveTick` (`Immediate`) flashing every tick. Driving `Setpoint` also feeds `ThrottledEcho`, so
  a burst of edits shows up gated.

**Constraint (documented, not worked around):** the SPA renders `uiHint=slider` as a plain number field
that commits one write on blur/Enter — there is no slider drag, so a *time throttle* cannot be exercised by
rapid hand-typing. Hence the throttle demo relies on the timer; the deadband demo is the hand-driven one.
The `Vion.Dale.DevHost.SmokeHost` `ShowcaseBlock.Setpoint` (writable, `MinInterval=1s, MinChange=Δ0.1`)
already proves this interaction pattern.

### Three teaching surfaces

Emission policy is force-*disabled* under a fake clock (so deterministic stepping/scenarios stay exact).
Therefore the example teaches the feature three ways:

1. **Live in the DevHost UI (free-run)** — badges (always visible) plus the gate acting on real values:
   the deadband demo is fully interactive (drive `Setpoint`), the throttle demo is timer-driven, and the
   chip flash marks emitted-vs-suppressed. Detailed in "Seeing it live" above.
2. **TestKit behavioral tests** (`SensorBlockShould.cs`) — the TestKit override that forces emission
   policy *on* under a controllable clock lets us **deterministically assert** the runtime behavior:
   throttle holds latest-wins and flushes at the interval; deadband drops sub-threshold moves; `Immediate`
   bypasses; the always-on dedup floor drops equal values; the custom `IChangeThreshold<ThreePhase>` fires;
   and `Power`'s two streams emit at independent cadences. This makes the knobs *verifiable*, not just visible.
3. **README** — a knob table mirroring the one above plus the step-by-step free-run drive walkthrough
   (clock-mode note, the `Setpoint` deadband steps, the watch-the-flash cue).

The `emission.scenario.json` validates the block's **value-production logic** (deterministic tick-driven
values), exactly like `rich-types.scenario.json` does — it does **not** assert throttle timing (policy is
off under the scenario clock).

> **Assumption to verify in planning:** the exact TestKit API that forces emission policy on under a fake
> clock (surveyed as `WithEmissionPolicy(FromAttributes)`). This is the one API not yet read first-hand;
> confirm the name/shape against `Vion.Dale.Sdk.TestKit` before writing `SensorBlockShould.cs`. If it
> differs, adapt the test; the rest of the design is unaffected.

### Wiring (4 must-update registration sites)

1. **`Vion.Dale.Sdk.sln`** — add the 3 projects under an `examples` solution folder. Use
   `dotnet sln Vion.Dale.Sdk.sln add <csproj>` to get correct GUIDs/config rows, then nest under the
   `examples` folder to match the other examples.
2. **`scripts/set-version.ps1`** — add the 3 csproj to `$exampleProjects` (library → `Vion.Dale.Sdk`;
   DevHost → `Vion.Dale.DevHost.Web`; Test → `Vion.Dale.Sdk.TestKit`) and the library csproj to
   `$exampleMainProjectsWithVersion`.
3. **`scripts/pack-examples.ps1`** — add the library csproj to `$projects` (pack/publish it, like
   `RichTypes`).
4. **Per-example `Vion.Examples.Emission.sln`** — flat 3-project sln, Debug/Release|Any CPU only.

## Verification (full)

1. `pwsh scripts/set-version.ps1 -Version 0.9.4` then `git diff` review.
2. `dotnet build Vion.Dale.Sdk.sln` and `dotnet test Vion.Dale.Sdk.sln` — whole solution green at 0.9.4
   (this is the real gate for the version jump / contracts 2.1.0).
3. New example: `dotnet test` on `Vion.Examples.Emission.Test` (throttle/deadband/immediate/dedup/custom
   threshold all pass), and run `emission.scenario.json` under deterministic stepping.
4. **Live DevHost smoke (Tier 2, chrome-devtools), in free-run / real-clock mode** — boot
   `Vion.Examples.Emission.DevHost`, confirm the badges render as specified (`deadband Δ0.5` on `Setpoint`,
   `throttle 2s · Δ0.5` on `Temperature`, `throttle 3s` on `ThrottledEcho`, `immediate` on `LiveTick`, two
   badges on `Power`, `deadband Δ0.25` on `Current`, no badge on `SampleCount`). Then **exercise the gate**:
   drive `Setpoint` with a sub-Δ0.5 step and confirm it is dropped (no flash) while a ≥Δ0.5 step flashes;
   confirm the timer-driven throttled chips hold/coalesce while `LiveTick` flashes every tick. Use the
   `devhost-smoke` skill. (Skip nothing to stepped mode — the gate is off there by design.)
5. `pwsh scripts/cleanup-code.ps1 -Changed`, review `git diff`, commit, before opening the PR.

## Risks

- **Version-jump build breakage** (contracts 1.1.0 → 2.1.0 across the solution). Mitigation: full
  build+test gate; if a real API break surfaces, fix the affected example minimally and note it.
- **Feed access** for 0.9.4 restore. Mitigation: confirm the private feed is configured before building.
- **TestKit emission-override API name** (above). Mitigation: verify first thing in implementation.
- **CI snapshot bot** may regenerate CLI help / PublicApi snapshots on the branch — reconcile before
  pushing, never force-push over it (per project memory).

## Out of scope / follow-ups

- Realistic adoption of emission policy into `Energy` / `ModbusRtu` / `Vion.Diagnostics` (the "hybrid"
  option) can follow later if desired.
- Interface-inherited throttle (DF-33/DF-35) demonstration.
