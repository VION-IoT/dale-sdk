# RFC 0010: Service-provider contracts are scenario-testable, uniformly

Status: **Draft** (DF-27; supersedes the hardcoded HAL mock path).
Author: jonas.bertsch. Date: 2026-06-22.

One sentence: make **every** `[ServiceProviderContractType]` contract — the SDK's own digital/analog
I/O and Modbus, and any third-party contract like PPC — drivable and assertable from a committed
`*.scenario.json` through **one** generic DevHost handler and **one** generic step pair, with **no
hardcoded contract support in the core** and **no change to any production code path**.

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
third-party contract uses. The SDK eats its own dog food. Two consequences fall out for free:

- **Modbus RTU becomes scenario-drivable in the DevHost for the first time** — it has a real MQTT
  handler (`ModbusRtuHandler`) but **no DevHost mock today**, so it is the structured-payload stress
  test that proves the mechanism generalizes past scalars.
- The four duplicated `MockHal*Handler` classes collapse into one.

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

1. **Discovers** every bound `[ServiceProviderContractType]` contract in the topology — it already does
   this in `AutoCreateServiceProviders` via `GetContractProperties`.
2. For each distinct contract, **creates one `ServiceProviderContractHandler` registered under that
   contract's `ContractHandlerActorName`** — `nameof(DigitalInputHandler)` for digital input,
   `nameof(PowerPlantControlGridHandler)` for PPC. This is exactly what the DevHost already does for the
   four HAL names; we stop hardcoding the set and loop over the discovered set instead.
3. Includes each in the same `LinkLogicBlockContractActors` fan-out, so the handler's
   `ContractLogicBlockActorReferences` is populated and `ForwardToLogicBlocks` reaches the consumer.

**The decisive property: because the handler registers under the name the consumer's contract *already*
looks up, `LogicBlockBase.cs:115` is unchanged and no production code path is touched.** The earlier
"retarget the handler name with a dev/test override map on the production `LinkRuntimeActors` path" idea
is **not needed** — it was an artifact of bolting a *second* actor alongside the real one. Here the
generic handler simply *is* the stand-in, under the expected name, the way the four HAL mocks always
were. No override map, no production gate, no leakage surface, no coordination with the private runtime.

### 2. How the generic handler stays type-agnostic: the wire descriptor

`ForwardToLogicBlocks<TChanged>` is generic and `ContractMessage<TChanged>` must carry the **exact**
struct the consumer's `HandleContractMessage` switches on, so the generic handler — which lives in the
DevHost and cannot reference a consumer's `PpcDemandGridReceived` — needs a typed bridge from "a JSON
value" to "the right `ContractMessage<T>`". A switch-case's generic type is not reflectable, so the
contract **declares** its wire shape, one line next to `ContractHandlerActorName`:

```csharp
// inbound-only (driven by the scenario; SP -> block). PPC and digital/analog input:
public override ServiceProviderWire Wire { get; } = ServiceProviderWire.Inbound<PpcDemandGridReceived>();

// output (block writes; asserted by the scenario). digital/analog output:
public override ServiceProviderWire Wire { get; } = ServiceProviderWire.Outbound<SetDigitalOutput, DigitalOutputChanged>();
```

`ServiceProviderWire` (new, in `Vion.Dale.Sdk`) carries strongly-typed factories closed over `T`:
`JsonElement -> ContractMessage<TChanged>` (drive) and `ContractMessage<TCommand> -> JsonElement` (assert
read-back), using the **existing** `Vion.Contracts.PropertyValueCodec` for the JSON↔struct conversion —
the same codec a consumer's `set` of a struct already uses (DF-25). No new codec, no runtime reflection
in the message path; the generic handler calls `contract.Wire.MakeInbound(id, json)` and sends the
result.

> **Convention, not magic.** This one line is the author's only new obligation, and it is explicit and
> type-checked. A source generator *could* emit `Wire` from the `HandleContractMessage` switch to reach
> *zero* author boilerplate — but that is **deliberately not pursued**: a whole generator, with its
> build-time and debugging surface, to save one declared, type-checked line is a poor cost/benefit trade.
> Revisit only if `Wire` ever grows past one line, or the per-contract boilerplate otherwise multiplies.

### 3. One generic step pair; the four HAL kinds are deleted (format v2)

The scenario vocabulary gains exactly **one** generic pair and loses the four hardcoded kinds:

```jsonc
// drive an input contract (replaces digitalInput / analogInput)
{ "serviceProviderSet":    { "block": "Io",  "contract": "EnableInput" }, "value": true }
{ "serviceProviderSet":    { "block": "ppc", "contract": "gridDemand"  }, "value": { "valid": true, "scope": "Total", "activePowerW": 1500 } }

// assert an output contract (replaces digitalOutput / analogOutput)
{ "serviceProviderExpect": { "block": "Io",  "contract": "ActiveOutput" }, "equals": true }
```

- Addressing is uniform `{ block, contract }` — the same `ResolveContract` the HAL steps use today
  ([`ScenarioResolver.cs`](../../Vion.Dale.DevHost/Scenarios/ScenarioResolver.cs)), with the
  `matchingContractType` gate generalized from "one of four" to "any `[ServiceProviderContractType]`".
- The `value` is the contract's wire struct (a scalar for digital/analog, a JSON object for PPC/Modbus).
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

### 4. Modbus and the structured payload

Modbus RTU is just another service-provider contract. Under this design it gets a
`ServiceProviderContractHandler` automatically and becomes scenario-drivable for the first time — but its
wire structs (register frames, function codes) are richer than `bool`/`double`, so it (with PPC) forces
the `value` codec to handle arbitrary structs from day one. This is the right forcing function: if the
mechanism handles Modbus and PPC, it handles everything, and there is no scalar-only shortcut to regret.

