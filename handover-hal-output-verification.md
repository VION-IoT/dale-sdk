# Continue: HAL-output verification in DevHost scenarios

Implement a new DevHost scenario capability: **asserting HAL outputs** (`IDigitalOutput` /
`IAnalogOutput`). Today scenarios can DRIVE inputs (`digitalInput`/`analogInput` steps) but cannot
ASSERT outputs — the mock output handlers record the last `Set` value + raise an `OutputChanged`
event to the SPA, but there's no control getter, no HTTP read endpoint, and no scenario assertion.
This is the missing half of HAL testing. Design + investigation are already done (below) — go
straight to TDD implementation. Work on branch **`feat/hal-output-verification`** (already created
off merged `main` @ `e13844e`; clean tree except the untracked `handover-*.md` scratch files at repo
root — leave those). This is a focused feature PR.

## Design (settled — don't re-litigate)

**Dedicated `digitalOutput` / `analogOutput` scenario step shapes**, each a contract-ref + comparator
object, reusing the existing comparator engine. Syntax:

```json
{ "digitalOutput": { "block": "Light", "contract": "DigitalOutput", "equals": true } }
{ "analogOutput":  { "block": "Io", "contract": "EchoOutput", "equals": 3.3, "tolerance": 0.001 } }
```

Rationale (vs. extending `expect`): fits the established one-slot-per-shape `ScenarioStep` pattern,
keeps `expect`'s property-only `{path}` relational-comparand + struct-field-path machinery untouched,
and makes the HAL family symmetric — `digitalInput`/`analogInput` (drive), `digitalOutput`/
`analogOutput` (assert). Comparators are the standard `above`/`below`/`equals`(+`tolerance`)/
`notEquals`/`oneOf`; for a bool digital output only equals/notEquals/oneOf are sensible (not a hard
error). NO `{path}` relational comparand for outputs (literals only).

**Read path:** `DevHostControl` already subscribes to `DigitalOutputChanged`/`AnalogOutputChanged`
(ctor lines 111/113; handlers `OnDigitalOutput` line 562, `OnAnalogOutput` line 572) but only
republishes — it does NOT cache the value. Add caching there (mirroring `OnServiceProperty`'s
`_values` cache, line 536) keyed by `(serviceProviderId, serviceId, contractId)`, then expose **sync**
getters (mirroring the sync `GetProperty`, line 208) returning nullable (null = never Set). Do NOT do
an actor round-trip; cache from the event you already receive.

## Implementation (TDD — write the failing test first, watch it fail, implement, green)

Files (with current line anchors):

1. **`Vion.Dale.DevHost/Control/IDevHostControl.cs`** (~lines 109-113, next to `SetDigitalInputAsync`/
   `SetAnalogInputAsync`): add
   `bool? GetDigitalOutput(string serviceProviderId, string serviceId, string contractId);` and
   `double? GetAnalogOutput(...)`. XML docs ("returns null if the output has never been Set").
   ⚠ `IDevHostControl` is `[PublicApi]` → the `docs/snapshots/publicapi-manifest.json` snapshot WILL
   change; regenerate it (see Verification) — don't fight the snapshot bot.

2. **`Vion.Dale.DevHost/Control/DevHostControl.cs`**: add two
   `ConcurrentDictionary<(string,string,string), bool/double>` caches; populate them in
   `OnDigitalOutput`/`OnAnalogOutput` (before `Publish`); implement the two getters as cache reads
   (return null if key absent). The `DigitalOutputChangedEventArgs` carries
   `ServiceProviderIdentifier`/`ServiceIdentifier`/`ContractIdentifier`/`Value` (confirm in
   `Vion.Dale.DevHost/.../DevHostEvents.cs`).

3. **`Vion.Dale.DevHost.Web/Api/Controllers/DevHostController.cs`** (~lines 38-56, mirror the POST
   `hal/di` / `hal/ai`): add `[HttpGet("hal/do/{serviceProviderIdentifier}/{serviceIdentifier}/{contractIdentifier}")]`
   and `hal/ao/...` returning `{ value }` (bool?/double?). (Web-only; for HTTP symmetry + the SPA — not
   strictly required by the scenario runner, which calls the control directly. Still add it.)

