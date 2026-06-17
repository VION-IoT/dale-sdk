# RFC 0008: Unified scenario + topology architecture — one deterministic substrate

Status: **Draft** (brainstorming/design output; pending maintainer review. May land as-is, or be split into an RFC 0006 v2 revision + a new deterministic-stepping RFC — see §10.)
Author: jonas.bertsch. Date: 2026-06-16.
Extends: RFC 0003 (headless DevHost control), RFC 0006 (scenario files). Primary repo: `dale-sdk`; first consumer: `logic-block-libraries` (`Ecocoach.EnergyManagement`).

---

## 1. Summary

Collapse today's **two scenario substrates** (virtual-time single-SUT TestKit vs wall-clock real-wired DevHost) and **three topology definitions** (C# preset, exported JSON mirror, TestKit `TopologyPreset`) into **one data-first artifact running on one deterministically-steppable real-wired substrate**.

The keystone is a new SDK capability: a **deterministic stepping mode** for the live actor network — built by generalizing machinery that already exists (DI-injected `TimeProvider`, the `IDelayedSendGate` delayed-send choke point, RFC 0005 mailbox vitals). With it, the *same* `*.scenario.json` artifact runs **exact-deterministic in CI** and **interactively in the Player**, authored once by both humans and agents.

Two principles run through the design:

- **SDK = mechanism, not methodology.** The SDK provides the stepper, the data model, the runner, name-path resolution, and the Player; it encodes **no** spec-driven-development process. Requirement↔scenario linkage is consumer tooling built on an optional free-form tag.
- **The user never touches DevHost `Program.cs` to manage topology.** Topology is defined by **files** or the **DevHost UI**. DI is the *catalog of what exists*; topology JSON is the *instance graph*. When no topology exists, the DevHost **auto-creates one by rules, writes it to disk transparently**, and the user may gitignore it.

---

## 2. Motivation — the current-state problems

Grounded in a full read of both repos (see `Vion.Dale.DevHost/Scenarios/*`, `Vion.Dale.DevHost/Topologies/*`, `Ecocoach.EnergyManagement.TestScenarios/*`, `Ecocoach.EnergyManagement.DevHost/Program.cs`, `Vion.Dale.Cli/Commands/ScenarioCommand.cs`).

### 2.1 "Topology" exists as three unsynchronized definitions

1. **JSON** (`topologies/*.topology.json`) — explicit `logicBlockInstances` + verbatim 4-tuple `interfaceMappings`. Loaded by `DevTopologyLoader`, switched live in the Player, used by headless CI (`HeadlessHost.Load`).
2. **C# DevHost preset** (`Program.cs` `Scenarios.*` + a hardcoded `BuildPreset` `switch`) — fluent `AddLogicBlock<T>().AutoConnect().WithTopologyName(id)`. The JSON files are **exported from** these (`dale dev --export-topology`), so the C# preset is the de-facto source of truth and the JSON is a derived mirror with **nothing enforcing sync**. Some presets carry real *logic* (the deliberately-unwired `all-surfaces` gallery; mutually-exclusive EM+Operator commanders; reservation's deliberate no-supplier).
3. **TestKit `TopologyPreset`** (`Scenarios/TopologyPresets.cs`) — a *different* abstraction: `em-superset` with abstract `Participant`/`ParticipantKind`, single-SUT where peers are **fed mocks** via `TestKitScenarioHarness`, not real blocks.

### 2.2 "Scenario" exists as two substrates that share vocabulary but nothing else

- **JSON `*.scenario.json`** — real wired DevHost, wall-clock, scalar/enum `waitUntil`, human `judge[]`. Already runs identically in the Player **and** headless CI (`CommittedScenariosShould`). So "same artifact, both ways" is *already true — but only within the real-wired tier*.
- **C# `Scenario`/`ScenarioScript` records** — virtual-time, single-SUT, axis matrices, typed auto-asserting `Claim`s, domain-vocabulary steps. Opposite substrate; composes one-way only (`ScenarioRunner.ApplyAsync` lets a *headless* C# test borrow a JSON arrange phase; the virtual-time records don't compose with JSON at all).

The entire rich declarative tier (axis matrices, `Claim` predicates, the interpreter harness) is **hand-built in the library, not provided by the SDK** — the SDK ships only the JSON/real-wired tier.

