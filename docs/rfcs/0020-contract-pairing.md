# RFC 0020 — Contract pairing: simulating the provider side of a service-provider contract

- **Status:** Draft — 2026-08-28. Design only; implementation not started.
- **Author:** jonas.bertsch (design drafted with Claude)
- **Origin:** consumer report from `logic-block-libraries` (ecocoach, 2026-08-27, HeatPumpSgReady
  port — the first production block whose whole hardware face is contract bindings). Their sim
  bench needs the DevHost to close a DO→DI loop; they verified it cannot, shipped a documented
  interim (scenario-mirrored drives), and filed VION-131/133. The same gap, one level up, leaves
  their bidirectional PPC contracts without a behaving peer (only scenario drives).
- **Related:** RFC 0010 (the scenario-wire foundation this extends; its §3 deferred drive-half and
  §4 request/response deferral are both resolved here), RFC 0011 (parked — §4's lease/cadence
  problem is answered for paired benches by sim blocks), RFC 0003 (`IDevHostControl`), RFC 0008
  (stepping/quiescence — the determinism this design preserves), RFC 0019 (precedent: declarative
  metadata on the contract, both halves derived mechanically).
  Jira: resolves VION-131; completes VION-129's deferred half; interacts with VION-133;
  orthogonal to VION-130/132.

## 1. Summary

A topology can declare that two service-provider contract endpoints are **one wire**:

```json
"contractPairings": [
  { "a": { "logicBlockName": "HeatPumpSgReady",          "contractIdentifier": "Sg1Output"   },
    "b": { "logicBlockName": "HeatPumpSgReadySimulator", "contractIdentifier": "Sg1Provider" } }
]
```

The DevHost then delivers each side's captured outbound as the other side's inbound — one actor
hop through the existing generic stand-in, at the JSON layer the scenario codec already speaks.
Which directions materialise is derived from the two handlers' `[ScenarioWire]` declarations;
per-direction wire-type identity is validated at topology load (§4.3) and a pairing with no valid
direction is refused with a message (devhost-conventions §3).

The host **never transforms** — it re-delivers a value along a declared wire. All behaviour
(device reactions, confirmations, controller responses) lives in ordinary **sim logic blocks**,
which bind new **provider faces**: the inverse of each consumer contract, authored by whoever owns
the contract, as the same interface/contract-class/handler trio that already exists. The SDK ships
`IDigitalOutputProvider` / `IDigitalInputProvider` (and analog twins); consumers ship provider
faces for their own contracts (PPC).

Because a pairing hop is a plain actor message, closed loops are visible to the RFC 0008
quiescence barrier: a closed-loop physics bench runs **stepped and deterministic** — something the
consumer's socket-based Modbus-TCP sims can never do — and identically on the real clock.

Three elements, no special cases:

1. **Complete the SDK output handlers' `[ScenarioWire]`** — `DigitalOutputHandler` gains
   `Inbound = typeof(DigitalOutputChanged)` (analog likewise), and the drive gate learns to key on
   a declared inbound rather than on `Consumers` (RFC 0010 §3's deferred item, now with its first
   instance). Output confirmations become scenario-drivable and pairing-deliverable; `OutputChanged`
   is no longer dead off production (VION-131).
2. **The pairing primitive** — topology-declared, structurally validated, capture→forward.
3. **Provider faces + documented sim patterns** — development-only by declaration (§4.8),
   including a reference "ideal I/O" echo block in the SmokeHost fixture and an authoring recipe
   in the docs; nothing behavioural ships in the host.

A deliberate non-feature: an earlier draft had a declarative `Confirmation = typeof(…)` sugar on
`[ScenarioWire]` (host-synthesised identity echo). Dropped — see §4.5.

## 2. What the consumer needs (evidence)

All verified in `logic-block-libraries` @ SDK 0.10.9, 2026-08-27/28.

