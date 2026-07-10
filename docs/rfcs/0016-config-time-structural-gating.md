# RFC 0016 — Configuration-time structural gating of logic-block members (DF-38)

- **Status:** Proposed — 2026-07-09
- **Author:** jonas.bertsch
- **Related:** RFC 0013 (DevHost topology authoring — the wiring/multiplicity contract), RFC 0008 (unified scenario+topology; recycle-on-run), RFC 0004 (emission policy — per-member retained streams), RFC 0010 (service-provider contracts). Sibling: **RFC 0017 (presentation-time member visibility)** — shares the predicate-expression foundation defined here in §7. Cross-repo: `vion-contracts` (introspection model), `cloud-api` (definition storage, activation, multiplicity validation, active read-models), `dashboard` (Logic Editor), `dale` (runtime `LogicSystemConfigurationInitializer` / `ServicePropertyHandler`), `architecture` (`concepts/logic-block-wiring.md`, `decisions/0020-seal-imperative-logicblock-configuration.md`, `flows/logic-configuration-deployment.md`). Origin: **DF-38** in `logic-block-libraries/docs/dale-preview-feedback.md` + the charging-station brainstorm (`.../notes/2026-07-08-charging-station-concept-brainstorm.md`).

> This is a design contract, not an implementation. It is the document an implementation plan (or an `architecture` spec) is generated from. It deliberately stops at the seams (exact attribute syntax, field names, DALE#### numbers, route bodies, store-action names) so the per-component spec can fill them in against current `main` of each repo. Where a mechanism is under-examined, it is flagged in §11 rather than hand-waved.

## 1. Summary

A physical device can expose a **configuration-determined number** of otherwise-identical sub-units — the motivating case is a charging station whose plug count (1–6, model-dependent) decides how many independent `IControllableConsumer` interface **and service** instances it presents. Today the exposed member set of a logic block is **frozen at pack time** by reflection over the compiled type (`DeclarativeInterfaceBinder` / `DeclarativeServiceBinder` iterate declared properties once); the count an operator picks is unknowable at that moment. The two shapes available today are both poor: *N one-plug blocks* (shared station state loses its owner — the legacy TwinCAT "fallback mismatch" hack) or *one block declaring the maximum with dead editor slots*.

This RFC moves the member-set **resolution point from pack time to configuration time**, without making it runtime-dynamic and **without touching the wiring/routing layer**. The mechanism is a single new operator:

- The developer declares a **static maximum** set of members the ordinary way (N named properties, exactly as `ChargingStationMultiPointSimulation` declares `ChargingPoint1`/`ChargingPoint2` today — no new instantiation machinery, the `new()`s and callbacks stay in the block).
- Each such member (or member-group) carries an **`[ExistsWhen(<predicate>)]`** gate whose predicate references one or more **`[StructuralConfig]`** scalar inputs (e.g. `Model`, `PlugCount`).
- `[StructuralConfig]` values are chosen at **configuration time** (in the Logic Editor), stored on the instance, and applied to the block **before binding**. The gate is evaluated against them, and a gated-out member **does not exist anywhere downstream** — filtered introspection, no actor wiring, no MQTT topic, no cloud read-model row, not in the editor. As if it were never declared.

Because the maximum is static, member **identities are stable** (`Point3_IControllableConsumer` is always that plug), which removes the two hardest problems a config-sized *collection* would create — indexed-identity minting and reconcile-on-shrink of a vanishing identity. The cost is an **arbitrary compile-time maximum** (acceptable for the EVTEC fleet — ceiling 6, typically 1–3; **not** sufficient for the truly runtime-dynamic OCPP case, which is explicitly out of scope, §10).

The load-bearing runtime change is a **lifecycle ordering** one: structural inputs must reach the block **before `Configure`** so the gate resolves at bind time. Everything the runtime must suppress for a hidden member (initial state-updates, property-set acceptance, routing, topics) then falls out *by construction*, because the member is simply never activated.

## 2. Motivation

From DF-38 and the charging-station brainstorm, grounded against the real corpus:

- **A "charge point" is not just an interface — it is a whole component.** In `ControllableConsumerContract` and the existing `ChargingStationMultiPointSimulation`, each `ChargingPoint` bundles the interface (`IControllableConsumer`: `HandleRequest`/`HandleCommand`), a full **service** (the `ConfigStateUpdate` surface — `OperatingMode, Priority, ResponseTime, IdleTime, PhaseType, PhaseOrder, StepConfig, Min/MaxCurrent` — plus operator settings and live **measuring points** `ActivePowerConsuming`, `EnergyConsumedTotal` (`[Persistent]` + `TotalIncreasing`), `RequestedCurrent`, SoC), and a station-level **contract** per plug (`IDigitalOutput`). So "config-determined *interface* count" undersells it: the repeatable unit is a **component** = interface + service + properties + measuring-points + persistent-state (+ its station contract).
- **Making only the interface count dynamic while services stay static is incoherent.** The EM polls/commands each plug over its interface *and* reads its per-plug config/telemetry over its service; a plug that is wireable but has no service (or vice versa) is meaningless.
- **The count is a configuration-time fact, static thereafter.** The operator picks the model / plug count at commissioning; it does not change while the station runs. This matches the platform's entire config-time pipeline (draft → activate → snapshot → recycle) and specifically does **not** need the runtime-dynamic machinery the truly-dynamic OCPP case would (see §10 and DF-38's own "OCPP angle" note).
- **The surface is frozen one notch too early.** Interface/service discovery is pure reflection over the compiled type (`DeclarativeInterfaceBinder.BindPropertyBasedInterfaces`, `DeclarativeServiceBinder`), captured at pack time by `LogicBlockIntrospection` into the `vion-contracts` `LogicBlockIntrospectionResult`. Nothing in that path consults configuration. DF-38 is exactly "let a small, declared set of config-time inputs shape that surface."