### 2.3 The four walls

- **Expressiveness** — scalar-only `waitUntil` (no struct field-paths like `AllocatedCurrent.L1`; rejected at `ScenarioFile.cs:423-426`), no auto-assert (`judge[]` is human-only), no logical-time steps, sequential-only → anything real falls back to a bespoke C# test.
- **Discovery** — the `Program.cs` preset selector is a hardcoded C# `switch`, *because* some presets need wiring logic, not data. Adding a topology means editing code.
- **Single-topology** — only `em-closed-loop` has scenarios; the editor schema + CI deep-validation are default-topology-only.
- **Traceability is static, not executable** — `specs`/`spec` ids are *counted* by `spec-trace.ps1`; there is no path from a requirement → run-with-concrete-values → observed → "fits / requirement must change."

---

## 3. Goals & non-goals

### Goals (in priority order, from the brainstorming forks)

1. **Unify the substrates** (north star) — one artifact, authored once, run both ways.
2. **DRY the topology** (close second) — a single source of truth; the developer **never edits `Program.cs`** to manage topology.
3. **Dual-author** — both agents (files) and humans (Player UI) can author and run.
4. **Executable traceability** — support the requirement→scenario→run→observe→judge loop, *as consumer tooling on an optional tag* (not baked into the SDK).

### Non-goals (explicitly out of scope)

- **Axis matrices as a C# engine** — accepted sacrifice. (May return later as a *data-level* `axes` field expanded by the runner — never as a parallel C# engine.)
- **SDD process in the SDK** — no assumptions about a requirements corpus, its layout, or AC↔scenario linking. The SDK offers an optional tag; the rest is consumer tooling.
- **Topology composition / nested sub-topologies** — cascading is *flat*: parent/child is `interfaceMappings` semantics + EM-internal logic, nothing structural.
- **First-class causality / message-ordering assertions** — the data model is state-after-settle; causality (if ever needed) goes through the C# escape hatch.
- **Single-SUT isolation for interaction scenarios** — interaction scenarios run the whole wired network by design; pure single-block math stays in the untouched unit tier.
- **A node-graph topology editor in the dev tool** — the production dashboard (`LogicEditor.vue`, VueFlow) owns heavy visual wiring; the dev tool gets a CLI generator + a lightweight UI affordance.
- **Auto-discovering referenced libraries' DI modules** — the one legit reason to edit `Program.cs` is adding a second `WithDi<OtherLib>()` (§6.3). An auto-discovery mechanism is possible but YAGNI for now.

---

## 4. Decisions (the converged forks + rationale)

| # | Decision | Rationale |
|---|---|---|
| D1 | **Collapse to one substrate: deterministically-steppable real-wired DevHost.** Retire the bespoke virtual-time single-SUT scenario engine *as the authoring form for new scenarios* (phased migration per D7 / §7 — existing matrices keep running until ported or frozen). | Maximum unification with least duplicated machinery; the determinism seams already exist (§6.1). Auto-resolves the stimulus-vocabulary problem (every stimulus is "set a name path") and the logical-time problem. |
| D2 | **Authoring form: data-first** (JSON, friendlier DSL optional later) **+ a narrow typed C# escape hatch.** | The dual-author requirement ("humans author/edit in the DevHost UI") rules out code-first as the *authoring* form. The escape hatch keeps the derived-semantic ~10% from stranding into a parallel test. |
| D3 | **DI is the catalog of available block types; topology JSON is the instance graph.** Topology is authored as files or in the Player UI — **never in `Program.cs`**. The C# topology presets (`AddLogicBlock` chains, `WithTopologyName`, the `BuildPreset` switch) are **deleted**. | `WithDi<DependencyInjection>()` (source-generated) already registers the assembly's blocks + services — that *is* "what exists." The topology declares "which instances + wiring." The C# preset was only a redundant mirror. |
| D4 | **Auto-create the default topology by rules, persisted to disk, transparently.** When no topology exists, the DevHost generates one (each DI-registered block instantiated once, AutoConnected), **writes `topologies/default.topology.json`**, and announces it. The user may edit, commit, or gitignore it. Authored files always win. | Zero-config hello-world without ever editing `Program.cs`; no ephemeral/in-memory topology, so "JSON is truth" holds end-to-end; the generated file is visible and self-correcting. |
| D5 | **Topology: JSON is the single source of truth for committed/named topologies; explicit wiring is first-class + validated. AutoConnect becomes a *generator* of `interfaceMappings`, not runtime magic.** | Reproducible, reviewable, dual-authorable wiring. AutoConnect keeps its "don't memorize interface identifiers" convenience but emits explicit JSON you review + commit (or that the auto-gen persists). |
| D6 | **Assertion model: state-after-settle (+ struct field paths, tolerances, relational A-vs-B).** No first-class causality/ordering in data. | Simplest vocabulary that covers the real needs; cascading is verified by settled end-state per level (quiescence handles propagation). |
| D7 | **Discovery from folders.** Topologies discovered from `topologies/`, scenarios from `scenarios/`. The `Program.cs` switch is gone. | Data files in a folder *are* the registry; no `IScenario`-reflection needed. |
| D8 | **SDK = mechanism; consumer = policy.** SDK ships the optional tag, runnable+observable outcomes, the verification report, and tag-based Player filtering. Requirement linking / coverage / SDD live in consumer tooling. | The SDK must not assume an SDD process exists or its shape. |
| D9 | **Migration: spike one matrix, then decide.** Unit tier untouched; existing TestKit matrices stay until a representative one is ported and the verbosity is judged. | De-risks both the stepper and the "matrices → explicit scenarios" claim before committing to a full port. |