## Authoring guide — make your service provider scenario-testable

This is the section that decides whether the feature is usable. Two worked examples.

### Simple case: a digital input (what the SDK itself does)

The SDK's `DigitalInput` is already a service-provider contract. Under this RFC it needs **one** line —
the `Wire` declaration — and it is then scenario-drivable with zero further work:

```csharp
public partial class DigitalInput : LogicBlockContractBase, IDigitalInput
{
    public override string ContractHandlerActorName { get; protected set; } = nameof(DigitalInputHandler);
    public override ServiceProviderWire Wire { get; } = ServiceProviderWire.Inbound<DigitalInputChanged>();   // <- the one new line
    public event EventHandler<bool>? InputChanged;
    public override void HandleContractMessage(IContractMessage m) { /* unchanged */ }
}
```

Scenario (a SmokeHost-style `io-control`):

```jsonc
{ "serviceProviderSet": { "block": "Io", "contract": "EnableInput" }, "value": true }
{ "waitUntil": { "property": "Io.IsEnabled", "equals": true } }
{ "advance": { "seconds": 1 } }
{ "serviceProviderExpect": { "block": "Io", "contract": "ActiveOutput" }, "equals": true }
```

The author writes the contract as they do today **plus the `Wire` line**, and gets the full drive/assert
loop. The interface keeps `[ServiceProviderContractType("digitalInput", Consumers = ZeroOrMore)]`; the
consuming block keeps `[ServiceProviderContractBinding]` on its property. Nothing about the consumer
block changes.

### Rich case: PPC (the DF-27 unblock)

Ecocoach's `PowerPlantControlGrid` is a real third-party contract — a unidirectional SP→block contract
whose wire struct `PpcDemandGridReceived` carries multiple fields. The author adds the same one line:

```csharp
public partial class PowerPlantControlGrid : LogicBlockContractBase, IPowerPlantControlGrid
{
    public override string ContractHandlerActorName { get; protected set; } = nameof(PowerPlantControlGridHandler);
    public override ServiceProviderWire Wire { get; } = ServiceProviderWire.Inbound<PpcDemandGridReceived>();   // <- the one new line
    public event EventHandler<PpcDemandGridReceived>? DemandReceived;
    public override void HandleContractMessage(IContractMessage m) { /* unchanged */ }
}
```

Then the committed scenario drives the **real** `PpcGridTF8360` adapter end-to-end — the portable
replacement for today's C# `RaiseDemandReceived`:

```jsonc
{
  "id": "ppc-grid-demand-folds-into-em", "topology": "em-closed-loop",
  "steps": [
    { "serviceProviderSet": { "block": "PpcGridTF8360", "contract": "ppcGrid" },
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
| **One `Wire` line** on the contract | `serviceProviderSet` / `serviceProviderExpect` for that contract |
| `[ServiceProviderContractType]` (already there) | Direction + ambiguity policy from its multiplicity |
| The wire struct (already there) | The `value` shape (and typed schema autocomplete once DF-25 lands) |
| Nothing on the consuming block | Drive/assert through the **real** consumer, deterministically under stepping |

## Migration (format v1 → v2)

Removing the four kinds invalidates every scenario that uses them. The rewrite is mechanical:

| v1 | v2 |
|---|---|
| `{ "digitalInput": { block, contract }, "value": v }` | `{ "serviceProviderSet": { block, contract }, "value": v }` |
| `{ "analogInput":  { block, contract }, "value": v }` | `{ "serviceProviderSet": { block, contract }, "value": v }` |
| `{ "digitalOutput": { block, contract }, "equals": v }` | `{ "serviceProviderExpect": { block, contract }, "equals": v }` |
| `{ "analogOutput":  { block, contract }, "equals": v }` | `{ "serviceProviderExpect": { block, contract }, "equals": v }` |

Sites to migrate in lockstep (the format lives in four parallel places + the docs):
- committed scenarios: SmokeHost `io-control`, the examples, the consumer's HAL scenarios;
- the generic JSON schema + per-topology enrichment;
- the CLI `ScenarioFileChecks`;
- the SPA `fileSteps`;
- RFC 0006 + the §11.7 cookbook + `docs/devhost-ui/examples`.

`version` bumps `1 → 2`; a v1 file is rejected with a one-line "use serviceProviderSet (RFC 0010)" hint.

## Implementation & staging

1. **This RFC. The de-risking spike is done (✅).** The wiring open question was answered from source
   (see Risks), and a throwaway spike (`spike/serviceprovider-generic-handler`) proved the one new piece,
   `ServiceProviderWire`, builds the exact closed `ContractMessage<T>` a consumer's `HandleContractMessage`
   switch matches — from a scenario JSON value, for both a scalar HAL wire struct and a multi-field
   PPC/Modbus struct — via one uniform decode. No surprises; the MVP is unblocked.
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
  retarget). The generic handler lives only in the DevHost; the SDK adds `Wire` (inert metadata) and the
  `ServiceProviderWire` type. Confirm `Wire` is never read on the production hot path.
- **`--export-config` / `--export-topology` fidelity:** the generalized contract set must already be in
  the exported config for `dale scenario validate` to type `serviceProviderSet` values; it reads the
  export, not the topology file.

## Usability self-assessment (from the author's seat)

Putting myself in the shoes of (a) a PPC author and (b) someone shipping a trivial digital input:

- **The good.** The author writes their contract exactly as today plus **one explicit, type-checked line**
  (`Wire = ServiceProviderWire.Inbound<...>()`), sitting right next to the `ContractHandlerActorName` they
  already write. They author **no mock**, touch **no topology** (auto-discovery), and the consuming block
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