- **DO→DI loopback.** `HeatPumpSgReady` drives two `IDigitalOutput`s
  (`HeatPumpSgReady.cs:181-185`); `HeatPumpSgReadySimulator`'s device face is two `IDigitalInput`s
  (`HeatPumpSgReadySimulator.cs:46-52`). Their change doc's `OPEN-HPS-DO-LOOPBACK` planned a
  DevHost bridge keyed by a shared contract mapping, verified against SDK source that none exists
  — *"even with both bindings contract-mapped to one shared endpoint"* — and resolved to an
  interim: the scenario hand-mirrors the asserted DO value into the sim's DI
  (`heat-pump-sg-ready-non-take-up.scenario.json:22-23`), with a step label instructing a human to
  eyeball the DO tile first. The scenario is not self-consistent: if the block's mode logic
  changed, the mirror would silently diverge and still pass. Codified as a corpus-wide gotcha
  (`AGENTS.md:527-533`).
- **Output confirmations.** The block subscribes `OutputChanged` to populate `SgContacts` and its
  D18 commissioning self-test records commanded-vs-echoed contact pairs
  (`HeatPumpSgReady.cs:620-639, 867-952`). `DigitalOutputChanged` is constructed only in
  production MQTT and the TestKit, so half that diagnostic is structurally dead in every DevHost
  bench (VION-131). Two shipped SDK examples (`ToggleLight/Light.cs:91`, `PingPong/Pong.cs:59`)
  subscribe the event as if it were live.
- **PPC.** Their four consumer-owned contracts (two bidirectional) are fully scenario-testable via
  RFC 0010 — drives via `serviceProviderSet`, outbound asserts per-field since VION-71/129. What
  is missing is a **behaving peer**: *"DevHost has no PPC SP simulator to drive a `demand`
  interactively"* (their `DevHost/CLAUDE.md:390-393`); a mirror cannot help because
  `PpcMeasurementSet` out ≠ `PpcDemandPvReceived` in — something must decide the demand.
- **The quality bar.** Their Modbus-TCP sims (register-map subsystems + a write-once host pump,
  ~200 lines per device) are the pattern to generalise — but they are wall-clock-only forever:
  socket threads are invisible to the quiescence barrier, so ~15 `sim-*` benches run as boot
  detectors, not asserting per-PR gates (their own analysis, `2026-06-24-ondevice-simulator-blocks-design.md:451-462`).

## 3. Why the DevHost cannot do this today

- One generic stand-in actor per handler **type** (`ServiceProviderContractHandlerScan.cs`,
  `DevLogicSystemInitializer.cs:304-327`): a DO capture and a DI drive live in different actors
  with no route between them.
- `ServiceProviderContractHandler.Capture` records to the `ServiceProviderOutputCache`, raises the
  UI event, and deliberately does not forward
  (`ServiceProviderContractHandler.cs:112-136`), pinned by
  `Capture_an_output_command_raising_the_generic_event_without_echoing`.
- `DigitalOutputHandler` declares `[ScenarioWire(Outbound = typeof(SetDigitalOutput))]` only
  (`DigitalOutputHandler.cs:18-20`); `DigitalOutputChanged` — dispatched by
  `DigitalOutput.HandleContractMessage` (`DigitalOutput.cs:44-53`) — is declared on no
  `[ScenarioWire]` anywhere, so no codec can build it.
- The scenario drive gate refuses any `Consumers == ZeroOrOne` contract
  (`ScenarioResolver.cs:405-414`); RFC 0010 §3 explicitly deferred the per-operation drive half
  "until an instance appears". This RFC is that instance.
- No topology vocabulary exists for "contract A's output feeds contract B's input".
  `DevConfigurationBuilder.ShareContract` (`DevConfigurationBuilder.cs:137`) implies shared
  endpoints but has zero call sites, and even a genuinely shared endpoint triple does not loop
  (routing is per handler type) — VION-133.

## 4. Design

### 4.1 The pairing primitive