---

## 5. The testing landscape (documentation deliverable)

A map of *all* test options, situating the unified scenario tier. This supersedes the "five meanings of scenario" framing in `TESTING.md`.

| Tier | Project | Substrate | Lane | Status after this design |
|---|---|---|---|---|
| **Unit (single-SUT TestKit)** | `*.Test` | one block, peers mocked, virtual-time `AdvanceTime`; exact numerics / K-rules / contract-shape | PR gate | **Unchanged** — different concern; stays |
| **Declarative matrix (TestKit records)** | `*.TestScenarios` | single-SUT virtual-time, axis matrices, typed `Claim`s | PR gate | **Retiring as an authoring form**; coverage migrates (§7) |
| **Unified scenario (real-wired, deterministic)** | `scenarios/*.scenario.json` + SDK runner | full wired network, **deterministically stepped**, data-first + C# escape hatch | PR gate (deterministic) **and** Player | **New** — absorbs the old wall-clock headless JSON tier and the wired-direction half of the matrix tier |
| **Integration (Modbus)** | `*.IntegrationTest` | real encode/decode, byte/word order | PR gate | **Unchanged** — stays |

Key shift: the old "headless wall-clock" tier (`CommittedScenariosShould`, nightly, flake-prone) **becomes deterministic and moves onto the PR gate** — that is the determinism upgrade, not a new lane.

The "scenario" overload is reduced from five meanings to three: (a) **unified scenario file** (the artifact), (b) the **SDK runner** that executes it, (c) **unit-tier imperative `*ScenariosShould` tests** (TestKit, unchanged). The C# `Scenario`/`ScenarioScript` records and their engine are on the retirement path.

### 5.1 The headless CLI evolves (it does not go away)

`dale dev --headless` (`DALE_DEVHOST_NO_BROWSER=1`, JSON readiness line — RFC 0003) + the `dale scenario` verbs are exactly the substrate the unified system runs on. The CLI stays a **thin HTTP/file client with no SDK dependency**; `ScenarioRunner` over `IDevHostControl` stays the one authoritative evaluator. What changes:

| Verb | Today | After |
|---|---|---|
| `dale dev --headless` | wall-clock host | **+ deterministic stepping mode** (clock + delayed-send gate + quiescence). Auto-gens + persists the default topology if none exists (§6.4) — so CI's `--export-config` and local `dale dev` agree, killing the `launchSettings` divergence by construction. |
| `dale scenario run` | fails only on a failed *step*; judge is human; nightly wall-clock | **auto-fails on a missed `expect[]`, deterministically → moves onto the PR gate**; `run --all` can replace the hand-rolled `CommittedScenariosShould` theory |
| `dale scenario validate` | default topology, scalars only | **per-topology + struct field paths** |
| `dale scenario schema` | default topology, name paths only | **per-topology, enumerating struct field shapes** (restores drift-safety) |
| `dale scenario scaffold` | codegen the typed escape-hatch test | **stays, but shrinks** — fewer scenarios need it once `expect`/struct-paths land |

