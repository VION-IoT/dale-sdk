# RFC 0010: Service-provider contracts are scenario-testable, uniformly

Status: **Draft** (DF-27; supersedes the hardcoded HAL mock path).
Author: jonas.bertsch. Date: 2026-06-22.
Revision 2: handler-side `[ScenarioWire]` + a DevHost codec (not a contract-side type); discovery via the
runtime's own convention scan; a scope boundary (only **value** contracts are in scope — Modbus RTU is
request/response and deferred, HTTP / Modbus-TCP are direct-DI and out). Foundation implemented on branch
`feat/serviceprovider-contract-scenario-support` (the `[ScenarioWire]` attribute + `ScenarioWireCodec`).

One sentence: make every `[ServiceProviderContractType]` **value** contract — the SDK's own digital/analog
I/O and any third-party contract like PPC — drivable and assertable from a committed `*.scenario.json`
through **one** generic DevHost handler and **one** generic step pair, with **no hardcoded contract support
in the core** and **no change to any production code path**.

## Motivation

A scenario can drive the four HAL contracts (`digitalInput` / `analogInput` / `digitalOutput` /
`analogOutput`) because the DevHost ships four **hardcoded mock handlers** and the scenario vocabulary
ships four **hardcoded step kinds**. Anything else — a consumer's custom service-provider contract such
as Ecocoach's `IPowerPlantControlGrid` (PPC) — has **no route**: the contract is the external MQTT
boundary, the DevHost wires no stand-in for it, and the scenario format has no step to address it
(DF-27). The only ways to deliver a PPC demand are the MQTT bridge (no broker in the stepped host) or a
C# TestKit call (`RaiseDemandReceived`, not reachable from JSON). So a committed, portable scenario
through the **real** adapter is impossible.

The fix is not to add a *second* hardcoded family. It is to **delete the special-casing**: the four HAL
contracts become ordinary service-provider contracts handled by the same generic mechanism every
third-party **value** contract uses. The SDK eats its own dog food. The four duplicated `MockHal*Handler`
classes collapse into one generic stand-in, and the DevHost simply becomes a faithful mirror of the
runtime — which already discovers handlers by convention (see §1). (Which contracts this reaches, and which
stay out, is the scope boundary in §4 — value contracts only.)

## How service-provider contracts work today (grounded)

A service-provider contract has **two actors on two repos' worth of code**, and that asymmetry is the
whole reason this is non-trivial:

**Consumer side (a `LogicBlock` owns the contract).** A contract is a `LogicBlockContractBase` subclass
implementing the typed interface, e.g. `DigitalInput : LogicBlockContractBase, IDigitalInput`. It
hardcodes the **name** of the actor that services it and dispatches inbound `ContractMessage<TWire>` to
a typed event:

```csharp
public partial class DigitalInput : LogicBlockContractBase, IDigitalInput
{
    public override string ContractHandlerActorName { get; protected set; } = nameof(DigitalInputHandler);
    public event EventHandler<bool>? InputChanged;

    public override void HandleContractMessage(IContractMessage m)
    {
        switch (m) { case ContractMessage<DigitalInputChanged> c: InputChanged?.Invoke(this, c.Data.Value); break; }
    }
}
```

An **output** contract additionally sends outbound: `DigitalOutput.Set(bool) =>
SendToContractHandler(new ContractMessage<SetDigitalOutput>(LogicBlockContractId, new SetDigitalOutput(value)))`.