`Capture` gains one lookup. After caching the command (so `serviceProviderExpect` is untouched)
and raising the UI event, it consults a pairing table built at topology load:
`(handlerActorName, ServiceProviderContractId)` → peer `(handlerActorName, ServiceProviderContractId)`.
On a hit it resolves the peer stand-in via `IActorContext.LookupByName` and sends it the existing
drive message — `MockSetServiceProviderInputMessage(peerContractId, capturedJson)` — which the
peer stand-in delivers through its ordinary `Drive` path (`MakeInbound` → typed
`ContractMessage<TInbound>` → every mapped block). No new transport, no new message types.

The JSON forward is an implementation choice, not a compatibility bridge: `Capture` already
decodes every command to JSON for the output cache and the UI, and re-driving that JSON reuses
the exact code path scenario drives use — one behaviour to test, one behaviour to reason about.
Pairs are type-identical by rule (§4.3), so nothing is ever adapted in flight. Production
encodings (FlatBuffers, the PPC packed binary) never appear: they live inside the real handlers,
which are precisely the components the stand-ins replace.

The no-forward invariant survives as the **default**: an unpaired capture behaves exactly as
today. The pinned test is reworded, not deleted.

### 4.2 Topology schema

A new optional `contractPairings` array; each entry is exactly two endpoints:

```json
{ "a": { "logicBlockName": "…", "contractIdentifier": "…" },
  "b": { "logicBlockName": "…", "contractIdentifier": "…" } }
```

- Keyed on **(block, contract binding)** — *not* on endpoint triples. `DevConfigurationBuilder`
  auto-fabricates an endpoint for every contract, so pairing needs nothing from
  `contractMappings` and stays clear of the shared-triple semantics VION-133 documents as
  twice-declared and unenforced. VION-133's cleanup proceeds independently; `ShareContract` is
  deleted or redefined there, not here.
- The declaration is symmetric; **active directions are derived**: a→b materialises iff a's
  handler declares an `Outbound` and b's an `Inbound` of the identical type (§4.3), and b→a
  likewise. A pairing with zero materialisable directions is refused at load.
- Fan-out is multiple entries (one output paired to two observing sims is legal). A physical
  channel feeding several consumers is modelled in a sim block — receive once, `Drive` each
  paired input. An entry whose endpoints coincide is rejected (self-pairing was the dropped
  Confirmation feature; see §4.5).
- C# fixtures get the equivalent `DevConfigurationBuilder.PairContracts(blockA, contractA, blockB, contractB)`.
- The SPA wiring view draws pairings as a distinct wire kind; the topology editor edits the list.

### 4.3 Wire-type identity — the pairing rule, and its validation

There is no field-mapping code, no transformation hook, and no structural adaptation: a direction
a→b materialises only when a's declared `Outbound` **is the same struct type** as b's declared
`Inbound`. Provider faces make this free — they reuse the consumer face's wire structs (§4.4), so
the canonical pair is exact by construction, and an accidental pairing of two unrelated contracts
that merely happen to share a JSON shape is impossible.

(A shape-based rule — match by field names and leaf kinds — was considered and rejected during
review: it would have admitted pairs like `IDigitalOutput`↔`IDigitalInput` with no sim in
between, resurrecting the invisible-bridge model this design replaces with explicit provider
faces, and its error messages degrade from "wrong type" to field-diff archaeology.)

Enforced on three rungs, matching this repo's enforcement ladder:

1. **Topology load** refuses a pairing with no type-identical direction, naming both declared
   types.
2. **`dale` CLI topology validation** applies the same predicate offline/CI.
3. **A DALE analyzer** (Phase 1 — structurally the same attribute-walking family as the existing
   DALE diagnostics) checks that `[ScenarioWire]` type arguments are codec-representable value
   structs (no delegates, STJ-serialisable members) — a consumer-build diagnostic for the trap
   §6's request/response structs would otherwise hit silently.

### 4.4 Provider faces