---

## 6. Architecture

### 6.1 Keystone — deterministic stepping (the new SDK capability)

A new opt-in DevHost run mode, built by generalizing existing seams:

- **Clock** — register a `FakeTimeProvider`-style controllable clock instead of `TimeProvider.System`. The DI seam already exists: `ActorSystem` resolves `serviceProvider.GetService<TimeProvider>() ?? TimeProvider.System` (`Vion.Dale.ProtoActor/ActorSystem.cs`); `LogicBlockBase` already uses `TimeProvider`; tests already inject `FakeTimeProvider`.
- **Stepper = generalized pause gate** — `IDelayedSendGate` is already the single choke point for *every* `[Timer]` tick / `SendToSelfAfter` / `InvokeSynchronizedAfter` delayed send, and `DevHostRunControl` already implements **hold-and-replay**. The stepper generalizes this: hold all delayed sends → advance the clock by one control interval → release exactly the now-due sends → pump to quiescence → repeat. `advance N cycles` becomes exact.
- **Quiescence barrier** — "all mailboxes empty + no in-flight handler," observed via the RFC 0005 mailbox-depth vitals + the receive middleware (`ActorMiddleware.ReceiveMiddleware`).

**Concrete work / risk (the spike gates this):**
1. **Route every time-dependent wait through `TimeProvider`.** Today some waits use wall-clock `Task.Delay` — e.g. the ack-timeout in `ActorSystem.SendAndWaitForAcknowledgementAsync` (`ctx.ReenterAfter(Task.Delay(timeout), …)`) and `StopActorsAndWaitAsync`. Under deterministic stepping these must use the injected clock, or a stepped run will time out differently than a live one.
2. **Reliable quiescence detection in Proto.Actor** — mailbox drain + `ReenterAfter` continuations must be accounted for; the barrier must be deadlock/livelock-free and deterministic.

**Spike (Phase 0):** make the `EnergyManager` control cycle step deterministically end-to-end (clock + gate generalization + quiescence) and port **one** representative matrix onto it. The rest of the design is gated on the spike succeeding.

The same artifact then runs **deterministically in CI** (`advance`/`settle` → exact) and in the **Player** either stepped (deterministic, for the traceability "why" loop) or free-running wall-clock (live feel).

### 6.2 The unified scenario artifact (data-first)

One file, a superset of today's shape (`Vion.Dale.DevHost/Scenarios/ScenarioFile.cs`):

| Section | Today | Grows to |
|---|---|---|
| `topology` | id ref | unchanged (folder-discovered; `"default"` for the auto-gen) |
| `setup` | `set` / `digitalInput` / `analogInput` | unchanged |
| timeline `steps` | `set`/inputs, `wait{seconds}`, scalar `waitUntil` | **logical-time vocabulary**: `advance {cycles}`, `settle` (advance-until-quiescent), `waitUntil {condition}`. `wait{seconds}` is **demoted to Player-only stimulus pacing** — never an assertion clock. |
| observe | `watch[]`, scalar `waitUntil` | **struct field paths** (`RefControllableConsumer.AllocatedCurrent.L1`), numeric tolerances |
| assert | `judge[]` (human only) | **`expect[]`** (auto: `above`/`below`/`equals`+`tolerance`/`notEquals`, **+ relational** path-vs-path) **and** `judge[]` retained for the un-mechanizable |
| trace | `specs` / per-item `spec` | unchanged — **optional free-form tag** |