4. **`Vion.Dale.DevHost/Scenarios/ScenarioFile.cs`**: add a `ScenarioOutputAssert` class
   { `Block`, `Contract`, `Above`, `Below`, `[JsonPropertyName("equals")] EqualTo`, `NotEquals`,
   `OneOf`, `Tolerance` } with `StructuralErrors(shape)` → block+contract required +
   `ScenarioComparators.StructuralErrors(shape, …, allowPathComparand: false)`. On `ScenarioStep` add
   `ScenarioOutputAssert? DigitalOutput` + `AnalogOutput`; update `Kind` (return
   "digitalOutput"/"analogOutput"), the `shapes` counter (lines 271-311), the setup-only rejection
   (line 319 — output asserts belong in steps, not setup), the "value is not valid on a {Kind} step"
   guard (line 338 — output steps carry no top-level `value`), and add per-shape validation calls.

5. **`Vion.Dale.DevHost/Scenarios/ScenarioResolver.cs`**: add `case "digitalOutput"` / `"analogOutput"`
   to `ResolveStep` (line 51) → `new ResolvedStep(null, ResolveContract(step.DigitalOutput!,
   "DigitalOutput", where, errors))`. `ResolveContract` (line 348) already enforces
   `contract.MatchingContractType == expectedType`. ⚠ CONFIRM the exact `MatchingContractType` string
   for output contracts in `ConfigurationOutput` (inputs are `"DigitalInput"`/`"AnalogInput"`; outputs
   are presumably `"DigitalOutput"`/`"AnalogOutput"` — verify before hard-coding). `ResolvedStep`
   already has a `Contract` slot, so no record change needed.

6. **`ScenarioConditions`** (bottom of `ScenarioResolver.cs`): add
   `public static bool IsSatisfied(ScenarioOutputAssert condition, object? live)` → calls the existing
   private `Evaluate(above, below, equalTo, notEquals, oneOf, tolerance, live, null, false)`. Reuses
   ALL comparator semantics unchanged.

7. **`Vion.Dale.DevHost/Scenarios/ScenarioRunner.cs`** (READ THIS FILE FIRST — not yet read): add
   execution cases for the new kinds that read via `control.GetDigitalOutput`/`GetAnalogOutput`
   (using `resolved.Contract!.ServiceProviderId/ServiceId/ContractId`) and assert via
   `ScenarioConditions.IsSatisfied(step.DigitalOutput!, live)`, setting the step result detail
   (success/fail message — mirror how `expect` builds its detail). Also update the report
   `Describe`/`Argument` helpers to handle the new kinds (label = `Block.Contract`).

8. **`Vion.Dale.DevHost/Scenarios/scenario.schema.json`**: add `digitalOutputStep` + `analogOutputStep`
   `$defs` (contract-ref + comparator), add them to the **steps** `oneOf` (NOT the setup `oneOf`). The
   CLI's embedded copy is a LINKED resource (auto-syncs); per-project `.dale/scenario.schema.json` are
   regenerated from the host (no manual edit).

## Tests (TDD) + adoption

- **Failing test first** (`Vion.Dale.DevHost.Test/DevHostSmokeShould.cs`, `[TestCategory("Smoke")]`):
  boot the committed **`Vion.Dale.DevHost.SmokeHost` `IoBlock`** (it has `IDigitalOutput ActiveOutput`,
  `IAnalogOutput EchoOutput`, and a `[Timer(1)] OnTick` that mirrors `ActiveOutput.Set(IsEnabled)` /
  `EchoOutput.Set(CurrentLevel)`). Scenario: `digitalInput EnableInput=true` + `analogInput
  LevelInput=3.3` + `waitUntil io.IsEnabled==true` + `advance {seconds:1}` (fires OnTick → outputs
  mirror) + `digitalOutput {block:"io", contract:"ActiveOutput", equals:true}` + `analogOutput
  {block:"io", contract:"EchoOutput", equals:3.3, tolerance:0.001}`. Run to `succeeded`. It fails today
  (unknown step / validation error); implement until green.