For a consumer contract `X`, the **provider face** `XProvider` is the inverse surface a simulator
binds. Authored by whoever owns `X`, as the same trio that already exists — interface, contract
class deriving `LogicBlockContractBase`, handler deriving `ServiceProviderHandlerBase` — and bound,
injected, discovered, and TestKit-auto-mapped by the **unchanged existing mechanisms**
(`DeclarativeContractBinder` property binding, `ContractFactory` implementation sweep, the DevHost
handler scan keyed on `[ScenarioWire]`).

SDK-shipped faces (in `Vion.Dale.Sdk.DigitalIo` / `.AnalogIo`, beside the consumer faces):

| Consumer face | Provider face | Members | Directions |
|---|---|---|---|
| `IDigitalOutput` | `IDigitalOutputProvider` | `event SetReceived(bool)`, `Confirm(bool)` | both |
| `IDigitalInput` | `IDigitalInputProvider` | `Drive(bool)` | provider→consumer only |
| `IAnalogOutput` | `IAnalogOutputProvider` | `event SetReceived(double)`, `Confirm(double)` | both |
| `IAnalogInput` | `IAnalogInputProvider` | `Drive(double)` | provider→consumer only |

Sketch (modelled line-for-line on `DigitalOutput.cs`):

```csharp
[PublicApi]
[ServiceProviderContractType("DigitalOutputProvider", Consumers = LinkMultiplicity.ZeroOrOne)]
public interface IDigitalOutputProvider
{
    event EventHandler<bool>? SetReceived;   // the paired output's command arrives here
    void Confirm(bool value);                // drives the paired output's OutputChanged
}

// contract class: Confirm → SendToContractHandler(ContractMessage<DigitalOutputChanged>);
//                 HandleContractMessage(ContractMessage<SetDigitalOutput>) → SetReceived.
// handler: [ScenarioWire(Inbound = typeof(SetDigitalOutput), Outbound = typeof(DigitalOutputChanged))]
```

The wire structs are **reused, not duplicated** — and §4.3 makes that normative:
`DigitalInputChanged` is `DigitalInputHandler`'s inbound and `DigitalInputProviderHandler`'s
outbound, so pairing a consumer face with its provider face is type-identical in every direction.
A provider face declaring lookalike copies of the structs is an authoring error the load-time
check surfaces.

Consumers author provider faces for their own contracts the same way. The PPC example
(their structs, reused):

```csharp
[ServiceProviderContractType("PowerPlantControlPvProvider")]
public interface IPowerPlantControlPvProvider
{
    event EventHandler<PpcMeasurementSet>? MeasurementReceived;
    void SetDemand(PpcDemandPvReceived demand);
}
```

Naming decision: **`…Provider`**, because the platform already calls the far side the service
provider (`ServiceProviderContractType`, `ServiceProviderHandlerBase`,
`mappedServiceProviderIdentifier`) — a sim binding the face literally provides the service; and it
is transport-neutral where `Device`/`HAL` are not (PPC). These contract-type strings are stable
introspection identifiers (docs/identifier-stability.md) — chosen once.

Provider faces declare `Consumers = LinkMultiplicity.ZeroOrOne` (a single simulator writes a
channel). All new public types follow docs/sdk-surface-conventions.md: XML docs, PublicApi
snapshot updates.

### 4.5 Decision: no host-synthesised confirmation ("Confirmation" sugar dropped)

An earlier draft declared `Confirmation = typeof(DigitalOutputChanged)` on `[ScenarioWire]`,
making the stand-in echo every captured command back as an identity confirmation. Dropped, for
three reasons:

1. **The primitive suffices, simply.** With `IDigitalOutputProvider`, the identity echo is three
   lines of sim code (`SetReceived += (_, v) => { Apply(v); Confirm(v); }`), and every non-trivial
   provider behaviour — delayed confirm, wrong confirm, no confirm (their D17/D18 sad paths) — is
   the *same three lines varied*, in ordinary, TestKit-testable block code.