The reframing that scopes this RFC (and rules out the expensive alternatives): a block's exposed surface splits into **which members exist** (this RFC) and **how they are wired** (unchanged — wiring is *already* a config-time concept, resolved once at Dale's `LogicSystemConfigurationInitializer.InitializeAsync` and immutable at runtime; see `architecture/concepts/logic-block-wiring.md`). DF-38 only needs the first to move pack→config; the second stays put.

## 3. The decision (and the alternatives rejected)

**Chosen — gate a developer-declared static-maximum member set with `[ExistsWhen]` over `[StructuralConfig]` scalar inputs, resolved at bind time, so a gated-out member is absent across every layer. No collection binding, no count-materialization machinery, no runtime dynamism, no wiring/routing change.**

Each axis, with the alternative rejected:

- **Existence gate over a static max, not config-sized collection materialization (REPEAT).** *Rejected:* an `IReadOnlyList<ChargingPoint>` collection binding whose length is a config value, with the binder minting N indexed instances (`chargingPoint[i]`). It is the "more correct" model in the abstract but pays for capabilities we do not need: a new indexed-identifier scheme (`InterfaceMapping.InterfaceIdentifier` is an un-indexed string today), **reconcile-on-shrink** of a genuinely vanishing identity (orphaned mappings/persistent state), and **new instantiation machinery** taking the `new()`s and per-instance callback wiring out of the developer's hands — which the consumer explicitly did not want. Gating a static max keeps identities stable (a plug is shown or hidden, never re-indexed), keeps the developer's `new()`/callback code exactly as it is today, and makes the cloud/editor work a *filter of a known set* rather than a *templated expansion*. REPEAT is parked (§10) for the unbounded / runtime-discovered case; if it ever lands it is a superset of this and reuses the same `[StructuralConfig]` and predicate foundation.
- **Config-time-static, not runtime-dynamic.** *Rejected:* making member count (or mappings) change while the block runs. That ripples through six surfaces (introspection, cloud persistence, editor, Dale routing table, the MQTT topic tree, retained-state streams) and collapses the clean draft-vs-active model (the runtime would drift from `IsCurrent`). The motivating fleet's count is known at config time; a change is a normal reconfigure→redeploy (recycle). Contradicts nothing in `decisions/0020-seal-imperative-logicblock-configuration.md`.
- **Gate = hard existence (all layers), not a display hint.** *Rejected:* hiding gated members only in the editor. That leaves the cloud materializing every max-N service → dead retained topics and read-model rows; the "dead slots" move from the editor to the topic tree. The gate must be a config-time *resolution* that filters introspection everywhere. (The *soft*, UI-only, runtime-reactive hide is a genuinely different operator — **RFC 0017**.)
- **`[StructuralConfig]` = scalar comparables only.** *Rejected:* letting arbitrary property types (arrays, structs) drive gates. A gate predicate over "element 3 of a struct array exists" needs a far richer language and a three-runtime evaluator for no current need. The plug-count driver is a scalar (`enum Model` / `int PlugCount`); per-plug data is *conforming runtime data*, not a structural driver (§9).
- **Resolve at bind time in the block, not post-hoc pruning by the host.** *Rejected (leaning):* Dale binding the full max set then pruning. Evaluating the gate where the block is configured keeps a single authority (the block knows its `[StructuralConfig]`) and makes "hidden = never activated" true by construction rather than by a cleanup pass. (This is the main open design seam — see §8.5 and §11 R2.)