- **Struct field paths** are read-only addressing. Extend `ScenarioResolver` to walk struct fields and `ScenarioConditions` to compare them; the lift at `ScenarioFile.cs:423-426` (reject structs/arrays) becomes "reject struct *as a whole*, allow an addressed scalar leaf."
- **`expect[]` vs `judge[]`** — `expect` auto-fails in CI (closes most of today's fallback-to-C#); `judge` stays human (the "PeakShaving buffer wins the argmin, not shadowed by self-consumption" class that is not mechanically checkable).
- **`expect[]` operator vocabulary — modelled on common assertion libraries** (xUnit / Vitest), so it reads familiar: `toBe`/`toEqual` (equals), `toBeCloseTo` (equals + tolerance), `toBeGreaterThan`/`toBeLessThan` (above/below), `not.*` (notEquals), `toBeOneOf` (enum-set membership), plus **relational** path-vs-path (`A` greater/less than `B`). The exact set is finalized in Phase 1; the principle is "borrow the matcher names developers already know," not invent a bespoke DSL.
- **Escape hatch (kept narrow)** — a C# headless test calls the SDK runner on the **same file** for arrange+stimulate, then adds arbitrary typed asserts (symmetric `ApplyAsync`). For the derived-semantic ~10% only.
- **Failure output** — the runner must emit expected-vs-actual + a timeline trace comparable to today's `Claim`/`ScenarioTrace` quality.

### 6.3 Topology as data — DI is the catalog, JSON is the instance graph

**The model:**
- **`WithDi<DependencyInjection>()`** (source-generated, in `Program.cs`) registers the assembly's block types + service deps — the **catalog of what exists**.
- **`topologies/*.topology.json`** declares **which instances** (`typeFullName` + `name`) and **how they wire** (`interfaceMappings`) — the **instance graph**. `DevTopologyLoader` already resolves the catalog types and applies the mappings.
- The committed JSON is the **single source of truth** for named topologies; explicit `interfaceMappings` are first-class and **validated** (interface identifiers resolve, no dangling refs, struct/contract shapes checked).

**`Program.cs` is fixed plumbing the user never edits — with one exception.** The C# topology presets (`AddLogicBlock` chains, `WithTopologyName`, the `BuildPreset` switch) are **deleted**. The DevHost program reduces to:

```csharp
DevHostBuilder.Create()
    .WithDi<DependencyInjection>()   // this library's catalog
    .WithWebUi()
    .Build();                        // + the standard runner
```

The **only** legit reason to edit `Program.cs` is the advanced **multi-library** case: registering a second library's DI module, exactly as `logic-block-libraries` does today —
`.WithDi<DependencyInjection>().WithDi<RefBlocksDi>()` — to bring in the `Ref*` blocks. An auto-discovery mechanism for referenced libraries' DI modules is possible but **YAGNI for now**.

**Auto-creation of the default topology (D4).** When the DevHost boots and finds no usable topology:
1. Generate one by rule — **each DI-registered block instantiated once, AutoConnected**.
2. **Write it to `topologies/default.topology.json`** with the stable id `default`, and **announce it** ("No topology found — generated `topologies/default.topology.json` (each block once, auto-connected). Edit it, commit it, or add it to `.gitignore`.").
3. Authored/committed topologies always win; the auto-gen only fills the gap.
4. **Mutually-exclusive guard:** AutoConnect leaves detected-conflicting commanders (e.g. two blocks claiming the same device manager interface) **unwired** and notes why, so the generated file is coherent rather than a fighting network. For curated libraries (EM) the real topologies are committed, so auto-gen rarely triggers there.
5. The auto-gen fires identically in the **headless/export path** (`dale dev --export-config`), so CI and local agree.

**AutoConnect is a *generator*, not runtime magic.** The committed artifact is always explicit `interfaceMappings`. AutoConnect (reflection over provider/consumer interfaces) produces that array — used by the auto-gen above, by a CLI generator (`dale topology … --auto` → explicit JSON to review+commit), and by the Player.

**The gallery becomes a built-in mode**, not a C# preset: `--gallery` (or a Player toggle) instantiates every catalog type **unwired** — the §8.16 surface review, with no fighting commanders because nothing is wired.

**Topology authoring in the Player (dual-author) — in scope for v1.** The Player gains a **lightweight** topology affordance: **select blocks from the catalog/DI**, then add links/mappings by **picking from a list of *compatible* targets** (the SDK already knows which provider↔consumer interfaces are compatible, so it offers only valid options — `isValidConnection`-lite), plus a one-click "auto-wire"; review the explicit mappings and save to `topologies/*.json`. This is deliberately **not** as clever as a node-graph editor — the production dashboard (`dashboard/.../LogicEditor.vue`, VueFlow) owns heavy visual wiring; the dev tool does not duplicate it. A dedicated **UX round happens at implementation time** (Phase 3).

**Cascading is flat** — multiple named instances of a block type are already supported; parent/child is `interfaceMappings` semantics + EM-internal logic. **No composition.**

**Per-topology schema** — the generated editor schema (`scenarios/.dale/scenario.schema.json`) must be produced **per topology** and must enumerate **struct field shapes**, not just name paths, so struct-literal/field drift becomes a loud validate-gate error (restoring the compile-time safety lost by leaving typed C#).

### 6.4 Discovery, hello-world, and the retired switch

- **Topologies** — discovered from `topologies/`; the Player switches between them. The `Program.cs` `BuildPreset` switch is **gone**. **Boot-topology resolution:** the Player's remembered **last selection** (localStorage) if it still exists, else the **first topology alphabetically**; if **none** exist at all, the auto-gen writes `default.topology.json` (D4). For EM this yields `em-closed-loop` (first alphabetically among `em-closed-loop` / `operator-steering` / `reservation`) — the expected default falls out of the naming, with no manifest needed.
- **Scenarios** — discovered from `scenarios/`; each binds by `topology` id (matches a committed file's id or the `default` auto-gen id).

**Hello-world walkthrough (`dale new` → `dale dev`):**
1. `dale new` scaffolds the fixed-plumbing `Program.cs` (never edited) + `scenarios/<id>.scenario.json` with `"topology": "default"`, and **no topology file**.
2. First `dale dev`: no topology found → DevHost generates + writes `topologies/default.topology.json` (the one scaffolded block, auto-connected) and announces it.
3. The scenario binds to `default`; the Player runs it. **Zero topology files authored, zero `Program.cs` edits.**
4. The developer can hand-edit `default.topology.json`, author more topologies (files or UI), commit them, or `.gitignore` the generated default.

### 6.5 SDK vs consumer boundary

- **SDK owns:** the deterministic stepper; the data scenario/topology model + runner + resolver (incl. struct paths); the auto-gen default-topology rule; the Player (with tag-based **filtering**, a lightweight topology affordance, and a record-to-author affordance); the `dale topology` / `dale scenario` CLI verbs; the **optional free-form tag** field.
- **Consumer owns (e.g. `logic-block-libraries`):** the requirements corpus, the AC↔scenario linking convention, coverage gates (`spec-trace.ps1`), and the "fit-or-change-the-requirement" SDD workflow — all built on the optional tag, via their own tooling. **No SDD assumptions in the SDK.**

---

## 7. Migration & scope

- **Untouched:** the pure single-block **unit tier** (K-rules, exact numerics, contract-shape) — different concern, stays.
- **C# topology presets deleted:** `Ecocoach.EnergyManagement.DevHost/Program.cs` loses the `BuildPreset` switch + the `Scenarios` static class; `em-closed-loop`, `operator-steering`, `reservation` are already committed JSON; the `all-surfaces` gallery becomes the built-in `--gallery` mode; the `manual-*` hardware-proxy presets become committed topology JSONs (or stay as documented `dale dev` recipes). `Program.cs` keeps only `.WithDi<DependencyInjection>().WithDi<RefBlocksDi>()` + plumbing.
- **Matrices (load-bearing):** `ScenarioRegistry` aggregates `Arbitration` / `ModeDerivation` / `Reservation` / `Delivery` definitions, AC-tagged, non-vacuity-validated, run on the **PR gate**, and counted by `spec-trace.ps1`. They are **not** throwaway.
- **Migration strategy — spike one, then decide:** the Phase 0 spike ports **one** representative matrix (the `Reservation` `reserveMode × socRegion` grid — it asserts exact derived `EnergyManager.BufferOperatingModes` through the full EM pipeline) to explicit scenarios on a shared topology, on the deterministic real-wired substrate. Then decide *port-the-rest* vs *keep-the-TestKit-lane-frozen-for-matrices* based on how the explicit-file verbosity actually feels.
- **Verbosity escape valve:** if explicit-per-combo files prove too verbose, reintroduce matrices as a **data-level `axes` field** expanded by the runner — *not* a C# engine.
- **New interaction scenarios** are authored only in the unified system.
- **Accepted losses:** axis-matrix combinatorics (as a C# engine) and single-SUT isolation for interaction scenarios.

---

## 8. Risks & open questions

1. **Quiescence reliability in Proto.Actor** *(highest risk)* — the spike must prove deterministic, deadlock/livelock-free settling.
2. **Every delay through `TimeProvider`** — audit all `Task.Delay` / Proto scheduler usages (ack-timeouts, stop-waits, settle timeouts). A missed wall-clock wait breaks determinism silently.
3. **Auto-gen coherence for mutually-exclusive libraries** — the each-once+AutoConnect rule can wire a fighting network; the conflicting-commander guard (§6.3) must reliably detect such cases and leave them unwired. Transparency (write-to-disk + announce) makes a bad guess self-correcting, but the guard should make the *common* case coherent.
4. **Struct-shape schema maintenance** — the generated per-topology schema must track struct fields, or drift-safety regresses below today's.
5. **Authoring ergonomics** — raw JSON gets verbose for interaction-heavy/cascading scenarios; the Player record-to-author + good defaults must carry it (a thin DSL is a possible later addition).
6. **`settle` over a volatile watch set silently burns the full budget** *(found during the Phase 1b visual verification)* — `settle` advances until the **`watch` set** stabilizes or the `maxSeconds` budget. If any watched signal never settles (a free-running counter, a clock-derived value, a noisy sensor), `settle` runs to the full budget and reports a *soft* "did not converge" detail — easy to miss, yet it has silently advanced virtual time (and can cascade: a relational `expect` comparand read after it sees the run-up value). **Resolved (Phase 2, both options).** `settle` gained an optional `until` field — an explicit subset of target paths that must stabilize (omitted ⇒ the whole `watch` set, so the large observability set need not all settle); and non-convergence within the `maxSeconds` budget now **fails the step**, naming the still-changing target and its last delta (e.g. `did not converge within 3 virtual s — Ticker.Ticks still changing (2 → 3) after 3 hops`) instead of the old soft pass. `maxSeconds` (author-controlled) stays the single bound: fast common-case convergence comes from scoping to `until`, and a non-advancing event storm still fails earlier via the quiescence barrier's safety timeout (a fixed hop cap was considered and dropped — it would override a legitimately-slow settle's explicit budget).

**Resolved during review (folded into the body):**
- **`expect[]` operators** → modelled on xUnit / Vitest matchers (`toBe`/`toEqual`/`toBeCloseTo`/`toBeGreaterThan`/`toBeLessThan`/`not.*`/`toBeOneOf` + relational); exact set finalized in Phase 1 (§6.2).
- **Default-topology selection** → remembered last-UI selection (localStorage) if it exists, else first-alphabetical, else auto-gen; EM lands on `em-closed-loop` (§6.4).
- **Player topology-editing affordance** → in scope for v1 (Phase 3): select from catalog/DI + pick compatible mappings; dedicated UX round at implementation (§6.3).

---

## 9. Phasing / rollout

- **Phase 0 — Spike (gates everything):** deterministic stepping of the EM control cycle end-to-end (clock + gate generalization + quiescence); port one representative matrix; validate verbosity + determinism.
- **Phase 1 — Stepper + data model:** the SDK stepping mode; `advance`/`settle`/`waitUntil` logical-time steps; struct field paths in resolver/conditions/schema; `expect[]`.
- **Phase 2 — Player + CI:** Player runs stepped + free-run; tag filtering; record-to-author; move scenario *execution* onto the deterministic PR gate (retire the nightly wall-clock lane).
- **Phase 3 — Topology becomes pure data:** DI-catalog model; auto-gen default-topology rule (persist to disk, transparent, mutually-exclusive guard); `dale topology --auto` generator; folder discovery; delete the `Program.cs` `BuildPreset` switch + `Scenarios` class; `--gallery` mode; lightweight Player topology affordance.
- **Phase 4 — Migration:** port remaining matrices (or freeze) per the Phase 0 verdict; update `TESTING.md` / `AGENTS.md` to the new landscape.

---

## 10. Graduation to RFCs

This design is expected to land as: an **RFC 0006 v2** revision (the grown artifact + topology-as-data + DI-catalog + auto-gen default + per-topology struct-aware schema) and a **new RFC** for deterministic DevHost stepping (the `TimeProvider` + delayed-send-gate + quiescence model, as a sibling to RFC 0003). Cross-repo coordination (SDK ↔ `logic-block-libraries`) may additionally use the `architecture` repo's `/spec` flow.
