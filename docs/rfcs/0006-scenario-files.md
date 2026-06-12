# RFC 0006: Scenario files — a portable verification vocabulary for DevHost

Status: **Accepted** (revision 5 — all open questions resolved; ready for implementation, R3/R4/R5).
Author: jonas.bertsch. Date: 2026-06-11.

Revision 2: example corrected against the real consumer block surfaces (block names, struct fields, enum
members verified in `logic-block-libraries`); step grammar closed into a schema-expressible `oneOf`; run
identity + concurrency semantics specified; `validate`/`schema` data source fixed (wired-host configuration,
not library introspection); xunit theory semantics specified; `waitUntil` already-true race owned explicitly.

Revision 3 (maintainer decisions): `PUT` write-to-disk approved; CI behavior derived from file content (no
`"ci"` flag); watch-only scenarios are a first-class exploration starting point; **`"value": null` supported
in v1** (verified working through the web path today — [DevHostControl.cs:301](../../Vion.Dale.DevHost/Control/DevHostControl.cs#L301)
decodes JSON null; only the in-process `SetPropertyAsync(…, object value)` *annotation* needs the `object?`
fix); **timing model added** (`wait` step for stimulus pacing, Playwright-aligned posture); security note
right-sized for a local-only tool; **topology files** introduced as a dev-profile subset of the production
`SetLogicConfigurationPayload` (Vion.Contracts), replacing the preset-naming open question.

Revision 4: the last three open questions resolved by the maintainer — topology dev-profile scope confirmed
(mock-provider materialization is an R5 loader-design detail, not a format question); topology switching
sequenced behind the R2 run-control reset, no elevated priority; `ramp` stays a v2 candidate — the
`set`/`wait` pair encoding is the accepted v1 answer.

Revision 5 (pre-implementation amendment): **name paths gain an optional service segment**
(`"Block.Service.Property"`) — the flat `Block.Property` form is ambiguous for multi-service blocks
(nested `[LogicBlockInterfaceBinding]` members), where the per-block name map collapses duplicate
property names last-service-wins ([DevHostIntrospection.cs:209](../../Vion.Dale.DevHost/Control/DevHostIntrospection.cs#L209));
resolution rules below. Rides additive service-qualified `IDevHostControl` overloads (R3). The
topology-name `ConfigurationOutput` field listed as planned in revision 2 has shipped meanwhile
(`WithTopologyName`, R0–R2) — the capability table is updated accordingly.

One sentence: a tiny, versioned, git-committed JSON vocabulary (`*.scenario.json`) that describes a
manual-test scenario — setup, ordered stimuli, watch list, human judgments — executed by **one** C#
`ScenarioRunner` over the existing `IDevHostControl` (RFC 0003), and consumed identically by the DevHost
web UI ("Player"), by xunit in CI, and by agents/CLI.

## Motivation

RFC 0003 closed the *programmatic* testing gap: agents and CI can boot the wired network and drive it
in-process. The remaining gap is **manual surface testing** — a human verifying behavior — and it has a
measured shape (counts from running `Vion.Dale.LogicBlockParser` against the built consumer assemblies,
2026-06-11):

- The default consumer DevHost topology (`EnergyManagerClosedLoop`,
  `Ecocoach.LogicBlockLibraries/Ecocoach.EnergyManagement.DevHost/Program.cs`) renders **7 blocks /
  176 properties** on one page; the all-surfaces gallery renders 294.
- A real scenario touches **1–9 of them (1–5 %)**: `PeakShavingShould` reads/writes 9 distinct properties
  of 176; `SteeringChainNetworkShould` touches 2; the TestKit mode-derivation scenarios assert on exactly
  one of EnergyManager's 84 properties (`…/Ecocoach.EnergyManagement.HeadlessTest/`,
  `…/Ecocoach.EnergyManagement.TestScenarios/`).

So the dominant cost of manual verification is not reading values — it is *staging* the situation and
*finding* the handful of relevant signals among hundreds. Today that staging knowledge lives in two
disconnected places: ephemeral human memory ("click these five knobs, then watch those three numbers") and
headless C# tests that a human cannot see or replay visually. Every manual session re-derives what a test
already encodes, and every test encodes what some manual session once discovered. They are parallel worlds.

The second motivation is the **agent-writes / human-verifies loop**. Coding agents author logic-block changes
and already write headless tests against `IDevHostControl`. What they cannot do today is *stage the human's
verification view*: there is no artifact an agent can commit that makes the reviewing human's DevHost open on
the right blocks, the right knobs, the right expectations. A scenario file is exactly that artifact; the PR
convention is the scenario **id** (resolvable via `dale scenario open <id>` or the running host's printed
links — see "Deep links and ports").

Third, **spec traceability**. The reference consumer runs spec-driven development: acceptance criteria carry
ids (`AC-…`, `SYS-…`), and a CI gate (`scripts/spec-trace.ps1` in the consumer repo) fails when a declared id
is referenced by no test. That gate already counts AC ids embedded in *declarative C# scenario records*
(`<Lib>.TestScenarios`, scenario-test-infra design §4.1). Scenario files extend the same idea across the
manual surface: a spec id on a scenario (or on an individual judgment item) makes the chain
**spec → scenario file → CI run → Player checklist → verification report in the PR** fully traceable.
Because the gate matches ids by regex over raw file content, the consumer-side change is small (add the
`*.scenario.json` extension and the scenarios directory to its scan set — 2–3 lines, see "Spec
traceability").

## What already exists (and what doesn't)

| Capability | State today | Cite |
|---|---|---|
| Acked property writes (task completes on re-publish; read-after-write reliable) | ✅ | [IDevHostControl.cs:39-56](../../Vion.Dale.DevHost/Control/IDevHostControl.cs#L39) |
| Condition-based waiting with timeout (the no-virtual-time substitute, RFC 0003) | ✅ `WaitForAsync` — but it observes only events *after* the call and returns `null` on timeout | [IDevHostControl.cs:76-83](../../Vion.Dale.DevHost/Control/IDevHostControl.cs#L76) |
| Digital / analog input injection | ✅ | [IDevHostControl.cs:58-62](../../Vion.Dale.DevHost/Control/IDevHostControl.cs#L58) |
| Set a nullable property to null | ✅ through the web path (`SetValueInput<object>` + `JsonValueKind.Null => null`, [DevHostControl.cs:301](../../Vion.Dale.DevHost/Control/DevHostControl.cs#L301)); in-process the `object value` parameter needs the `object?` annotation fix | [DevHostController.cs:54](../../Vion.Dale.DevHost.Web/Api/Controllers/DevHostController.cs#L54) |
| Topology + full introspection (blocks, services, schemas, wiring) | ✅ `ListLogicBlocks` / `GetConfiguration` | [IDevHostControl.cs:18-26](../../Vion.Dale.DevHost/Control/IDevHostControl.cs#L18) |
| HTTP projection of the control surface (localhost `/api` + SignalR) | ✅ shared by UI and headless tools | RFC 0003, [DevHostController.cs](../../Vion.Dale.DevHost.Web/Api/Controllers/DevHostController.cs) |
| A running topology name exposed to clients | ✅ `ConfigurationOutput.TopologyName` (`WithTopologyName`, shipped R0–R2) | [DevConfigurationBuilder.cs](../../Vion.Dale.DevHost/DevConfigurationBuilder.cs) |
| Service-qualified property reads/writes by identifier (multi-service disambiguation) | ❌ — writes can address by service GUID (`SetServicePropertyValueAsync`), reads collapse to the per-block name map; additive identifier-qualified overloads ship with R3 | [DevHostControl.cs](../../Vion.Dale.DevHost/Control/DevHostControl.cs) |
| A JSON form of a topology | ✅ in production — `SetLogicConfigurationPayload` (Vion.Contracts, Cloud→Mesh) carries logic-block instances, interface mappings, contract mappings; ❌ in DevHost (presets are C# only) | `vion-contracts/Vion.Contracts/Events/CloudToMesh/SetLogicConfigurationPayload.cs` |
| A scenario artifact both humans and machines consume | ❌ does not exist — UI sessions are ephemeral, tests are invisible to the UI | — |
| Scenario discovery / serving / execution endpoints | ❌ | — |
| Spec-id traceability for manual verification | ❌ (consumer gate scans `*.cs` under `test|conformance` paths only) | consumer `scripts/spec-trace.ps1:95-96` |

The crucial observation: **every construct the format needs already has a shipped, documented capability** on
`IDevHostControl`. The format is a serialization of that interface, not a new runtime.

| scenario.json construct | `IDevHostControl` member | Player rendering |
|---|---|---|
| `setup[]` / `steps[].set` | `SetPropertyAsync` (acked) | checkmark / ordered step + ack ms |
| `steps[].digitalInput` / `steps[].analogInput` | `SetDigitalInputAsync` / `SetAnalogInputAsync` | ordered step + ack |
| `steps[].waitUntil` | `GetProperty` + `WaitForAsync` (protocol below) | spinner + elapsed time |
| `steps[].wait` | `Task.Delay` (stimulus pacing — see "Timing model") | timed step |
| `watch[]` | `Subscribe` + `GetProperty` (validated up front) | live value tiles |
| `judge[]` | — (human only, v1) | checkboxes + report |
| `topology` | topology name in `ConfigurationOutput` (additive) | blocking mismatch interstitial |

## Design principles

1. **JSON is the canonical authoring path.** Humans promote Explorer sessions into files; agents write files
   directly; the C# side *consumes* files (generic xunit theory) rather than generating them. There is no C#
   fluent builder — it would tie the vocabulary to SDK binary versions and undercut portability.
2. **Data, not DSL.** No loops, no expressions, no computed values, no conditionals. Anything that needs
   logic is a C# test (which may *compose* with a scenario — see "Composition rule"). The expressiveness
   ceiling is a feature: pressure to grow the format is redirected into either a reviewed vocabulary version
   or honest C#.
3. **No new runtime capabilities.** The runner is a thin sequential interpreter over existing
   `IDevHostControl` members. It does own one *protocol* (the `waitUntil` evaluation order, below), but if a
   scenario can express something the headless API cannot do, the format is wrong.
4. **Mechanism, not policy.** The SDK defines the format, discovery, runner, and endpoints. The spec-id
   grammar (`AC-…`/`SYS-…`), the trace gate, and where files live in a consumer's workflow are consumer
   policy. `specs` fields are free-form strings to the SDK.
5. **Portable by construction.** The vocabulary maps 1:1 onto the HTTP `/api` projection, so a non-.NET
   runner (e.g. Python) is a small HTTP client away; the introspection JSON consumed alongside is already
   language-neutral. Nothing in the format references C# types.

## The format (v1)

File name: `<id>.scenario.json`, discovered under `{cwd}/scenarios/` (overridable, `WithScenarios(path)`;
when `{cwd}/scenarios` does not exist — IDE launches set the working directory to the build output — the
nearest ancestor's `scenarios/` up to the repository root (`.git`) is used).
Scratch scenarios that shouldn't ship can simply be `.gitignore`d (e.g. `scenarios/_local/`); committed ones
are the artifact.

**Name paths** (revision 5 grammar). Properties are addressed by a dot-separated path with **two or three
segments**:

- `"BlockName.PropertyIdentifier"` — the common form. Valid only when the property identifier is
  **unambiguous within the block** (exactly one of the block's services carries it).
- `"BlockName.ServiceIdentifier.PropertyIdentifier"` — the qualified form for multi-service blocks. The
  service identifier of a nested `[LogicBlockInterfaceBinding]` member equals the **binding member name**
  (e.g. `"ChargingStationMultiPoint.ChargingPoint1.RequestedCurrentA"`).

Resolution is part of up-front validation, against `GetConfiguration()`: a two-segment path whose property
identifier exists on **more than one** of the block's services is a **validation error** that lists the
qualified candidate paths (never silent last-wins — the flat per-block name map collapses duplicates
last-service-wins, [DevHostIntrospection.cs:209](../../Vion.Dale.DevHost/Control/DevHostIntrospection.cs#L209),
which is exactly the trap the format must not inherit). A three-segment path is always legal, also for
single-service blocks. Segments are split on `.`; block names, service identifiers, and property identifiers
containing a literal `.` are not addressable by scenario files (validated at authoring time by
`dale scenario validate`; C# identifiers cannot contain dots, so only an exotic `AddLogicBlock(name:)` choice
hits this — don't put dots in block names).

Block *names* are those assigned in `AddLogicBlock(name:)`. Never per-run GUIDs. When a preset assigns no
`name:`, the name defaults to the type name
([DevConfigurationBuilder.cs:35](../../Vion.Dale.DevHost/DevConfigurationBuilder.cs#L35) — `name ??
typeof(T).Name`); the reference preset does exactly that, so the example below uses type names. Consumers are
encouraged to assign short stable names — scenario files bind to whatever the topology declares. The runner
executes through the additive service-qualified `IDevHostControl` members (R3), so the qualified form reaches
services the flat map shadows.

```json
{
  "$schema": "./.dale/scenario.schema.json",
  "version": 1,
  "id": "peak-shaving",
  "title": "Peak shaving under import limit",
  "description": "EM must shift load to the buffer when grid import approaches the configured limit.",
  "topology": "em-closed-loop",
  "specs": ["AC-EM-23"],
  "setup": [
    { "set": "RefControllableBuffer.Bands",
      "value": { "offGridCapacity": 10.0, "loadManagementCapacity": 30.0,
                 "peakShavingCapacity": 40.0, "gridFeedOptimizedCapacity": 20.0 } },
    { "set": "RefControllableConsumer.OperatingMode", "value": "PeakShaving" }
  ],
  "steps": [
    { "label": "Limit grid import", "spec": "AC-EM-23.1",
      "set": "EnergyManager.GridConfig",
      "value": { "maximumImportCurrentA": 25.0, "loadManagementReservePercent": 10.0,
                 "peakFilterEnabled": true, "peakFactor0s": 1.0, "peakFactor4s": 1.0,
                 "peakFactor10s": 1.0, "maxExportPowerKw": null, "peakShavingLimitKw": 24.0 } },
    { "label": "Raise consumer demand", "set": "RefControllableConsumer.RequestedCurrentA", "value": 16.0 },
    { "label": "Buffer takes over",
      "waitUntil": { "property": "RefControllableBuffer.AllocatedActivePowerKw", "above": 5.0 },
      "timeoutSeconds": 20 },
    { "label": "Clouds roll in", "set": "RefLimitableSupplier.ProductionProfile", "value": 0.2 }
  ],
  "watch": [
    "RefGridMeter.ComputedGridActivePowerKw",
    "RefControllableBuffer.AllocatedActivePowerKw",
    "RefControllableConsumer.AllocatedCurrent",
    "RefControllableBuffer.StateOfChargePercent"
  ],
  "judge": [
    { "text": "Grid import stays at or below the configured limit after step 2", "spec": "AC-EM-23.1" },
    { "text": "Buffer ramps within ~5 s without oscillation", "spec": "AC-EM-23.2" },
    { "text": "Consumer current recovers after step 4" }
  ]
}
```

(Struct values are complete literals: a struct property is **replaced as a whole** — `SetPropertyAsync`
takes one complete value; cf. the consumer convention that writable config structs are flat aggregates,
`_implementation-conventions.md` §8.6 rule 6. Nested nullable fields like `maxExportPowerKw` may be `null`.)

Vocabulary, exhaustively (v1):

- `version` (required, integer) — vocabulary version. Runners reject unknown versions loudly; evolution is
  by version bump, not by silent extra fields. The JSON Schema sets `additionalProperties: false` throughout.
- `id` (required) — URL-safe slug; must match the file name; the deep-link route is `#/scenario/{id}`.
- `title`, `description` (optional) — human text.
- `topology` (required) — the topology id the scenario expects (a C#-preset's declared name or a topology
  file id — see "Topology files"), compared against the (additive) topology name in `ConfigurationOutput`.
  Mismatch is a **blocking** interstitial in the Player (switch / proceed-anyway) and a **skip** in CI.
- `specs` (optional, string[]) — free-form trace ids for the scenario as a whole.
- `setup[]` (optional) — staging entries, applied **in file order** before steps; authors must keep them
  order-independent and idempotent (an authoring contract, not runner-verified). Entries are `set` /
  `digitalInput` / `analogInput` shapes (no waits). Any failure aborts the run.
- `steps[]` (optional) — **ordered**; order is semantically significant (e.g. urgency before limit). A step
  is exactly **one of four closed shapes** (schema `oneOf`), each with optional `label` (shown in Player and
  reports) and optional `spec`:
  - `{ "set": "Block.Property", "value": … }` — scalar, enum name (case-sensitive string), complete
    struct/array literal, **or `null`** (nullable properties; the web path already decodes JSON null —
    the in-process `object?` annotation fix ships with R3); acked via `SetPropertyAsync`.
  - `{ "digitalInput": { "block": "…", "contract": "…" }, "value": true }` (likewise `analogInput` with a
    number) — resolved by name against `GetConfiguration()` wiring at run time; executed via
    `SetDigitalInputAsync` / `SetAnalogInputAsync`.
  - `{ "waitUntil": { "property": "Block.Property", "above" | "below" | "equals" | "notEquals": … },
    "timeoutSeconds": 20 }` — condition-gated **outcome waiting**. `timeoutSeconds` defaults to 20.
  - `{ "wait": { "seconds": 1.0 } }` — fixed pause for **stimulus pacing** (see "Timing model").
- `watch[]` (optional) — name paths the Player pins as live tiles; also the suggested pin set when the
  scenario is opened in the Explorer. **Validated up front on every run** (Player and CI) against
  `GetConfiguration()`, so even a watch-only scenario smokes renames. A watch-only scenario (no steps) is
  legal and is the recommended *starting point for exploring*: it stages the relevant signals without
  driving anything.
- `judge[]` (optional) — human-judgment checklist items: `{ "text": …, "spec": … }`. **v1 has no
  auto-asserting checks** — one false-red on correct-but-slow behavior would poison trust in the whole
  surface. A `checks[]` section (direction/tolerance-with-timeout, evaluated server-side by the same runner,
  opt-in for CI) is a planned v2, gated on Player usage experience.

**CI semantics are derived from content, not flags.** There is no `"ci": false`. What CI does with a file is
inherent in what the file contains: `setup`/`steps` are executed and must ack; `waitUntil` must complete in
time; `watch[]` must resolve; `judge[]` items are *reported* as `requires human` (never failed); future
`checks[]` assert. A judge-only file is thereby automatically a smoke test, a watch-only file a rename
detector — no flag to keep in sync.

**Timing model** (Playwright-aligned). Three distinct timing constructs, three distinct purposes:

| Construct | Purpose | Playwright analogue |
|---|---|---|
| acked `set` (implicit) | every write completes only when applied + re-published — the next step never races its predecessor | auto-waiting actionability |
| `waitUntil` | wait for an **outcome**, with timeout | `expect(...).toPass()` / `waitFor` |
| `wait` | **shape a stimulus over time** — ramps, dwell times, pacing | `waitForTimeout` (discouraged for assertions, legitimate for pacing) |

Steps otherwise run back-to-back: ack → next. A ramp ("1 kW per second for 5 s") is expressed today as
alternating `set`/`wait` pairs — verbose but fully declarative, and trivial for an agent to generate:

```json
{ "set": "RefLimitableSupplier.ActivePowerKw", "value": 1.0 }, { "wait": { "seconds": 1 } },
{ "set": "RefLimitableSupplier.ActivePowerKw", "value": 2.0 }, { "wait": { "seconds": 1 } }
```

A declarative `ramp` step (`{ "ramp": { "property": …, "to": …, "durationSeconds": …, "intervalSeconds": … } }`
— a signal-generator segment in the hardware-in-the-loop tradition) is the designated v2 candidate if
profiles recur; it stays data, not logic. The discipline that matters is documented, not enforced: **use
`wait` to shape inputs, `waitUntil` to await outputs** — a `wait` immediately before a judgment about
system response is the antipattern `waitUntil` exists to replace.

**Comparison semantics** (`waitUntil`), stated so the first implementer doesn't guess:

| Value kind | `above` / `below` | `equals` / `notEquals` |
|---|---|---|
| number | numeric | exact — **discouraged for doubles**; use `above`/`below`, or `equals` with the optional `"tolerance": ε` field |
| bool / string | invalid | exact |
| enum | invalid | case-sensitive member-name string |
| null | invalid | legal (`"equals": null` awaits the property becoming null) |
| struct / array | invalid in v1 | invalid in v1 (no field-path syntax; a scenario needing it is a C# test) |

Timing assertions remain deliberately inexpressible (RFC 0003's determinism trade-off: wall-clock runs make
them flake).

What is *deliberately absent*: loops, conditionals, expressions, computed values, references between
scenarios, parameter matrices, timing assertions, message-tap assertions. The consumer's existing
`TestScenarios` axis-matrix C# records remain the right tool for combinatorial derivation tests at the
TestKit tier; scenario files are the cross-surface skeleton, not a replacement for that layer.

## Topology files

Production already has a JSON form of a topology: `SetLogicConfigurationPayload` (Vion.Contracts,
Cloud→Mesh) — logic-block instances (`TypeFullName`, `Name`, service-id mappings), `InterfaceMappings`, and
`ContractMappings`, plus deployment concerns (per-mapping MQTT `InstallationTopic`s, package
pinning/`LogicBlockLibrarySources`) that have no meaning at dev time. DevHost topologies should converge on
the same structure rather than invent a second one:

- `topologies/<id>.topology.json` — the **dev profile** of the payload: `logicBlockInstances[]`
  (`typeFullName`, `name`; types resolved against the loaded plugin assemblies), `interfaceMappings[]`
  (sans topics), `contractMappings[]` **optional** — contracts left unmapped get DevHost mocks, exactly
  today's behavior. No package pinning (dev loads the local build output), no topics, no deprecated
  `IoMappings`/`HardwareBlocks`.
- Scenario files reference topologies by id (`"topology": "em-closed-loop"`) — DRY: many scenarios, one
  topology file.
- C# presets remain fully supported (mechanism, not policy); they declare an id
  (`WithTopologyName("em-closed-loop")` or equivalent), and `dale dev --export-topology` dumps a C#-built
  preset as a topology file — the migration path, and the way the schema/validate tooling gets its block
  names without booting.
- The strategic convergence: the dashboard's Logic Configuration editor *builds* this payload in production.
  A shared structure makes "export an installation's logic configuration, run it locally in DevHost with
  mocks" a tooling exercise rather than a format negotiation — the local-reproduction story for field
  debugging.

Scope discipline: **R3 needs only the string guard** (declared topology name in `ConfigurationOutput`
compared against the scenario's `topology`). The JSON topology loader, `--export-topology`, and
Player-driven topology switching are their own milestone (below) — the format is specified now so the
`topology` field's semantics don't change later.

Initial property values stay **out** of topology files: structural wiring is topology; staged state is the
scenario's `setup[]`. (One layer for "what exists and how it's wired", one for "what situation we put it
in".)

## Execution model

`ScenarioRunner` lives in `Vion.Dale.DevHost` (not `.Web`) and operates **exclusively** through
`IDevHostControl` members. Sequence per run: validate (`topology` match, every name path in
`setup`/`steps`/`watch` resolves) → setup in file order (acked) → steps in order → done. There is no
JavaScript evaluator anywhere — the web UI **triggers and renders** runs, it never executes them. What the
Player shows, xunit ran, byte for byte.

**`waitUntil` protocol** (the one place the runner owns more than a pass-through, because
`WaitForAsync` observes only events occurring *after* the call —
[IDevHostControl.cs:81](../../Vion.Dale.DevHost/Control/IDevHostControl.cs#L81)): evaluate the condition
against `GetProperty` first (already true → step completes immediately); otherwise subscribe via
`WaitForAsync`, then re-evaluate the current value once more (closing the set-between-check-and-subscribe
race); `WaitForAsync` returning `null` (timeout) converts to a **step failure**.

**Run identity & concurrency.** `POST /api/scenarios/{id}/apply` returns `{ "runId": … }`;
`GET /api/scenarios/{id}/run` returns the latest run's status *including its `runId`* (404 if never run), so
pollers can detect restarts (no ABA). **One active run per host** — a second `apply` (any scenario id) while
a run is active returns `409 Conflict`; `?restart=true` cancels the in-flight run (including its pending
`WaitForAsync`) and starts the new one. Two scenarios interleaving sets on one shared network is semantically
incoherent; the API refuses it rather than documenting the hazard. The in-process xunit path executes the
same `ScenarioRunner` but does not register in the HTTP run registry — the "identical state machine" claim
applies to HTTP-triggered runs.

**Failure taxonomy** (distinct renderings, distinct report entries):

- **Step failure** — unresolvable name path (e.g. property renamed), rejected write, or `waitUntil` timeout.
  Loud, red, points at the file, **step index, and label**. The run stops.
- **Judgment failure** — a human ticked "not ok". Amber, recorded as a human verdict.
- **Topology mismatch** — blocked before anything runs (Player interstitial; CI skip).

**New surface** (additive to RFC 0003's `/api`):

- `GET /api/scenarios` — discovered list (id, title, topology, specs, file path).
- `GET /api/scenarios/{id}` — the parsed file. `GET /api/scenarios/{id}/run` — latest run status + runId.
- `POST /api/scenarios/{id}/apply[?restart=true]` — start a run.
- `PUT /api/scenarios/{id}` — save-as-scenario from the Explorer (approved): path-confined to the scenarios
  directory (traversal-rejected), disabled by `DALE_DEVHOST_READONLY_SCENARIOS=1`.

**Security note** (right-sized: this is a localhost-only dev tool). The residual risk class is a hostile web
page in the developer's own browser firing requests at `http://localhost:{port}` — CORS does not prevent
cross-origin *sends*, and the current policy is allow-all
([WebHostService.cs:82](../../Vion.Dale.DevHost.Web/Services/WebHostService.cs#L82)). The proportionate fix
is a one-time, ~10-line `Origin`/`Host` header check applied to the mutating `/api` routes when the scenario
endpoints land (browsers' Private Network Access rules are tightening the same hole from their side). `PUT`
additionally keeps the path confinement + env-var disable above since it writes to the working tree. No
further ceremony.

## Discovery, deep links, and ports

Discovery: `{cwd}/scenarios/*.scenario.json` with a `FileSystemWatcher` — edit the file in your IDE and the
Player reloads it; agents stage the human's view simply by committing a file.

Absolute URLs appear **only** in runtime output, where the port is known: the `dale dev` readiness line
(RFC 0003, `DALE_DEVHOST_NO_BROWSER`,
[DevHostWebRunner.cs:37](../../Vion.Dale.DevHost.Web/DevHostWebRunner.cs#L37)) additionally lists discovered
scenario links. A PR must not hardcode `http://localhost:5000/…` — the port is configurable and
per-machine. The PR convention is the scenario **id**; `dale scenario open <id>` resolves the running host
(or boots one via the consumer's DevHost project, CLI-consistent shell-out) and opens `#/scenario/{id}` on
the actual port.

## Consumption surfaces

**Player (web UI).** Loads a scenario, renders *only* its working set: ordered steps with acks and elapsed
times, the watch tiles, the judgment checklist with spec ids displayed inline, and "Copy verification report"
— a markdown block carrying scenario id, file git hash, step acks + timings, and judgment ticks (with their
spec ids), pasteable into the PR.

**xunit (CI).** One generic theory ships with the DevHost package (net10.0, intended for test projects):

```csharp
[Theory]
[ScenarioFiles]   // default: <project>/scenarios, copied to output as content; path overridable
public Task Run(ScenarioFile scenario) => ScenarioRunner.RunAsync(scenario, _host.Control);
```

Semantics, fully specified: the attribute discovers `*.scenario.json` files copied to the test output
directory (the `dale new` template adds the `<Content CopyToOutputDirectory>` glob; an explicit path
argument overrides). A scenario whose `topology` differs from the fixture host's declared topology is
**skipped with a reason**, not failed — consumers group scenarios per topology fixture. Scenario tests must
run in a single **serial collection per host** (xunit parallelizes across collections; interleaving scenarios
against one network is the same hazard the HTTP 409 refuses). CI behavior per file is content-derived (see
format section): steps smoke, watch paths resolve, judgments report `requires human`. The theory is
**opt-in** (explicit attribute, no magic discovery): scenario runs reintroduce the wall-clock waits the
virtual-time TestKit tier (RFC 0001) deliberately avoids, and consumers choose where that trade-off runs.

**CLI / agents.** All `dale scenario` verbs follow the CLI's rules (`-o json`; no SDK dependency — they
operate on files, processes, and the localhost `/api`):

- `dale scenario run <id> [-o json]` — execute against the running `dale dev` host via the HTTP projection;
  emits the same report schema the Player's copy button produces, so an agent verifies it sees exactly what
  the human saw.
- `dale scenario validate` — resolves every name path, `topology`, and the schema itself. Data source: the
  wired host's `ConfigurationOutput` (block *instance* names and topologies exist only there, not in library
  introspection) — fetched from a running `dale dev`, or produced by a one-shot
  `dale dev --export-config <file>` (boot, dump, exit; shell-out-consistent). CI-friendly; catches renames
  fast.
- `dale scenario schema [-o scenarios/.dale/scenario.schema.json]` — emits the **per-project JSON Schema**:
  the SDK ships a generic structural schema; this generated variant enriches it with enums of the actual
  block names and property identifiers from the same `ConfigurationOutput` export (one schema per topology;
  default = the default topology). Convention: committed at `scenarios/.dale/scenario.schema.json` (the
  example's `$schema` ref), drift detection folded into `dale scenario validate`. This is the type-safety
  substitute for the rejected C# builder: completion and red squiggles in any editor, for humans and agents.
- `dale scenario scaffold <id>` — generates a typed C# test from a file — always total, since the vocabulary
  is a strict subset of what C# can do — the graduation path when a scenario outgrows the format.
- `dale scenario open <id>` — see "Deep links and ports".

## Spec traceability (consumer integration)

The SDK treats `specs`/`spec` fields as opaque strings. In the reference consumer, the convention is the
existing id grammar (`spec-trace.ps1:25`), and the gate change is small but not one line: the scan currently
filters `*.cs` under paths matching `test|conformance` (`spec-trace.ps1:95-96`) with fixed scan roots — it
gains the `*.scenario.json` extension and the scenarios directory as a root (2–3 lines; ids inside JSON
strings match the existing raw-content regex as-is). The resulting chain, end to end:

1. `spec/<block>/requirements.md` declares `AC-EM-23.2`.
2. `scenarios/peak-shaving.scenario.json` carries it on a judgment item.
3. `spec-trace.ps1` counts the scenario as coverage — the id is no longer an orphan.
4. CI runs the file as a smoke test on every push (once the consumer adds the opt-in theory — R4).
5. The Player shows `AC-EM-23.2` beside the checkbox the human ticks.
6. The verification report pasted into the PR cites the id with a verdict and a file hash.

A criterion that can only be judged by a human (UX feel, oscillation, "looks right") is thereby *first-class
traceable* without pretending to be automatable — which is precisely the class of criteria the headless tiers
cannot honestly cover.

## Composition rule (C# ↔ scenario)

One entry point, two layers: `ScenarioRunner.ApplyAsync(scenario, control)` = validate + setup + steps
(acked), nothing more; `ScenarioRunner.RunAsync(scenario, control)` = `ApplyAsync` + structured report
(step acks/timings, judgment items as `requires human`). Both take a `ScenarioFile` or an id + scenarios
directory (`(string id, IDevHostControl, string? scenariosDir = null)`).

A C# test uses a scenario as its arrange/stimulate phase and adds arbitrary assertions on top:

```csharp
await ScenarioRunner.ApplyAsync("peak-shaving", host.Control, scenariosDir);
var modes = host.Control.GetProperty("EnergyManager", "BufferOperatingModes");
// …hand-written asserts the vocabulary cannot express…
```

Degradation rules are explicit and one-way honest: scenario → C# is always total (subset); C# → scenario is
partial (only the skeleton serializes; computed values become literals). An optional later `Recorder` (an
`IDevHostControl` decorator capturing a test run's interaction trace as a scenario file) can backfill Player
visualizations for existing tests; it is a convenience, not a pillar.

## Backwards compatibility

- All endpoints, the topology-name `ConfigurationOutput` field, and the `object?` annotation fix are
  additive; existing consumers see no behavioral change until they create a `scenarios/` directory.
- Already-generated projects (from older `dale new` templates) lack the `scenarios/` folder, the content-copy
  glob, the AGENTS.md snippet, and the theory class — they adopt by regenerating or hand-adding (same
  compat posture as RFC 0003's `DevHostWebRunner` note). Nothing breaks meanwhile; `dale scenario` verbs
  degrade with clear errors when no scenarios directory exists.
- `ScenarioRunner` + `[ScenarioFiles]` ship in `Vion.Dale.DevHost` (net10.0) — fine for test projects and
  DevHost hosts, which are already net10.0; the netstandard2.1 SDK runtime is untouched.

## Non-goals

- **Not a TestKit replacement.** Virtual-time, single-SUT testing (RFC 0001/0002) remains the precision tier;
  scenario runs are wall-clock and multi-block by design.
- **Not a DSL.** The vocabulary rejects features that smell like programming; v2 candidates (`ramp`,
  `checks[]`, `variables`) go through an RFC revision, not field creep.
- **No UI-authored topology.** Topologies are files (C# presets or topology JSON); the UI switches them.
- **No auto-asserting checks in v1.** See `judge[]` rationale.
- **No timing assertions, ever, in the format.** Tolerance-with-timeout in v2 `checks[]` is the ceiling.

## Resolved questions (maintainer decisions, 2026-06-11)

1. **PUT write-to-disk** — approved; path-confined + env-var disable; scratch files gitignore-able.
2. **CI exclusion flag** — rejected in favor of content-derived semantics (no `"ci"` field).
3. **Parameterization** — deferred; `variables` is a v2 candidate gated on observed demand.
4. **Watch-only scenarios** — legal, validated, and the recommended exploration starting point.
5. **Preset naming** — superseded by topology files: topology ids name both C# presets (declared) and
   topology JSON files; scenario `topology` references the id.
6. **Set-to-null** — supported in v1; the runtime already handles it, only the in-process annotation changes.
7. **Topology file scope** — confirmed: instances + interface mappings, contract mappings
   optional-with-mock-default; how mock service providers are materialized is settled in the R5 loader
   design, not in the format.
8. **Topology switching** — confirmed to ride the R2 run-control reset; no elevated priority
   (restart-by-hand until then).
9. **`ramp`** — stays a v2 candidate; the `set`/`wait` pair encoding is the accepted v1 answer.

## Open questions

None — resolved 1–6 in revision 3, 7–9 in revision 4.

## Milestones (maps to the agreed value-first phasing)

- **R3** — format v1 (incl. `wait`, `null`, content-derived CI semantics) + shipped generic JSON Schema +
  discovery/watcher + `GET`/`apply`/`run`/`PUT` endpoints + Origin/Host check on mutating routes + Player v1
  (steps, watch tiles, judge checklist, report, mismatch interstitial, deep links) + readiness-line links +
  topology name in `ConfigurationOutput` + `object?` annotation fix.
- **R4** — generic xunit theory; `dale scenario run / validate / schema / scaffold / open` +
  `dale dev --export-config`; `dale new` template gains `scenarios/` + content-copy glob + an example file +
  an AGENTS.md snippet teaching agents to emit scenarios and reference ids in PR summaries; consumer-side:
  the small spec-trace extension.
- **R5 (topology files)** — `*.topology.json` dev profile + loader (types resolved from plugin assemblies,
  unmapped contracts mocked) + `dale dev --export-topology` + Player topology switching (rides the R2
  run-control reset) + per-topology schema generation.
- **Later** — `checks[]` (v2, server-side, opt-in), `ramp`, `Recorder`, server-side value history for a run
  timeline, cloud-config import (production `SetLogicConfigurationPayload` → local topology file).