## 4. Concepts and vocabulary

- **Structural input** — a `[StructuralConfig]` scalar property whose value is chosen at configuration time, stored on the logic-block instance, and applied to the block **before `Configure`/binding**. Immutable at runtime (changing it = reconfigure + redeploy). It is the *only* thing a structural gate may reference. Distinct from an ordinary `[ServiceProperty]`, whose value arrives post-deploy over the runtime set-path.
- **Structural gate** — an `[ExistsWhen(<predicate>)]` on a member (interface binding, contract binding, service, or a nested component that carries all three). When the predicate is false for an instance's structural inputs, the member **does not exist** for that instance.
- **Definition view vs live view** — the two introspection projections this RFC creates:
  - **Definition view** (pack-time, stored in `cloud-api` `LogicBlockDefinitionEntity`): the **full maximum** member set **plus** the gate predicates **plus** the `[StructuralConfig]` descriptors. It is configuration-independent (it must be — the pack-time parser runs a default instance) and is what the editor evaluates *per instance* to compute the resolved surface.
  - **Live view** (config-apply time — Dale binders, MQTT topic tree, cloud *active* read-models): the **resolved/filtered** set for one instance's structural inputs. This is what actually runs.
- **Predicate** — a small boolean expression over structural inputs (§7). Shared, as a grammar only, with RFC 0017 (which allows a wider reference scope and evaluates differently).

## 5. Authoring surface (SDK)

The developer writes the maximum case the ordinary way and annotates it. Illustrative (exact attribute names/shape are a seam for the SDK spec):

```csharp
public class ChargingStationEvtec : LogicBlockBase
{
    // config-time structural input: operator picks the model; the block owns Model → PlugCount.
    [ServiceProperty(Title = "Modell")]
    [StructuralConfig]                                  // scalar, config-time, applied pre-bind, immutable at runtime
    public EvtecModel Model { get; set; }

    // derived structural scalar (see §8.6 for the "derived input" open question)
    [StructuralConfig(DerivedFrom = nameof(Model))]     // e.g. crema=2, cappuccino2in1=2, cappuccino3in1=3, bricco=1
    public int PlugCount => Model.PlugCount();

    [LogicBlockInterfaceBinding(typeof(IControllableConsumer), Multiplicity = LinkMultiplicity.ZeroOrMore)]
    [ExistsWhen("PlugCount >= 1")] public ChargingPoint Point1 { get; }

    [LogicBlockInterfaceBinding(typeof(IControllableConsumer), Multiplicity = LinkMultiplicity.ZeroOrMore)]
    [ExistsWhen("PlugCount >= 2")] public ChargingPoint Point2 { get; }

    [LogicBlockInterfaceBinding(typeof(IControllableConsumer), Multiplicity = LinkMultiplicity.ZeroOrMore)]
    [ExistsWhen("PlugCount >= 3")] public ChargingPoint Point3 { get; }

    public ChargingStationEvtec(TimeProvider tp, ILogger log) : base(log)
    {
        Point1 = new ChargingPoint(tp, log);            // developer's own new(), exactly as today
        Point2 = new ChargingPoint(tp, log);
        Point3 = new ChargingPoint(tp, log);
    }
    // timer / SetExternallyLocked fan-out / output.Set(): the developer loops over the plugs they choose
    // to drive, gating on PlugCount in their own code. No inversion of control.
}
```

Notes:

- `ChargingPoint` (the nested component carrying the interface + its `[ServiceProperty]`/`[ServiceMeasuringPoint]` surface + callbacks) is **unchanged** from the example that ships today. The gate rides on the **binding**, not the component.
- `[ExistsWhen]` targets the same declaration sites `[LogicBlockInterfaceBinding]` / `[ServiceProviderContractBinding]` / service-bearing properties already target, so it composes with the existing `DeclarativeInterfaceBinder` / `DeclarativeContractBinder` / `DeclarativeServiceBinder`. A member with no `[ExistsWhen]` is unconditional — **fully backward compatible** with every block that ships today.
- **A gate on the component gates the whole bundle.** `[ExistsWhen]` on `Point3` must remove *its* interface, *its* service, *its* measuring points, and *its* station contract (`ChargingPoint3Output`) together. The unit of gating is the member/component, and the spec must ensure the three binders resolve the same gate consistently (see §11 R4).

## 6. Multiplicity interaction

`LinkMultiplicity` (RFC 0013 / `LinkMultiplicity.cs`) is orthogonal and unchanged: it says how many *wires* attach to one binding, not whether the binding exists. But the two interact at validation time: **a gated-out `ExactlyOne`/`OneOrMore` member must not be flagged "required but unwired"**, and a gated-out provider member must not count toward a `Consumers` cap. Every multiplicity check (in `cloud-api` `LogicConfigurationMultiplicityValidator` and in the DevHost `AutoConnect`/authoring guides) must **resolve gates first, then count** — i.e. run against the *live view*, never the *definition view*. Charge-point interfaces stay `ZeroOrMore` (the DF-19 fan-in rule: both the EM and a steering source may wire them).

## 7. The predicate-expression language (shared foundation)

One grammar, defined here in sketch and **concretized by RFC 0017's cross-repo spec** (which widens the reference scope and evaluates it differently). The **canonical grammar + semantics now live in vion-contracts [`docs/predicates.md`](https://github.com/VION-IoT/vion-contracts/blob/main/docs/predicates.md)**, pinned by `Predicates/predicate-conformance.json`; this section is superseded by that document and kept only for context. Kept deliberately small so it is trivially and identically implementable in C# (SDK + Dale + cloud-api) and TypeScript (dashboard):

```
predicate   := orExpr
orExpr      := andExpr ( "||" andExpr )*
andExpr     := unaryExpr ( "&&" unaryExpr )*
unaryExpr   := "!" negand | "(" predicate ")" | comparison | membership | boolRef
negand      := boolRef | "(" predicate ")"     // NOT a bare comparison: "!A == 5" is rejected
comparison  := ref ( "==" | "!=" | "<" | "<=" | ">" | ">=" ) literal
membership  := ref "in" "[" literal ( "," literal )* "]"
ref         := identifier | identifier "." identifier   // Property | Service.Property
boolRef     := ref                                       // must type-check to bool
literal     := integer | "true" | "false" | string      // strings quoted; enum members are quoted
```