2. **Modelling honesty.** In production the confirmation comes from the I/O module, not the
   device. Dropping the sugar makes the "ideal HAL" an explicit bench participant — a visible
   block in the topology and wiring view — instead of host magic behind a stale doc-comment
   (which is exactly the trap the consumer's loopback investigation walked into,
   `ServiceProviderContractHandler.cs:25-33`).
3. **No knock-on surface.** The sugar dragged in a suppression vocabulary (topologies that model
   broken hardware), a compat analyzer for the declared pair, and a special drive-gate case. All
   three disappear: a bench without an echo block *is* the suppressed bench.

Consequence: a bench that wants live `OutputChanged` includes an echoing sim block. The SmokeHost
grows a reference **ideal-I/O block** (per devhost-conventions §1 the fixture must grow with the
feature anyway), and the simulator-authoring recipe documents the ~20-line pattern for consumers.
The `ToggleLight`/`PingPong` example topologies adopt it after release, making the examples the
teaching material and their `OutputChanged` subscriptions honest at last.

Hand-driving a **wrong** confirmation needs no sim at all: with `DigitalOutputHandler`'s inbound
declared (§1 item 1), a scenario `serviceProviderSet` can drive `commanded=true, echoed=false`
into an unpaired output — a mismatch test even real hardware cannot produce on demand.

### 4.6 Scenario surface: nothing new

No new step kinds. The only change at the four step-definition sites (devhost-conventions §7) is
the drive-gate re-key: `serviceProviderSet` resolves against a **declared, codec-drivable
inbound** (a new exported annotation, the mirror of `scenarioOutputFields`) instead of
`Consumers`, completing VION-129's deferred half. `serviceProviderExpect` works on provider faces
automatically (their handlers are ordinary `[ScenarioWire]` handlers), so a scenario can assert
"the sim confirmed true" the same way it asserts any outbound.

Load-time validation warns when a scenario drives an inbound that a pairing also feeds (legal —
last write wins — but usually a bench-design smell).

### 4.7 Clocks and determinism

A pairing hop is `Capture` → one actor send → peer `Drive` → typed sends to mapped blocks: every
step is mailbox-visible, so RFC 0008's quiescence barrier fences the whole loop. Closed-loop
benches run stepped, deterministically, per-PR — and identically on the free-running clock (the
hop is clock-agnostic). Loops converge on block cadence (timers, edge-only writes), not stand-in
recursion: stand-ins never originate messages.

### 4.8 Provider faces are development surface, not production surface

A provider face exists to stand in for the real provider; running one against production MQTT
would double-publish onto live HAL topics. The boundary is declarative, not conventional:

- `[ServiceProviderContractType]` gains a flag, `DevelopmentOnly = true`, set on every provider
  face. It reaches introspection as the `developmentOnly` contract annotation, emitted only when
  true, so tooling can see it.