At link time the SDK resolves the handler **by that hardcoded name**:
`contract.SetLinkedContractHandler(actorContext.LookupByName(contract.ContractHandlerActorName))`
([`LogicBlockBase.cs:115`](../../Vion.Dale.Sdk/Core/LogicBlockBase.cs#L115)). Nothing ever rewrites the
name; it is a compile-time `nameof(...)`.

**Provider side (the external MQTT boundary).** The provider role is *exclusively*
`ServiceProviderHandlerBase : IServiceProviderHandlerActor : IMqttHandlerActor` — it subscribes to broker
topics and pushes inbound state to consumers via `ForwardToLogicBlocks<TChanged>(contractId, changed)`
([`ServiceProviderHandlerBase.cs:209`](../../Vion.Dale.Sdk/Abstractions/ServiceProviderHandlerBase.cs#L209)).
**No `LogicBlock` can be a provider** — the SP family *is* the external-system boundary by design.

**DevHost today.** It hardcodes four mock providers — created under the four handler class names
([`DevLogicSystemInitializer.cs:150`](../../Vion.Dale.DevHost/DevLogicSystemInitializer.cs#L150)) and fed
one shared `LinkLogicBlockContractActors` map fanned to exactly four literal names (`:294-297`). Each
`MockHal*Handler` is a line-for-line reimplementation of `ForwardToLogicBlocks` /
`FindMappedServiceProviderContracts`, differing only by value type and message struct. A custom contract
(PPC) gets a fabricated mock *config* from `AutoCreateServiceProviders` but **no handler actor under its
name**, so the consumer's `LookupByName(nameof(PowerPlantControlGridHandler))` finds nothing and the
contract is inert. **That is DF-27.**

## Design

### 1. One generic DevHost handler, registered under the contract's own name

Replace the four `MockHal*Handler` actors with a single generic
**`ServiceProviderContractHandler : ServiceProviderHandlerBase`** in the DevHost. At wire time the host:

1. **Discovers the service-provider handler types by convention — exactly as the runtime already does.**
   The production runtime is *not* hardcoded: it scans `assemblies.GetConcreteTypes(typeof(IServiceProviderHandlerActor))`
   and creates/links each handler under its class name (`Dale/Program.cs` `CreateMqttHandlerActors`;
   `LogicSystemConfigurationInitializer` contract-linking). The DevHost loads the same assemblies, so the
   same scan finds `DigitalInputHandler … ModbusRtuHandler` **and** a consumer's `PowerPlantControlGridHandler`.
   The four-name hardcoding in `DevLogicSystemInitializer.cs:150-153 / :294-297` is the **only** place that
   isn't already generic.
2. For each scanned handler type, **creates one generic stand-in registered under that handler's class
   name** — the name the consumer's contract `ContractHandlerActorName` already looks up — instead of the
   real MQTT handler (which needs a broker). It reads the handler's `[ScenarioWire]` (below) to know how to
   build/decode the contract message.
3. Includes each in the same `LinkLogicBlockContractActors` fan-out, so its `ContractLogicBlockActorReferences`
   is populated and `ForwardToLogicBlocks` reaches the consumer. The link map is already built from **all**
   contracts, unfiltered — `AutoCreateServiceProviders` (`DevConfigurationBuilder.cs:443`) fabricates a
   mapping for every `[ServiceProviderContractType]` — so a custom contract is already in it; only the create
   + fan-out were hardcoded.

**The decisive property: because the stand-in registers under the name the consumer's contract *already*
looks up, `LogicBlockBase.cs:115` is unchanged and no production code path is touched.** An earlier
"retarget the handler name with a dev/test override map on the production path" idea is **not needed** — it
was an artifact of bolting a *second* actor alongside the real one. Here the stand-in simply *is* the
handler, under the expected name, the way the four HAL mocks always were. No override map, no production
gate, no leakage surface, no coordination with the private runtime.

### 2. How the generic handler stays type-agnostic: `[ScenarioWire]` + a DevHost codec

`ForwardToLogicBlocks<TChanged>` needs the **exact** struct the consumer's `HandleContractMessage` switches
on, and the generic handler lives in the DevHost and cannot reference a consumer's `PpcDemandGridReceived`.
A switch-case's generic type is not reflectable, so the **handler declares** its wire struct with one
attribute — and because the DevHost discovers handlers by the convention scan, it reads the attribute off
the scanned type with **no instance** needed:

```csharp
[ScenarioWire(Inbound = typeof(DigitalInputChanged))]   // an input — digital/analog input, PPC demand
public partial class DigitalInputHandler : ServiceProviderHandlerBase { … }

[ScenarioWire(Outbound = typeof(SetDigitalOutput))]     // an output — digital/analog output
public partial class DigitalOutputHandler : ServiceProviderHandlerBase { … }
```

The split is deliberate — it keeps the scope **structural**, not just named:

- **`[ScenarioWire]` (in `Vion.Dale.Sdk`)** is a pure declarative marker (`Type Inbound` / `Type Outbound`).
  The production runtime reaches hardware over MQTT (FlatBuffers) and **never reads it** — it carries no
  runtime behaviour. This attribute is the *entire* SDK surface the feature adds.
- **`ScenarioWireCodec` (internal to `Vion.Dale.DevHost`)** is where the JSON↔struct lives. It reflects over
  the declared `Type` to build the exact closed `ContractMessage<TInbound>` from a scenario JSON value
  (drive) and decode an output command back to a value (assert). A single-field struct round-trips as its
  scalar (so a digital input is driven by `true`); a multi-field struct as a JSON object; enums by name
  (`JsonSerialization.DefaultOptions`). It produces the **same CLR wire payload the production handler
  forwards** — just sourced from a JSON value instead of a FlatBuffer frame, on the opposite side of the
  handler, so the two encodings never meet.

The author's only obligation is that one attribute line on the handler (which already constructs the wire
struct). A source generator could derive it, but is deliberately out of scope — a whole generator to save
one type-checked line is poor cost/benefit.

### 3. One generic step pair; the four HAL kinds are deleted (format v2)

The scenario vocabulary gains exactly **one** generic pair and loses the four hardcoded kinds:

```jsonc
// drive an input contract (replaces digitalInput / analogInput)
{ "serviceProviderSet":    { "logicBlock": "Io",  "contract": "EnableInput" }, "value": true }
{ "serviceProviderSet":    { "logicBlock": "ppc", "contract": "gridDemand"  }, "value": { "valid": true, "scope": "Total", "activePowerW": 1500 } }

// assert an output contract (replaces digitalOutput / analogOutput)
{ "serviceProviderExpect": { "logicBlock": "Io",  "contract": "ActiveOutput" }, "equals": true }
```

- Addressing is uniform `{ logicBlock, contract }` — the same `ResolveContract` the HAL steps use today
  ([`ScenarioResolver.cs`](../../Vion.Dale.DevHost/Scenarios/ScenarioResolver.cs)), with the
  `matchingContractType` gate generalized from "one of four" to "any `[ServiceProviderContractType]`".
- The `value` is the contract's wire struct (a scalar for digital/analog, a JSON object for PPC).
  Per-topology schema enrichment types it (the DF-25 typed-`set`-value work — this RFC is its first real
  consumer; until it lands the value is untyped-but-runtime-checked, exactly like a struct `set` today).
- **Direction is read off the contract**, never guessed: `[ServiceProviderContractType].Consumers`
  multiplicity — `ZeroOrOne` (single-writer) ⇒ output (assert only); `ZeroOrMore` ⇒ input (drivable). A
  `serviceProviderSet` on an output is a validation error.
- `set` / `expect` / `waitUntil` / `advance` / `settle` / `wait` are untouched — those are the
  service-*property* and time planes, a different routing plane from the contract plane.

`digitalInput` / `analogInput` / `digitalOutput` / `analogOutput` are **removed**, not aliased — the
core carries no contract-specific vocabulary. This is a **breaking format change → `version: 2`** (see
Migration).

### 4. Scope boundary — which IO is scenario-testable

Not all external IO fits this mechanism; there are **three** distinct kinds, and only the first is in scope:

| mechanism | wiring | examples | verdict |
|---|---|---|---|
| **value contract** | `[ServiceProviderContractType]`, fire-and-forget | digital/analog I/O, PPC demand | **this RFC** — `serviceProviderSet` / `serviceProviderExpect` |
| **request/response contract** | `[ServiceProviderContractType]`, but with in-process `Action` callbacks | **Modbus RTU** (`IModbusRtu`) | **deferred** — a request triggers a response, so it needs a *response-fixture* vocabulary on the same actor plane, not a value-drive; `[ScenarioWire]` does not apply |
| **direct-DI client** | an injected service, *off* the actor/contract plane | **HTTP** (`ILogicBlockHttpClient`), **Modbus-TCP** (`ILogicBlockModbusTcpClientFactory`) | **out of scope** — the contract-plane mechanism can't reach it; scripting HTTP bodies / register maps in scenario JSON is protocol mocking the C# TestKit does better. Keep the **Ref\*** substitute (scenario) / **TestKit** (real block) |

So scenarios cover the logic network **plus its service-provider *value*-contract boundary** — and stop
there. Request/response (Modbus RTU) is a coherent future extension *on the same plane* (a fixture vocab);
direct-DI external protocols stay in the TestKit/integration layer with Ref\* as the scenario-side stand-in.
That keeps scenarios at their valuable altitude (deterministic logic verification) rather than drifting into
integration testing. The structured-payload codec is still exercised — PPC's multi-field struct is the
non-scalar case that proves it generalizes past `bool`/`double`.

## Authoring guide — make your service provider scenario-testable

This is the section that decides whether the feature is usable. Two worked examples.

### Simple case: a digital input (what the SDK itself does)

The SDK's `DigitalInputHandler` is the handler for a digital-input contract. Under this RFC it needs
**one** line — the `[ScenarioWire]` attribute — and the contract is then scenario-drivable with no change
to the contract or the consuming block:

```csharp
[ScenarioWire(Inbound = typeof(DigitalInputChanged))]   // <- the one new line
public partial class DigitalInputHandler : ServiceProviderHandlerBase { /* unchanged */ }
```

Scenario (a SmokeHost-style `io-control`):

```jsonc
{ "serviceProviderSet": { "logicBlock": "Io", "contract": "EnableInput" }, "value": true }
{ "waitUntil": { "property": "Io.IsEnabled", "equals": true } }
{ "advance": { "seconds": 1 } }
{ "serviceProviderExpect": { "logicBlock": "Io", "contract": "ActiveOutput" }, "equals": true }
```

The handler carries the one attribute; the **contract and the consuming block are untouched**. The interface
keeps `[ServiceProviderContractType("digitalInput", Consumers = ZeroOrMore)]`; the consuming block keeps
`[ServiceProviderContractBinding]` on its property.

### Rich case: PPC (the DF-27 unblock)

Ecocoach's `PowerPlantControlGridHandler` is the handler for a real third-party contract whose wire struct
`PpcDemandGridReceived` carries multiple fields. The author adds the same one line, to the handler:

```csharp
[ScenarioWire(Inbound = typeof(PpcDemandGridReceived))]   // <- the one new line
public partial class PowerPlantControlGridHandler : ServiceProviderHandlerBase { /* unchanged */ }
```

Then the committed scenario drives the **real** `PpcGridTF8360` adapter end-to-end — the portable
replacement for today's C# `RaiseDemandReceived`:

```jsonc
{
  "id": "ppc-grid-demand-folds-into-em", "topology": "em-closed-loop",
  "steps": [
    { "serviceProviderSet": { "logicBlock": "PpcGridTF8360", "contract": "ppcGrid" },
      "value": { "valid": true, "scope": "Total", "activePowerW": 1500 } },
    { "settle": {} },
    { "expect": { "property": "EnergyManager.GridExportLimitKw", "equals": 1.5, "tolerance": 0.01 } }
  ]
}
```

`serviceProviderSet` → the generic handler builds `ContractMessage<PpcDemandGridReceived>` via
`Wire.MakeInbound` → `ForwardToLogicBlocks` → the **real** `PowerPlantControlGrid.HandleContractMessage`
fires `DemandReceived` → the **real** `PpcGridTF8360` folds it into steering → `expect` asserts the EM
state. No provider block, no broker, no C#.

### What the author writes vs. gets for free

| Author writes | Author gets for free |
|---|---|
| The contract + handler **as today** | The DevHost stand-in handler (generic; no per-contract mock) |
| **One `[ScenarioWire]` line** on the handler | `serviceProviderSet` / `serviceProviderExpect` for that contract |
| `[ServiceProviderContractType]` (already there) | Direction + ambiguity policy from its multiplicity |
| The wire struct (already there) | The `value` shape (and typed schema autocomplete once DF-25 lands) |
| Nothing on the consuming block | Drive/assert through the **real** consumer, deterministically under stepping |

## Migration (format v1 → v2)

Removing the four kinds invalidates every scenario that uses them. The rewrite is mechanical:

| v1 | v2 |
|---|---|
| `{ "digitalInput": { block, contract }, "value": v }` | `{ "serviceProviderSet": { logicBlock, contract }, "value": v }` |
| `{ "analogInput":  { block, contract }, "value": v }` | `{ "serviceProviderSet": { logicBlock, contract }, "value": v }` |
| `{ "digitalOutput": { block, contract }, "equals": v }` | `{ "serviceProviderExpect": { logicBlock, contract }, "equals": v }` |
| `{ "analogOutput":  { block, contract }, "equals": v }` | `{ "serviceProviderExpect": { logicBlock, contract }, "equals": v }` |

Sites to migrate in lockstep (the format lives in four parallel places + the docs):
- committed scenarios: SmokeHost `io-control`, the examples, the consumer's HAL scenarios;
- the generic JSON schema + per-topology enrichment;
- the CLI `ScenarioFileChecks`;
- the SPA `fileSteps`;
- RFC 0006 + the §11.7 cookbook + `docs/devhost-ui/examples`.

`version` bumps `1 → 2`; a v1 file is rejected with a one-line "use serviceProviderSet (RFC 0010)" hint.

## Implementation & staging

1. **This RFC. The de-risking spike + the foundation are done (✅).** The wiring open question was answered
   from source (see Risks); a throwaway spike (`spike/serviceprovider-generic-handler`) proved the codec
   builds the exact closed `ContractMessage<T>` a consumer's `HandleContractMessage` switch matches — from a
   scenario JSON value, for both a scalar HAL wire struct and a multi-field PPC struct — via one uniform
   decode. The `[ScenarioWire]` attribute + the internal `ScenarioWireCodec` (with tests) are committed on
   `feat/serviceprovider-contract-scenario-support`. No surprises; the MVP is unblocked.
2. **MVP** (cut hard): the generic handler + `Wire`; auto-discovery replacing the hardcoded four;
   `serviceProviderSet`/`serviceProviderExpect` across all four vocabulary sites + `ScenarioResolver`
   type-gate; **delete** the four kinds (v2) + migrate every committed file; Modbus + PPC as the struct
   cases. **Defer:** typed `value` schema enrichment (DF-25 — works untyped meanwhile),
   `serviceProviderMappings` (a real provider *block* driving the contract), request/response contracts.
3. **Verify:** `devhost-smoke` (both tiers — it touches the runtime + scenario runner + every contract
   step) and `scripts/cleanup-code.ps1` before the PR.

Realistic effort: **~6–9 dev-days**, dominated by the four-vocabulary-site change + the migration + the
struct codec, not by the handler (which reuses `ForwardToLogicBlocks` verbatim).

## Risks & open questions

- **RESOLVED (spike ✅):** does `AutoCreateServiceProviders` already build the
  `LinkLogicBlockContractActors` map entries for a *custom* contract, so a handler registered under its
  name "just works"? **Yes.** `AutoCreateServiceProviders` adds a `DevContractMapping` for **every**
  `[ServiceProviderContractType]` contract unconditionally
  ([`DevConfigurationBuilder.cs:443`](../../Vion.Dale.DevHost/DevConfigurationBuilder.cs#L443)), and
  `LinkContractsWithMockHandlers` builds the link map from **all** contract mappings, unfiltered
  ([`DevLogicSystemInitializer.cs:271-289`](../../Vion.Dale.DevHost/DevLogicSystemInitializer.cs#L271)) —
  only the four-handler **create** and the four-`SendTo` **fan-out** (`:294-297`) are HAL-specific. So a
  custom contract is already in the map; the MVP change is exactly to (a) create one generic handler per
  discovered contract under its `ContractHandlerActorName` and (b) loop the fan-out over them. Still add a
  **positive round-trip** smoke (not a "no throw") to guard the silent-drop trap
  (`SendToContractHandler` no-ops on an empty mapping — `LogicBlockContractBase.cs:111-116`).
- **Stepping determinism:** the bridge must publish via `ForwardToLogicBlocks` (one hop, the contract
  plane), **never** a `[ServiceProperty]` setter (a different async plane that never reaches contracts) —
  so a single `settle`/`advance` deterministically drains the delivery, exactly as `digitalInput` does
  today.
- **Ambiguity:** exactly one handler per contract type (the singleton model). Two providers for one
  contract type is a hard load error (reuses the DF-19 multiplicity guard).
- **Production safety:** there is **no** production code change in this design (the win over the earlier
  retarget). The generic handler + codec live only in the DevHost; the SDK adds the `[ScenarioWire]`
  attribute (inert metadata). Confirm it is never read on the production hot path.
- **`--export-config` / `--export-topology` fidelity:** the generalized contract set must already be in
  the exported config for `dale scenario validate` to type `serviceProviderSet` values; it reads the
  export, not the topology file.

## Usability self-assessment (from the author's seat)

Putting myself in the shoes of (a) a PPC author and (b) someone shipping a trivial digital input:

- **The good.** The author writes their contract + handler exactly as today plus **one explicit,
  type-checked attribute** (`[ScenarioWire(Inbound = typeof(...))]`) on the handler. They author **no mock**,
  touch **no topology** (auto-discovery), and the consuming block
  is untouched. The scenario reads naturally and addresses the contract the same way for a `bool` input
  and a multi-field PPC struct. That clears the "is it usable" bar.
- **The honest gaps the docs must close.** (1) Until DF-25's typed-value enrichment lands, the `value` for
  a struct contract has **no editor autocomplete/validation** — it works at runtime but a mis-cased field
  binds silently (the exact DF-25 papercut). The authoring guide must say "your `value` matches your wire
  struct's fields (camelCase); typo = silent empty until DF-25." (2) The author must understand **which
  wire struct is the inbound one** (the `HandleContractMessage` case) vs the outbound command — obvious
  for I/O, less so for a request/response contract (out of MVP scope, but the guide must say so). (3) The
  direction rule (multiplicity ⇒ drivable vs assert-only) is implicit; the guide needs a one-liner so an
  author doesn't `serviceProviderSet` an output and get a confusing error.
- **Verdict.** With the `Wire` convention + an authoring guide that states those three things, this is
  usable by a contract author with no DevHost knowledge. One declared, type-checked line is an acceptable,
  explicit convention — a source generator to eliminate it is **deliberately out of scope** (a poor
  cost/benefit for one line), and explicit beats magic here.