> **Concretization deviations from the original sketch above** (adopted 2026-07-10; see the spec's Drift
> checkpoints). The sketch's `literal := number | boolean | string | enumMember` became: **enum members are
> quoted strings** (`Mode == 'Eco'`) — the unquoted `enumMember` literal is gone, because an unquoted RHS
> identifier is indistinguishable from a property reference under JS/jsep evaluation. Also: **bare bool refs**
> are a predicate (`unaryExpr := … | boolRef`); refs may be **two segments** (`Service.Property`), not just a
> bare identifier; **negation is restricted** to a bool ref or a parenthesized predicate (`!A == 5` is a parse
> error — parenthesize as `!(A == 5)`); numeric literals are **integers only, int32-range** (no floats — analog
> values flap); strings accept both quote styles but **single quotes are the documented style** (no escaping in
> a C# attribute). For RFC 0016 the `ref` still MUST resolve to a `[StructuralConfig]` scalar on the same block.

Constraints and obligations:

- **Reference scope (this RFC):** `propertyRef` must resolve to a `[StructuralConfig]` scalar on the same block. An analyzer enforces this (§8.1). (RFC 0017 relaxes this to any service property but forbids `&&`/`||` reaching across the structural/runtime boundary — see that RFC.)
- **Type discipline:** comparisons and membership are type-checked against the referenced scalar (`enum`/`int`/`bool`/`string`). No arithmetic, no functions, no nesting into struct/array members.
- **Two implementations, one behavior.** The evaluator exists in C# (owned by the SDK, referenced by Dale; re-used or re-implemented by cloud-api) and TypeScript (dashboard). To prevent silent drift — the exact failure mode `LogicBlockWiringConventionsShould` guards for the multiplicity vocabulary — ship a **shared conformance vector**: a checked-in JSON list of `{ predicate, inputs, expected }` that both implementations run in CI. This vector is a cross-repo artifact; its home (vion-contracts vs a new shared fixture) is a seam for the spec.
- **Serialization:** the predicate travels in the introspection model as a string in this grammar (parsed by each consumer) **or** as a small pre-parsed AST. String-with-shared-parser is simpler and matches how the template layer already carries `visibleWhen`; AST avoids re-parsing. Decision deferred to the vion-contracts spec (§11 R6).

## 8. Cross-repo impact

Ordered by dependency. Each bullet names the concrete component the per-repo spec must open; **bold** marks the load-bearing change in that repo.

### 8.1 dale-sdk

- `Vion.Dale.Sdk/Core/` — new `[StructuralConfig]` and `[ExistsWhen]` attributes. `[StructuralConfig]` is a `[ServiceProperty]` flavor (or a sibling) marked scalar / config-time / pre-bind / runtime-immutable; the `DerivedFrom` option (§8.6) is optional.
- `Vion.Dale.Sdk/Configuration/**` — **the binders (`DeclarativeInterfaceBinder`, `DeclarativeContractBinder`, `DeclarativeServiceBinder`) must resolve `[ExistsWhen]` and, in live mode, skip gated-out members.** This is where "hidden = never minted" is realized. See §8.5 for the two-mode subtlety.
- `Vion.Dale.Sdk/Core/LogicBlockBase.cs` — (a) apply `[StructuralConfig]` values *before* `Configure` (line ~480 today); (b) emit initial state-updates only for the live (resolved-active) member set; (c) expose the resolved-active set so the runtime property handler can reject sets to gated-out members.
- `Vion.Dale.Sdk/Introspection/LogicBlockIntrospection.cs` — **emit the definition view: the full max set + each member's `[ExistsWhen]` predicate + the `[StructuralConfig]` descriptors** (identifier, scalar type, enum members, optional `DerivedFrom` map). Must emit *unresolved* (it runs a default instance and has no operator config).
- `Vion.Dale.Sdk/Configuration/Interfaces/FunctionInterfaceMetaData.cs` (+ service/contract equivalents) — carry the predicate into the emitted `Annotations`.
- `Vion.Dale.Sdk.Generators/` — new analyzers (DALE#### to be assigned): (i) `[ExistsWhen]` predicate references only `[StructuralConfig]` scalars declared on the block; (ii) `[StructuralConfig]` type is an allowed scalar; (iii) predicate parses and type-checks; (iv) *optionally* the per-plug conforming array's declared bound relates to a structural input (§9). Consistent with today's "declared, not enforced" posture: analyzers guide; cloud-api enforces.
- `Vion.Dale.LogicBlockParser/Program.cs` — unchanged in shape, but its output now carries the definition view. Confirm the default-instance introspection path emits predicates without needing a config value (it should — predicates are static metadata).

### 8.2 vion-contracts

- `Vion.Contracts/Introspection/LogicBlockIntrospectionResult.cs` — **additive** optional fields: a gate predicate on `InterfaceInfo` / `ContractInfo` / `ServiceInfo`, and a new `StructuralConfig` descriptor list on `LogicBlockIntrospectionResult` (identifier, scalar type, enum members, optional derivation map). Add-fields-only per the repo's compat posture; no reorder, no FlatBuffers involved (introspection is JSON).
- `Vion.Contracts/Events/CloudToMesh/SetLogicConfigurationPayload.cs` — **`LogicBlockInstance` must carry the instance's `[StructuralConfig]` values** so Dale can resolve gates at config-apply. This is the wire-format delta. Interface identifiers stay bare strings (no index needed — identities are static), so `InterfaceMapping` is unchanged.
- `Vion.Contracts/Conventions/LogicBlockWiringConventions.cs` — annotation key(s) for the predicate + structural-config descriptor; a change-detector test pins them.
- The predicate **conformance vector** (§7) — candidate home.

### 8.3 cloud-api

- `Cloud.Api/IntegratorApis/LogicBlockLibraries/Entities/LogicBlockDefinitionEntity.cs` — stores the **definition view** (full set + predicates + structural descriptors). Note the existing `HasOrderedJsonConversion` (`json`, not `jsonb`) on `Services` (struct field-order) — the new predicate/descriptor fields must land where key order is not load-bearing or inherit the ordered treatment.
- `Cloud.Api/TenantApis/LogicConfigurations/Dtos/LogicConfigurationData.cs` + `Entities/LogicConfigurationEntity.cs` — **`LogicBlockInstance` must persist the operator-chosen `[StructuralConfig]` values.** Today a stored instance carries only `Id`, `EdgeGatewayId`, `LogicBlockDefinitionId`, `Name`, `Services[]` — *no* property values (those are set live post-deploy). Structural inputs are the first config-time-stored values on an instance; this is a real, if contained, addition.
- `Cloud.Api/TenantApis/Services/EventHandlers/ActiveLogicConfigurationDataReadModelUpdater.cs` — **project the live view**: when materializing `ActiveService*`/interface read-models for an instance, evaluate each member's gate against that instance's structural inputs and emit only the resolved-active set. This is the "spill" the exploration flagged: the projection iterates `definition.Services` today; it must now filter.
- `Cloud.Api/TenantApis/LogicConfigurations/Helpers/LogicConfigurationMultiplicityValidator.cs` — resolve gates before counting (§6); a gated-out member is absent for multiplicity purposes.
- `Cloud.Api/TenantApis/LogicConfigurations/RequestHandlers/ActivateLogicConfigurationRequestHandler.cs` — `CreateEventsToMeshAsync` ships the structural values (via the updated `SetLogicConfigurationPayload.LogicBlockInstance`) and only builds mappings/topics for resolved-active members (defensively — the editor should already have filtered).
- A C# **predicate evaluator** (shared with, or re-implemented against the same conformance vector as, the SDK/Dale one).

### 8.4 dashboard

- `src/domain/apis/logicBlockDefinition/models.ts` — carry the gate predicate + structural descriptors on the definition models.
- `src/pages/tenant/logicConfiguration/editor/composables/editorNodeBuilders.ts` (`buildBlockNode`), `useInterfaceMatching.ts`, and the store getters that iterate `definition.interfaces`/`services` — **resolve gates against the instance's structural inputs and render only the resolved-active slots** (labelled, e.g. "Ladepunkt 1"). This is the DF-38 editor win: exactly N wireable slots, no dead ones.
- `src/pages/tenant/logicConfiguration/editor/AddBlockDialog.vue` — **new: a configuration-time form for `[StructuralConfig]` inputs.** Today this dialog edits only `name` + `edgeGateway`; structural inputs are the first block-config values it must edit and persist. Reuse the schema-driven value editors under `src/components/ui/values/`.
- `src/pages/tenant/logicConfiguration/store.ts` — persist structural inputs on the instance; **reconcile wiring when a structural input changes** (see §11 R3 — this is the analogue of the existing `upgradeDraftLogicBlocks` gap, which rebuilds `services` but orphans mappings).
- `src/domain/apis/logicBlockDefinition/multiplicity.ts` — treat gated-out members as absent (§6).
- A TypeScript **predicate evaluator** conforming to the §7 vector.

### 8.5 dale (runtime)

- `Dale/Configuration/LogicSystem/LogicSystemConfigurationInitializer.cs` — **apply each instance's `[StructuralConfig]` values to the block before it is configured/bound**, then link only the resolved-active interfaces. This is the ordering change everything hinges on. `ProcessInterfaceMappings` / `SetLinkedInterfaces` then naturally see only active interfaces.
- `Dale/Mqtt/Handlers/ServicePropertyHandler.cs` — because a gated-out service is never bound, a set to it is an **unknown-service rejection** for free; verify no path assumes the full definition set.
- `Dale/Mqtt/Handlers/LogicSystemConfigurationHandler.cs` — receive and route the structural values from the payload.
- **Retained state & topics** — a gated-out member publishes/subscribes nothing (it does not exist). Persistent members (`[Persistent]`, e.g. `EnergyConsumedTotal`) that are *currently gated out* must have their retained state left **dormant, not garbage-collected**, so re-showing the plug (a reconfigure that raises the count) restores history (§11 R5).

### 8.6 The Model → count derivation (open shape)

The operator picks a human-readable **Model**; several models map to the same count. Two ways to feed the gates:

1. **Gates reference `Model` directly** — `[ExistsWhen("Model in [Cappuccino3in1, Ristretto2, ...]")]`. No derived scalar, but each gate carries a model set and the editor needs the full model→member knowledge inline.
2. **A derived structural scalar `PlugCount`** whose Model→count map is emitted in the definition view; gates reference `PlugCount`. Cleaner gates; needs the introspection model to express a *derived* structural input (a static map from another structural input).

Recommendation leans (2) for readability, but it introduces "derived structural input" as a concept the three evaluators must all understand. Settle in the vion-contracts + SDK spec (§11 R6).

## 9. Per-plug conforming data (the array-length concern)

Per-plug data splits by whether it is independently observed or wired:

- **Independently wired / charted / retained / persisted** (the EM polls/commands it; the dashboard charts it; MQTT retains it): this **must** be N distinct service+interface instances — i.e. it lives *inside* the gated component (`ChargingPoint`), and scales with the gate. It cannot be a struct-array element (you cannot wire `plugs[2]`, cannot give `plugs[2].EnergyConsumedTotal` its own retained `TotalIncreasing` stream, cannot persist it by stable identity). `Mode`/`Priority`/`PhaseOrder` ride the plug's `ConfigStateUpdate` → per-plug **service** state.
- **Inert per-plug setup**, read once at `Starting()` and never streamed/wired/charted (e.g. a Modbus register offset), *may* be a station-level `ImmutableArray<PlugSetup>` whose **length must equal the resolved plug count**.

Length validation for that inert array is inherently **runtime-only**: the array is an ordinary `[ServiceProperty]` value set post-deploy, so cloud-api cannot check it at activate (it has no value then). The floor is the block asserting `Plugs.Length == PlugCount` in `Starting()` and reporting a config error / unhealthy component (the consumer already accepted "worst case in logic-block code"). An analyzer can at most check a *declared* relationship, not the runtime value. **Open:** whether this inert setup is better *derived from `Model`* (removing the array and the mismatch class entirely) than carried as a separately-set array (§11 R7).

## 10. Out of scope (explicit non-goals)

- **Runtime-dynamic member count.** Members appearing/vanishing while a block runs (the OCPP case: EVSEs discovered at `BootNotification`, changeable live). This is the strictly harder problem DF-38 itself defers; it would need a mutable routing table, live topic/retained-stream reconciliation, and would collapse draft-vs-active. If ever built, it is a superset that reuses `[StructuralConfig]`/predicates but adds a runtime-signal source.
- **Config-sized collection materialization (REPEAT).** Deferred with runtime-dynamic; a static max + gate covers the bounded fleet.
- **Presentation-time visibility** (soft, UI-only, runtime-reactive — the `DirectMeasurement` class). That is **RFC 0017**, sharing only the §7 grammar.
- **Unbounded maxima.** The static-max approach assumes a small, source-declared ceiling (fleet: 6). A device family with a large or unknown ceiling wants REPEAT, not this.

## 11. Risks and under-examined areas

Flagged for the per-component specs; several were not deeply traced in the design session.

- **R1 — Config-time property storage is net-new.** No operator-set property value is stored on an instance today (only `name`/`gateway`; all values are runtime). `[StructuralConfig]` is the first. The `LogicBlockInstance` DTO/entity, the editor form, and the activation payload all gain a config-time value channel. Contained, but touches four repos.
- **R2 — Binder dual-mode (the sharpest seam).** The same binder path serves *pack-time introspection* (must emit the **full** set + predicates, unfiltered — it has no config) and *config-apply binding* (must emit the **filtered** live set). Options: (a) a mode flag on the binders; (b) binders always produce the full annotated set and a separate resolution step filters (introspection emits all; Dale/block resolves post-bind). (b) risks "hidden = minted then pruned," weakening the "never activated" guarantee for state-updates / handler rejection unless the block itself consults its resolved set. **This is the single most important thing the SDK + Dale spec must nail.**
- **R3 — Edit-time reconcile on structural change.** Lowering a count (3→2) at edit time invalidates wiring to the now-gated member. Because identity is stable the mapping is *dormant*, not deleted — but the editor must surface it (validation) and activation must reject/prune it. Today's `upgradeDraftLogicBlocks` demonstrates the exact gap (rebuilds services, orphans mappings); this RFC must not inherit it. No reconcile logic was designed in this session.
- **R4 — Whole-component gating consistency.** A gate on a component must remove its interface *and* service *and* contract *and* measuring-points together, across three independent binders + the cloud read-model projection + the editor. Divergence (interface gated but service not) is a real failure mode. Needs a single resolution authority per instance.
- **R5 — Dormant persistent state.** `[Persistent]` members inside a gated-out component (`EnergyConsumedTotal`) must not have retained state garbage-collected while hidden, so re-showing restores history. The cloud retained-topic lifecycle and Dale persistence eviction were not examined.
- **R6 — Two evaluators, one behavior.** C# (SDK/Dale/cloud) and TS (dashboard) must agree bit-for-bit on predicate evaluation and on Model→count derivation. Mitigation: the shared conformance vector (§7). Predicate serialization (string vs AST) and the derived-input representation are unsettled.
- **R7 — Inert per-plug array vs Model-derived.** Whether to carry inert per-plug setup as a length-validated runtime array (mismatch class, runtime-only check) or derive it from `Model` (no array, no mismatch). Not decided.
- **R8 — Multiplicity validator is the load-bearing cloud gate.** `LogicConfigurationMultiplicityValidator` iterates `definition.Interfaces` and assumes a static enumerated set; gate resolution must be threaded through it carefully, including the provider-side `Consumers` cap grouping.
- **R9 — Analyzer completeness.** Getting the predicate/type/reference-scope analyzers right matters (a bad `[ExistsWhen]` that references a non-structural or non-scalar property must fail at build, not at deploy). The precise DALE#### set and their diagnostics are unspecified.
- **R10 — Introspection snapshot / PublicApi drift.** New introspection fields will move the `docs/snapshots` manifest and any CLI-help/PublicApi snapshots; the CI auto-commit-on-PR flow (see repo memory) must be reconciled.

## 12. Requirement tags (for the plan/spec)

- **R-SDK-1** `[StructuralConfig]` + `[ExistsWhen]` attributes; scalar/pre-bind/immutable semantics.
- **R-SDK-2** Binders resolve gates; define the introspection (full+predicates) vs live (filtered) split (R2).
- **R-SDK-3** `LogicBlockBase` applies structural inputs pre-`Configure`; initial state-updates + handler-visibility over the live set only.
- **R-SDK-4** Analyzers (predicate parse/type/reference-scope; array-length relation).
- **R-CON-1** Additive introspection fields (predicate + structural descriptor); `SetLogicConfigurationPayload` carries structural values; conformance vector.
- **R-CLOUD-1** Store structural values on the instance; project the live view in active read-models; gate-aware multiplicity; ship resolved members at activate.
- **R-DASH-1** Structural-input config form; gate-resolved slot rendering; reconcile-on-change; gate-aware multiplicity UI; TS evaluator.
- **R-DALE-1** Apply structural inputs pre-bind; link/topic/handle only the live set; dormant persistent state.
- **R-SHARED-1** One predicate grammar + evaluator behavior across C#/TS, guarded by a conformance vector (shared with RFC 0017).

## 13. Prior art in the platform (why this fits)

The template layer already parameterizes structure at configuration time — `LogicConfigurationTemplate` carries typed parameters and expands `RepeatParameterIdentifier` (count), `ConditionalOnParameter` (include), `VariantParameterIdentifier` (swap), and `visibleWhen` (field visibility) **client-side** into a concrete config, deployed as a per-gateway snapshot with recycle semantics and a first-class draft/active split (`IsCurrent`/`ActivatedAt`). This RFC pulls one of those operators — *include/exclude a member by a config predicate* — **down** from the whole-block template layer into the **block/introspection** layer, where it can reach inside a block, be block-owned, and be resolved by the runtime. It reuses the existing config-time pipeline and its already-solved draft-vs-active sync; it invents no new deploy or runtime-reactivity model.