- The **production runtime refuses to start** a configuration containing a block whose bindings
  include a `DevelopmentOnly` contract, with an error naming the block and contract — a loud
  refusal, not the silent unmapped drop. The check lives in the private `dale` runtime
  (VION-IoT/dale#56). It is the ladder's backstop: with the pack filter below in place, no
  production configuration should ever reach it.
- Provider **handlers** must not be registered by a production host, and each carries
  `[DevelopmentOnlyHandler]` so a host can decide that from the handler type alone — there is no
  handler-to-contract-type link to follow. An earlier draft of this section said they "register no
  MQTT topics"; that is wrong in effect. Stood up, `GetMqttRegistration` returns an **empty**
  routing key, and a host matching handlers by prefix or substring has its whole routing table
  claimed by `Contains("")` — found by the dale runtime (VION-IoT/dale#56), which guarded itself
  and reported the gap. In the DevHost they are discovered by the existing `[ScenarioWire]` scan
  like any other handler.
- The **pack path filters them out of the introspection JSON**: `dotnet pack` runs
  `Vion.Dale.LogicBlockParser` with `--exclude-development-only`, so a block bound to a
  development-only contract never reaches the cloud, and the pack log names each excluded block.
  The assembly is packed unchanged (the types are inert data). `dale list` still shows such
  blocks, marked. The enforcement ladder is pack excludes → runtime refuses (a backstop expected
  never to fire); there is deliberately no cloud-side gate, because the cloud is never told these
  blocks exist.
- XML docs on every provider face state the constraint. Packaging is unchanged
  (`Vion.Dale.Sdk.DigitalIo` / `.AnalogIo`, beside the consumer faces) — the flag, not the
  package, is the boundary.
- **Cross-repo coupling:** the dale runtime reads
  `ServiceProviderContractTypeAttribute.DevelopmentOnly` **reflectively**, off the contract types
  of the loaded plugin assembly. Moving the flag off the attribute — renaming it, replacing it
  with a marker interface, deriving it from the introspection annotation instead — breaks that
  refusal silently: neither repo gets a compile error.

The TestKit is unaffected: provider faces bind and auto-map in unit tests like any contract, so
simulators stay unit-testable.

There is no third tier and no environment-aware relaxation of the boundary. Simulation against a
real gateway is a **real service provider** at the MQTT layer — a program speaking the transport —
not a logic block, so it needs nothing from this flag and no deployment ever carries a
development-only-bound block. §10.4 records that decision and the v2 question it leaves open.

## 5. Worked cases

1. **DO→DI (SG-Ready).** Their sim swaps its two `IDigitalInput` bindings for two
   `IDigitalOutputProvider` bindings; topology pairs `Sg1Output↔Sg1Provider`, `Sg2Output↔Sg2Provider`.
   The hand-mirrored `serviceProviderSet` step and its "check the tile first" label are deleted;
   non-take-up becomes honest behaviour (the sim receives the command and *ignores it* by knob);
   confirmations come from the sim's `Confirm`, so `SgContacts` and the D18 self-test are live in
   every bench; a socket-free closed-loop topology runs stepped.
2. **Utility-lock DI.** A `UtilityLockSimulator` binds `IDigitalInputProvider LockContact`, paired
   to the charging station's `LockSignal`; a service-property knob and a lock-window timer decide
   `Drive(bool)` edges. Explore-mode poking of the knob is the interactive story (RFC 0011's
   lease/cadence, answered by a block).
3. **PPC measurement/demand.** Consumer authors `IPowerPlantControlPvProvider` (structs reused,
   §4.4) + a `PpcPvProviderSimulator` — a controller model with curtailment knobs. One pairing,
   both directions type-identical. Existing `serviceProviderSet`-driven PPC scenarios keep working
   unchanged on unpaired topologies.
4. **Modbus RTU (Phase 2, §6).** Request/response: the stand-in holds the callback, the provider
   face answers from a register-map subsystem.

## 6. Phase 2 — request/response pairing (Modbus RTU, future M-Bus)

The RTU wire structs carry a `CorrelationId` and a **callback delegate**
(`ActorMessages.cs:22-31`) — not JSON-representable, and semantically a pending operation with
timeout rules, not a fire-and-forget value. RFC 0010 §4 deferred this family ("needs a
response-fixture vocabulary"). The pairing concept extends; the mechanism differs:

- A request/response-aware stand-in **captures the request, holds the callback pending** (exactly
  as the production `ModbusRtuHandler` does, `ModbusRtuHandler.cs:187-227`), and forwards only
  the payload across the pairing: `ReadRequested(correlationId, functionCode, unitId, address, quantity)`.
- The provider face is SDK-owned: `IModbusRtuProvider` — `event ReadRequested` /
  `WriteRequested`, `RespondRead(correlationId, byte[])`, `RespondWrite(correlationId)`,
  `RespondError(correlationId, code)`.
- On `Respond*` the stand-in completes the original callback with a receipt; expiry runs on the
  **virtual clock**, so `advance 6s` deterministically produces a `Timeout` outcome — timeout and
  device-error paths become steppable scenario material that today only unit tests reach.
- A sim block binds `IModbusRtuProvider` and answers from a register map — the consumer's existing
  transport-agnostic subsystems (`IModbusRegisterBufferHost`) port unchanged from socket-hosted to
  contract-hosted, and RTU device benches join the stepped per-PR gate.
- Declaration surface: the request/response analog of `[ScenarioWire]` (request + response types
  per operation), design detailed when Phase 2 starts.
- Worth knowing: today an RTU-bound block in the DevHost gets **no response ever** — no stand-in
  exists (`ModbusRtuHandler` carries no `[ScenarioWire]`), and the timeout sweep lives in the
  absent production handler — so Phase 2 also fixes a silent dead end.

M-Bus later is the same recipe: whoever ships the contract ships its provider face.

Out of scope for both phases: Modbus TCP and HTTP (direct-DI clients off the contract plane — the
socket sims serve them well, wall-clock stays their declared constraint) and logic-block interface
contracts (they already loop natively).

## 7. Changes by component (Phase 1)

| Component | Change |
|---|---|
| `Vion.Dale.Sdk.DigitalIo` / `.AnalogIo` | `Inbound = typeof(*OutputChanged)` on the two output handlers; four provider-face trios; TestKit extensions (`RaiseSetReceived`, `VerifyDriven` etc.); PublicApi snapshots |
| `Vion.Dale.Sdk` / `Vion.Dale.Sdk.Generators` | `DevelopmentOnly` flag on `[ServiceProviderContractType]` + its introspection annotation; the DALE analyzer for codec-representable `[ScenarioWire]` types (§4.3) |
| `dale` runtime (private, cross-repo) | refuse production configurations containing development-only contracts (§4.8); verify handler discovery excludes provider handlers |
| `Vion.Dale.DevHost` | pairing table (topology loader + `DevConfigurationBuilder.PairContracts`), `Capture` forward, structural-compat predicate, drive-gate re-key on declared inbound + exported annotation, reworded no-echo pin, **fix the stale `ServiceProviderContractHandler` class doc** (`:25-33`) |
| `Vion.Dale.DevHost` topology schema | `contractPairings` array; regenerated per-project schemas |
| `Vion.Dale.Cli` | topology validation of pairings; scenario checks follow the drive-gate re-key; `dale list` marks development-only blocks and `dale upload` repeats the pack notice |
| `Vion.Dale.LogicBlockParser` + `Vion.Dale.Sdk.targets` | `--exclude-development-only`, passed on the pack path: development-only blocks are dropped from the emitted JSON and named in the pack log (§4.8) |
| `Vion.Dale.DevHost.Web` | wiring view renders pairings; topology editor edits them; scenario forms unchanged |
| `Vion.Dale.DevHost.SmokeHost` | ideal-I/O sim block (provider faces), a paired topology, scenarios proving: delivery, confirmation loop, DI drive, mismatch drive, stepped determinism; `devhost-smoke` coverage |
| Docs | devhost-conventions section on pairing + a simulator-authoring recipe; identifier-stability note for the new contract-type names |
| Adjacent cleanup | `DevHostControl.PublishAllStates` still hardcodes the four HAL handler names (`DevHostControl.cs:381-389`) — provider handlers make this stale fragment visible; fold its fix in |

## 8. Jira mapping

- **VION-131** — becomes the carrying item for Phase 1. Comment now, linking this RFC; closed by
  the implementing release with the usual `/fix` closing comment (which the maintainer relays to
  the consumer). Its "Done when" list is satisfied by §7: confirmations deliverable and drivable,
  stand-in doc fixed, `devhost-smoke` coverage.
- **VION-129 deferred half** — completed by Phase 1 (drive-gate re-key + exported annotation);
  VION-129 itself stays closed, no comment needed.
- **VION-133** — stays open; scope sharpens: pairing deliberately avoids endpoint-triple
  semantics, and the consumer's "`ShareContract` is the natural seam" framing is superseded —
  under VION-133, `ShareContract` can now simply be deleted. Worth a comment saying so.
- **VION-130 / VION-132** — orthogonal; untouched; no comment needed.
- **Phase 2** — gets its own item when picked up; it is not consumer-filed feedback, it
  originates here.
- **RFC 0011 (parked)** — paired benches answer its interactive-drive need with sim-block knobs;
  the unpaired interactive-drive question stays parked.

## 9. Consumer follow-up (logic-block-libraries)

Their work, in their repo, once Phase 1 releases — relayed through the usual feedback flow:

1. **SG-Ready bench**: swap the sim's two `IDigitalInput` bindings for `IDigitalOutputProvider`,
   pair the four endpoints, delete the hand-mirrored `serviceProviderSet` step and its
   check-the-tile label, model non-take-up as sim behaviour, and let `Confirm` light up
   `SgContacts` and the D18 self-test off-production.
2. **PPC simulators — newly possible**: author provider-face trios for their PPC contracts
   (reusing their existing wire structs, per §4.3's identity rule), then write
   `Ppc*ProviderSimulator` blocks — behaving peers with controller knobs, drivable interactively
   in Explore and deterministically in stepped scenarios. Until now the only off-production PPC
   surface was scenario drives.
3. **Ideal-I/O adoption**: copy the SmokeHost echo-block recipe wherever a bench wants
   ideal-hardware confirmations without a device sim.
4. **Housekeeping enabled, their call**: the `LastPublishedMeasurement*` substitute service
   properties (kept after VION-71) and their AGENTS.md "no DO→DI bridge" gotchas can retire as
   benches migrate.

## 10. Decisions from review

### 2026-08-28

1. **Provider faces are development-only, declaratively** (was open: production semantics) —
   §4.8. Same package; the `DevelopmentOnly` flag, XML docs, handler exclusion and the production
   runtime's refusal are the mechanism. Settled since: the flag shipped as `DevelopmentOnly`, and
   the private runtime's handler discovery was verified cross-repo (VION-IoT/dale#56).
2. **The analyzer ships with Phase 1** (was open: analyzer scope) — it is structurally the same
   attribute-walking family as the existing DALE diagnostics; §4.3 rung 3, §7.
3. **Pairing rule tightened from shape-compatibility to wire-type identity** — §4.3. Provider
   faces are the pattern; a shape-based bridge (e.g. `IDigitalOutput`↔`IDigitalInput` directly)
   would resurrect the invisible-bridge model this RFC replaces.

### 2026-08-31

4. **Two simulation tiers, and the v1 line between them is hard** (was open: whether a simulator
   could ever be deployed). There are exactly two ways to stand in for a provider, and they are
   different mechanisms, not two settings of one:

   - **Logic-level, in the DevHost, via pairing** — a simulator block binds a provider face and the
     host re-delivers along a declared wire (this RFC). Deterministic and steppable, and it
     **bypasses the MQTT hop and the binary codec entirely**: the pairing forwards the value at the
     JSON layer the scenario codec already speaks (§4.1).
   - **Transport-level, against a real gateway** — the peer is a **real service provider on MQTT**:
     it publishes and subscribes the actual topics with the actual encodings. It is a program, not
     a logic block, and it exercises exactly what the first tier skips.

   So a "deployed simulator" is not a relaxation of §4.8 — it is the second tier, and it needs no
   development-only contract at all. That is why v1 draws a hard line and the enforcement ladder
   has no escape hatch: pack excludes, runtime refuses.

   **Open for v2:** unifying service-provider and logic-block contracts. If a service provider and
   a logic block could be the same kind of peer, "simulator as a deployable participant" becomes a
   real question again — with the fidelity trade-off above as its first problem. Not now.