- Verify each scenario green by booting a real stepped host (the `devhost-smoke` skill's pattern:
  `DALE_DEVHOST_STEPPED=1` + `DALE_DEVHOST_NO_BROWSER=1`, cwd = the host's dir, POST
  `/api/scenarios/{id}/apply` then poll `/run`).
- **Adopt** in the fixtures: extend `Vion.Dale.DevHost.SmokeHost/scenarios/io-control.scenario.json`
  to assert the outputs; add a `digitalOutput` assert to
  `examples/Vion.Examples.ToggleLight/scenarios/toggle-light.scenario.json` on Light's
  `DigitalOutput` contract after the light turns on (this is the user's original ask — close the
  input→light→output loop; today it only asserts `Light.On`).
- **Docs:** update the `devhost-smoke` SKILL.md (Tier 1 now covers output assertion), RFC 0008 cookbook
  `docs/rfcs/0008-unified-scenario-topology.md` §11 (the new step types), and template AGENTS.md
  (`templates/vion-iot-library/AGENTS.md` — list `digitalOutput`/`analogOutput`).

## Verification gate (this repo's discipline)

- End each increment with `dotnet test Vion.Dale.Sdk.sln` green. ⚠ A transient
  `CSC error LAMA0601: ... Insufficient system resources` can occur after many host boots (process
  leak) — fix by `dotnet build-server shutdown` + kill stray `dotnet`/`VBCSCompiler`/`MSBuild`, then
  retry (use `--no-build` if the sln just built).
- `pwsh scripts/cleanup-code.ps1` (apply) → review `git diff` → it's idempotent; `-Verify` is the CI
  gate (passes only on a committed/clean tree).
- Run the `devhost-smoke` skill (Tier 1 `dotnet test --filter TestCategory=Smoke`; Tier 2 live UI if
  the SPA/SignalR is touched — here it's mostly the API + runner, so Tier 1 + an output-assert smoke
  is the core).
- **Snapshot:** `IDevHostControl` change → regenerate `docs/snapshots/publicapi-manifest.json`
  (the snapshot bot auto-commits it on PR; reconcile before pushing, never force-push over it — see
  the `cli-help-snapshot-bot` memory). The new web endpoints/scenario steps don't affect the CLI help
  snapshot.
- Conventional commits; commit per verified increment; open a fresh PR to `main`. Don't commit/push
  until ready.

## Modbus (the user also asked "is it possible for modbusrtu?") — REPORT, don't build

Verdict: **not a clean extension; it's a separate, larger feature.** (1) `IModbusRtu` is NOT mocked in
the DevHost at all today (unlike digital/analog HAL) — Modbus blocks don't fully run there (reads/
writes queue with no responder), which is why the ModbusRtu example is boot-only. (2) Different model:
addressed registers + function codes + multi-register typed values with byte/word-order encoding, not
a scalar bool/double — "expect output == value" doesn't map. (3) Needs a full `MockModbusRtuHandler`
(FC routing, register state, correlation-id) + `modbusRead`/`modbusWrite` step shapes + encoding
helpers. For Modbus determinism today the **TestKit** (`SimulateReadResponse` / `ModbusResponseBuilder`,
in `Vion.Dale.Sdk.Modbus.Rtu.TestKit`) is the right layer (unit tests). Recommend scoping Modbus
observability as its own RFC if wanted; keep the ModbusRtu DevHost example boot-only. Mention this to
the user and let them decide.

## Key gotchas
- `IDevHostControl` is `[PublicApi]` (snapshot). Mock-handler actors are looked up by `nameof(...)`
  (e.g. `DigitalOutputHandler`), but you read output values from the **DevHostControl event cache**,
  not the handler.
- Output value is null until the block actually calls `.Set(...)` — pair the assert with
  `advance`/`waitUntil` so the timer/handler has fired.
- Name paths + contract identifiers are case-sensitive; enums compare by member name.
- The `ResolvedStep` record already carries both `Property` and `Contract` — output steps use the
  `Contract` slot (like `digitalInput`/`analogInput`).
