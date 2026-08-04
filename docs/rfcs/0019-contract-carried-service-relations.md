# RFC 0019 — Contract-carried service relations

- **Status:** Draft — 2026-08-04. Design only; implementation not started.
- **Author:** jonas.bertsch (design drafted with Claude)
- **Origin:** service-relations request from `logic-block-libraries` (ecocoach, 2026-08-04) — the
  first real consumer of `[ServiceRelation]`. Their request identifies two blockers (carrier
  forces marker interfaces; component services can't participate) and asks for a decision on the
  carrier and the component identifier before they build their spec vocabulary and CI gate.
- **Related:** RFC 0016 (config-time structural gating — the inclusion-gate interplay in §5.4);
  `[LogicBlockContract]` / `LogicClassGenerator` (the carrier this RFC moves relations onto).
  Consumers: `cloud-api` (`ActiveLogicConfigurationDataReadModelUpdater` — unchanged by this RFC),
  `dashboard` (renders nothing from relations today).

## 1. Summary

`[ServiceRelation]` moves from **service interfaces** (two independent halves, resolved against
class-level interface bindings only) to the **`[LogicBlockContract]` class** (one declaration,
naming the relation type and which contract side is the outwards half). The SDK then **derives**
both halves mechanically, per **bound interface endpoint**, at bind time:

- every block that implements a relation-bearing contract interface emits the matching half —
  no per-block declaration, no marker interfaces, no partial-adoption failure mode;
- each half carries the endpoint's **actual wiring identifier** (class-level override / bare
  interface name / `{Property}_{Interface}` for components) because it is registered by the same
  code path that mints that identifier — the two can no longer diverge;
- each half attaches to the **service that owns the endpoint** (root service for class-implemented
  interfaces, component service for property-bound ones).

The emitted wire shape (`services[].inwardRelations[]` / `outwardRelations[]` with
`{relationType, interfaceIdentifier, interfaceTypeFullName, annotations}`) is **unchanged**, so
cloud-api and vion-contracts need **zero changes**. Relations stay pure cloud metadata: no runtime
behaviour, rows still materialise only where an operator-authored interface mapping exists.

This is a deliberate step back from the original assumption that `[ServiceInterface]` would be the
prominent authoring surface. Real usage (ecocoach's `EnergyManager`: 75 `[ServiceProperty]`s
directly on the class, zero service interfaces) shows service interfaces are optional schema
sharing, not the place block-graph semantics live. The contract — the one artifact both sides of a
wire already share — is.

## 2. What the consumer needs to express

Five relation types in one library (`Ecocoach.EnergyManagement`), all pure topology metadata for
`GET /Tenant/{id}/Services` → `relations[]`:

| RelationType                | Outwards (subordinate/providing)      | Inwards (aggregating/managing)  |
|-----------------------------|---------------------------------------|---------------------------------|
| LinkedParentEnergyManager   | `IEnergyManagerCascadeChild`          | `IEnergyManagerCascadeParent`   |
| LinkedEnergyManagerConsumer | `IControllableConsumer`               | `IControllableConsumerManager`  |
| LinkedEnergyManagerSupplier | `IControllableSupplier`               | `IControllableSupplierManager`  |
| LinkedEnergyManagerBuffer   | `IControllableBuffer`                 | `IControllableBufferManager`    |
| LinkedEnergyManagerMeter    | `IGridMeasurementProvider`            | `IGridMeasurementManager`       |

Shape constraints that break the current design (all verified in `logic-block-libraries`):

- `EnergyManager` implements **six** of the ten interfaces on one class — including **both** sides
  of the cascade relation (one universal block plays parent *and* child) — and has **no
  `[ServiceInterface]`** (`Ecocoach.EnergyManagement/LogicBlocks/EnergyManager/EnergyManager.cs:72-103`).
- The only production `IControllableConsumer` today is a nested `ChargePoint` component exposed as
  **two properties** of `BatterySystemLiebherrLpo600` (`BatterySystemLiebherrLpo600.cs:217-223`),
  whose real wiring identifiers are `ChargePoint1_IControllableConsumer` /
  `ChargePoint2_IControllableConsumer` (checked-in topologies use exactly these:
  `topologies/sim-liebherr-solarlog.topology.json:18-19`).
- All ten interfaces are **source-generated** from `[LogicBlockContract]` static classes; each
  carries `[LogicInterface(MatchingInterface = …, SenderInterface = …, ContractType = …)]`
  pointing back at its contract. The contract is already the single artifact that names both sides.

## 3. Verified current behaviour (and corrections to the request)

The integrator asked us to verify their reading. It is essentially correct; the load-bearing facts,
plus four corrections, with sources:

### 3.1 SDK

- Carrier: `[AttributeUsage(AttributeTargets.Interface)]`, `AllowMultiple` unset → one relation
  per interface ([ServiceRelationAttribute.cs:11](../../Vion.Dale.Sdk/Core/ServiceRelationAttribute.cs)),
  and relations are only read off `[ServiceInterface]` types
  ([DeclarativeServiceBinder.cs:25-33, 185](../../Vion.Dale.Sdk/Configuration/Services/DeclarativeServiceBinder.cs)).
  Both read sites already call the **plural** `GetCustomAttributes<ServiceRelationAttribute>()`
  (binder :185, [ServiceDeclaration.cs:78](../../Vion.Dale.Sdk/Configuration/Services/ServiceDeclaration.cs))
  — written for a multiplicity the attribute forbids. Confirmed.
- The root service is created unconditionally from the class name **before** the service-interface
  loop ([DeclarativeServiceBinder.cs:22](../../Vion.Dale.Sdk/Configuration/Services/DeclarativeServiceBinder.cs))
  — every block has a service for relations to attach to. Confirmed.
- **Problem 2 mechanism confirmed, precisely:** relation identifier resolution looks only at
  **class-implemented** logic interfaces and **class-level** `[LogicBlockInterfaceBinding]`
  (binder :188-189, :201-202 — `attr.Identifier ?? interface.Name`), while the interface binder
  mints **`{Property}_{Interface}`** for property-bound endpoints
  ([DeclarativeInterfaceBinder.cs:93](../../Vion.Dale.Sdk/Configuration/Interfaces/DeclarativeInterfaceBinder.cs))
  with per-property overrides (:86, :117). Two minting rules for the same concept; a component
  relation can never equal its wiring identifier. Additionally the `FunctionInterfaceType` match
  silently no-ops at 0 and >1 candidates (binder :194, :217-220).
- `ServiceDeclaration<T>.DefineRelation` is **dead public API** — exactly zero callers repo-wide.
  It also still requires the attribute on the service interface, so it never escaped Problem 1.
- The two examples chose **opposite** direction conventions: PingPong makes the request *sender*
  Outwards (`examples/…/IPingService.cs:7`), LightToToggle makes the state-update *receiver*
  Outwards (`Vion.Dale.Sdk/Examples/ServiceInterfaces/ILightService.cs:8`). Confirmed. Further:
  the LightToToggle service interfaces (`ILightService`, `IToggleService`, `IOtherService`) are
  **orphans compiled into the shipped SDK assembly** — no block implements them. PingPong is the
  only end-to-end relation example, and no tests or prose docs exist anywhere.
- Relations are stored per **service** (`ServiceBinder._serviceRelations`,
  [ServiceBinder.cs:26](../../Vion.Dale.Sdk/Configuration/Services/ServiceBinder.cs)) and split
  into `inwardRelations`/`outwardRelations` at introspection
  ([LogicBlockIntrospection.cs:277-299](../../Vion.Dale.Sdk/Introspection/LogicBlockIntrospection.cs));
  `Direction` itself never reaches the wire.
- The introspection already resolves contract sides per endpoint — `MergeContractAnnotations`
  ([LogicBlockIntrospection.cs:145-181](../../Vion.Dale.Sdk/Introspection/LogicBlockIntrospection.cs))
  determines the side by short-name comparison against `BetweenInterface` and emits
  `ArrowDirection`/`RoleDefaultName` annotations. The pattern this RFC generalises already exists.

### 3.2 cloud-api (corrections in bold)

`cloud-api: Cloud.Api/TenantApis/Services/EventHandlers/ActiveLogicConfigurationDataReadModelUpdater.cs:349-482`:

- **Correction 1 — matching is two-stage, and the identifier is compared per side against the
  mapping, never across sides.** Stage 1 pre-filters each side's halves by
  `relation.InterfaceIdentifier == mapping.InterfaceIdentifier` (from side, :392/:404) resp.
  `== mapping.MappedInterfaceIdentifier` (to side, :416/:429) — ordinal string equality. Stage 2
  pairs across sides by **`relationType` equality only** (:441, :463), via `FirstOrDefault`.
  `interfaceTypeFullName` plays no part anywhere.
- **Correction 2 — both orientations are tried** (`fromOutward↔toInward` *and*
  `fromInward↔toOutward`), so one operator wire can yield 0, 1, or 2 rows, and relation direction
  is independent of which way the operator authored the mapping. This is why both existing
  examples "work" despite opposite conventions, and it means ecocoach's topology convention
  (source = manager/initiator side) coexists fine with Outwards = subordinate.
- **Correction 3 — the persisted `serviceIdentifier` is not the SDK service name.** It is the
  per-instance **service GUID** from the operator draft's `ServiceIdMapping`, joined by SDK
  service name (:395 `fromLogicBlock.Services.Single(sv => sv.Identifier == service.Identifier)`,
  :451 `.ServiceId.ToString()`). `serviceProviderIdentifier` is hardcoded `"dale"` on both
  endpoints — a relation endpoint outside dale is structurally impossible today.
- Persisted row (`ActiveServiceRelationReadModel`): `relationType` + two
  `(edgeGatewayId, serviceProviderIdentifier, serviceIdentifier)` triples + tenant/project/config
  scoping keys. No interface identifier, no annotations, **no ordering column**. The 8-column
  composite PK makes a duplicated pair a **hard crash of the whole activation projection**
  (duplicate `Add` in the EF change tracker), which is also the self-mapping failure mode — there
  is no self-mapping guard at any layer, and it's a crash, not a cosmetic self-edge.
- Silent failures confirmed and sharper: missing blocks/definitions log warnings (:354, :361,
  :369, :378); unmatched relation halves and empty pre-filters produce **no signal whatsoever**.
  A typo'd `relationType` or `interfaceIdentifier` is invisible end to end.
- **Correction 4 — `annotations`/`DefaultName` are dropped from the relation path but are *not*
  dead in the system**: the full `ServiceInfo` (including relation annotations) persists as jsonb
  on `LogicBlockDefinitionEntity.Services` and is re-served verbatim by both
  LogicBlockDefinitions controllers. The dashboard *could* already read them from the definition
  catalogue; nothing does.
- The relation's `interfaceIdentifier` must ordinal-equal an `interfaces[]` entry `identifier` on
  the same block definition — that is what mappings reference
  (`LogicConfigurationMultiplicityValidator.cs:142-143`). Nothing validates a relation names a
  declared interface; an undeclared value silently never matches.

## 4. Design

### 4.1 The attribute

`ServiceRelationAttribute` is **redefined** (breaking; zero real users — ecocoach is explicitly
waiting for the new shape, and the two in-repo usages migrate in the same PR):

```csharp
/// <summary>
///     Declares that an operator wiring of this contract constitutes a service relation of the
///     given type between the two blocks' services. The SDK derives one relation half per bound
///     interface endpoint on each side; a relation row materialises in the cloud only where an
///     operator-authored interface mapping connects two blocks over this contract.
///     Convention: <see cref="OutwardsInterface"/> names the subordinate / providing side (the
///     start of the arrow — e.g. a consumer, supplier, meter, or cascade child); the other
///     contract interface is the inwards, aggregating / managing side (the end of the arrow —
///     e.g. an energy manager or cascade parent).
/// </summary>
[PublicApi]
[AttributeUsage(AttributeTargets.Class, AllowMultiple = true)]
public sealed class ServiceRelationAttribute : Attribute
{
    /// <summary>
    ///     Identifier of the relation as it appears in the cloud API (`relationType`). An opaque,
    ///     ordinal-compared, stable contract string — renaming it is a breaking metadata change
    ///     for every dashboard/API consumer keying on it.
    /// </summary>
    public required string RelationType { get; init; }

    /// <summary>
    ///     Which of the contract's two interfaces (`BetweenInterface` / `AndInterface`, by the
    ///     same short-name string) is the outwards side. Must equal one of the two; validated at
    ///     bind time and by analyzer DALE045.
    /// </summary>
    public required string OutwardsInterface { get; init; }
}
```

Usage, next to the attribute it rides with (ecocoach's five declarations become exactly):

```csharp
[LogicBlockContract(BetweenInterface = "IControllableConsumer",
                    AndInterface = "IControllableConsumerManager", …)]
[ServiceRelation(RelationType = "LinkedEnergyManagerConsumer", OutwardsInterface = "IControllableConsumer")]
public static class ControllableConsumerContract { … }

// ControllableSupplierContract: RelationType = "LinkedEnergyManagerSupplier", OutwardsInterface = "IControllableSupplier"
// ControllableBufferContract:   RelationType = "LinkedEnergyManagerBuffer",   OutwardsInterface = "IControllableBuffer"
// GridMeasurementContract:      RelationType = "LinkedEnergyManagerMeter",    OutwardsInterface = "IGridMeasurementProvider"
// EnergyManagerCascadeContract: RelationType = "LinkedParentEnergyManager",   OutwardsInterface = "IEnergyManagerCascadeChild"
```

Design notes:

- **String, not `Type`** — consistent with `BetweenInterface`/`AndInterface`, and necessary for
  the same reason those are strings: the interfaces are *generated from* this class.
- **Explicit side, not positional/`ContractDirection`-derived** — ecocoach's own contracts prove
  `Between`/`And` ordering carries no consistent subordinate semantics (device contracts put the
  device in `Between`; the cascade contract puts the **Parent** there), and `ContractDirection`
  describes message-arrow rendering ("who initiates"), a third, different axis. Overloading either
  would project the cascade backwards.
- **`AllowMultiple = true`** — a contract may express several relation types (each derives its own
  halves; cloud-api pairs per type). Duplicate `RelationType` values on one contract are an
  analyzer error (they would produce duplicate rows → the PK crash in §3.2).
- **`ServiceRelationDirection` stays** (with the convention documented in its XML docs) — it
  remains the split key between the `inwardRelations`/`outwardRelations` arrays.
- **Removed:** the old ctor, `FunctionInterfaceType`, `Direction`, `DefaultName`, `Annotations`
  (see §4.5 for the label story), `ServiceDeclaration<T>.DefineRelation` (dead), and
  `DeclarativeServiceBinder.AutoDetectServiceRelationsForInterface` (superseded).

### 4.2 Derivation: one half per bound endpoint

At bind time, whenever an interface endpoint is bound (today
[DeclarativeInterfaceBinder.BindLogicInterface](../../Vion.Dale.Sdk/Configuration/Interfaces/DeclarativeInterfaceBinder.cs)),
the SDK additionally resolves the endpoint's contract
(`LogicInterfaceAttribute.ContractType`) and, for each `[ServiceRelation]` on it, registers:

```
ServiceRelationInfo {
    RelationType          = attr.RelationType,
    InterfaceIdentifier   = the endpoint's bound identifier,        // same variable, same code path
    InterfaceTypeFullName = the endpoint's logic interface full name,
    Direction             = endpoint interface short name == attr.OutwardsInterface
                              ? Outwards : Inwards,
}
```

on the **owning service**:

| Endpoint kind | Endpoint identifier (unchanged rules) | Owning service |
|---|---|---|
| Class-implemented interface | class-level `[LogicBlockInterfaceBinding].Identifier` ?? interface name | root service (`type.Name`) |
| Property-bound interface, property type is service-bearing¹ | property-level `Identifier` ?? `{Property}_{Interface}` | component service (`property.Name`) |
| Property-bound interface, property type **not** service-bearing | property-level `Identifier` ?? `{Property}_{Interface}` | **none — no half is emitted** (endpoint still binds and wires normally); DALE045 warns at the property (§4.6) |

¹ service-bearing = implements a `[ServiceInterface]` or has `[ServiceProperty]`/
`[ServiceMeasuringPoint]` members — the exact predicate
`DeclarativeServiceBinder.BindPropertyBasedServices` already uses to create component services;
it is extracted into a shared internal helper so the two passes cannot drift.

**Why no root-service fallback for non-service components.** A relation endpoint in the cloud
model *is* a service (the row persists service GUIDs, nothing else — §3.2). A component without a
service surface has no node in the graph, so there is nothing correct to anchor its edge to.
Falling back to the root service is actively dangerous: with two such components (the
ChargePoint shape), both halves would land on the root service with the same `relationType`, and
wiring both to the same counterpart would produce two rows identical in all eight PK columns —
the activation-projection crash from §3.2. It would also collapse two real edges into one
indistinguishable row even if cloud-api deduped. The convention is therefore: **to participate in
relations, a component must be a service** (one `[ServiceProperty]` is enough); a block that wants
the edge at block granularity implements the contract interface class-level instead. Skipping is
compile-time loud (DALE045 warning at the property), never a silent runtime drop.

This yields the invariant that keeps rows PK-distinct by construction: a class implements a given
interface at most once, and each component property is its own service — so **every service
carries at most one half per (contract side, relation type)**. Distinct endpoints always mean
distinct services, hence distinct rows.

The load-bearing invariant, and the permanent fix for Problem 2: **the half is registered by the
same code path that mints the endpoint identifier.** There is no second resolution rule left in
the codebase. The `FunctionInterfaceType` matching heuristic — including its two silent no-op
branches — is deleted, not repaired.

Plumbing: `LogicBlockBase` invokes the binders in the order interfaces → contracts → services
([LogicBlockBase.cs:539-542](../../Vion.Dale.Sdk/Core/LogicBlockBase.cs)); the interface binder
gains access to the `ServiceBinder` (already on the same builder) to register halves. Order is
irrelevant to correctness: `RegisterServiceRelation` is keyed by service identifier, and the
introspection joins by key ([ServiceBinder.cs:315](../../Vion.Dale.Sdk/Configuration/Services/ServiceBinder.cs)).

Introspection, parser, and wire model are **untouched**: halves split by `Direction` into the
existing `inwardRelations`/`outwardRelations` arrays with the existing four fields
(`annotations` now always `{}`).

### 4.3 Worked example — ecocoach

With the five contract declarations from §4.1 and no change to any block:

`EnergyManager` (root service `EnergyManager`, six class-implemented relation-bearing endpoints):

```jsonc
"inwardRelations": [
  { "relationType": "LinkedEnergyManagerConsumer", "interfaceIdentifier": "IControllableConsumerManager", … },
  { "relationType": "LinkedEnergyManagerSupplier", "interfaceIdentifier": "IControllableSupplierManager", … },
  { "relationType": "LinkedEnergyManagerBuffer",   "interfaceIdentifier": "IControllableBufferManager", … },
  { "relationType": "LinkedEnergyManagerMeter",    "interfaceIdentifier": "IGridMeasurementManager", … },
  { "relationType": "LinkedParentEnergyManager",   "interfaceIdentifier": "IEnergyManagerCascadeParent", … }
],
"outwardRelations": [
  { "relationType": "LinkedParentEnergyManager",   "interfaceIdentifier": "IEnergyManagerCascadeChild", … }
]
```

`BatterySystemLiebherrLpo600` — the multi-service block
(`class BatterySystemLiebherrLpo600 : LogicBlockBase, IControllableBuffer, IBufferTelemetry, …`
plus two ChargePoint properties, `BatterySystemLiebherrLpo600.cs:58, :217-223`) emits three
services, each owning exactly its own endpoints' halves:

```jsonc
{ "identifier": "BatterySystemLiebherrLpo600",          // root: class-implemented endpoints
  "outwardRelations": [
    { "relationType": "LinkedEnergyManagerBuffer",
      "interfaceIdentifier": "IControllableBuffer", … } ] },
{ "identifier": "ChargePoint1",                          // component service (service-bearing)
  "outwardRelations": [
    { "relationType": "LinkedEnergyManagerConsumer",
      "interfaceIdentifier": "ChargePoint1_IControllableConsumer", … } ] },
{ "identifier": "ChargePoint2",
  "outwardRelations": [
    { "relationType": "LinkedEnergyManagerConsumer",
      "interfaceIdentifier": "ChargePoint2_IControllableConsumer", … } ] }
```

Against their checked-in `sim-liebherr-solarlog.topology.json` (four wirings touch this block:
EM→buffer :17, EM→CP1 :18, EM→CP2 :19, battery→OperatorBufferSteering :25) this yields **four
PK-distinct rows**: buffer edges battery→EM and battery→OperatorBufferSteering (same endpoint
wired twice — one row per mapping, distinct inward GUIDs; note the two mappings are authored in
*opposite* source/target directions and both orient correctly per §3.2 correction 2), plus one
consumer edge per charge-point service. `OperatorBufferSteering` participating is by intent: it
implements `IControllableBufferManager`, so a wire to it *is* a buffer-management edge — every
implementer of a relation-bearing contract participates, reference and steering blocks included.

Tracing the two hard cases through cloud-api's verified matcher:

- **Component wiring** `(EM, "IControllableConsumerManager") → (Liebherr, "ChargePoint1_IControllableConsumer")`:
  from-side pre-filter hits EM's inward half; to-side pre-filter hits `ChargePoint1`'s outward
  half; pass 2 pairs on `LinkedEnergyManagerConsumer` → one row, Outward = ChargePoint1's service
  GUID, Inward = EM's. `ChargePoint2` produces its own row from its own mapping. *(Previously
  impossible — Problem 2.)*
- **Cascade** `(parent EM, "IEnergyManagerCascadeParent") → (child EM, "IEnergyManagerCascadeChild")`:
  from-side pre-filter selects only the parent's **inward** half (its outward half sits on the
  child-side endpoint identifier and fails the filter); to-side selects only the child's
  **outward** half. Exactly one row, correct orientation. The identifier pre-filter is what makes
  the dual-role block safe — no cross-pairing, no duplicate row, even though both halves of the
  same relation type live on the same class. *(The request's key worry, answered by
  construction.)*
- Wiring authored in the opposite direction produces the same row via the other pass (§3.2,
  correction 2) — the design is robust to both topology-authoring conventions.

### 4.4 What becomes impossible (error classes removed)

| Failure mode today | After |
|---|---|
| Halves declared on only one side → silently no row | unrepresentable — both halves derive from one declaration |
| RelationType typo between the two sides | unrepresentable — single string, declared once |
| Relation identifier ≠ wiring identifier (components, overrides) | unrepresentable — same code path mints both |
| Direction convention ambiguity (two examples disagree) | one documented convention; side named explicitly per contract |
| `FunctionInterfaceType` matches 0 / >1 interfaces → silent drop | mechanism deleted; every bound endpoint of a relation-bearing contract emits |
| Duplicate `(relationType)` halves on one service → cloud PK crash | analyzer error for the per-contract case (DALE045); derivation emits exactly one half per (endpoint, declaration). Reusing one `RelationType` across *two* contracts wired between the same two services can still collide — cloud-side dedupe stays a follow-up (§7) |
| Marker `[ServiceInterface]`s invented to carry relations | carrier no longer involves service interfaces at all |

Remaining (accepted): a relation-bearing contract emits halves for *every* implementing block —
reference/test blocks included. That is correct by intent: the row still only materialises where
an operator wires the contract, and a wired block *is* part of the topology.

### 4.5 Labels (`DefaultName`) — removed from relations

The old attribute's `DefaultName` is write-only end to end (§3.2, correction 4; the dashboard
renders nothing from relations — verified in the i18n spec,
`architecture/specs/in-flight/2026-07-10-logic-block-libraries-i18n.md:99-103`, which cut relation
names from v1 for exactly that reason). This RFC removes it rather than porting it:

- **Edge-end labels already exist**: the contract's `BetweenDefaultName`/`AndDefaultName` are
  emitted per endpoint as `RoleDefaultName`/`MatchingRoleDefaultName` interface annotations
  ([LogicBlockIntrospection.cs:169-180](../../Vion.Dale.Sdk/Introspection/LogicBlockIntrospection.cs)) —
  a UI labelling relation ends has richer material there than a single relation-level name.
- **Relation-*type* labels are a platform concern**: cloud-api treats `relationType` as an opaque
  pass-through (no lookup table anywhere), so display naming/i18n belongs to the dashboard, keyed
  by `relationType` — as in the older ecocoach system (per-direction translation keys on a
  relation-type entity). **Consequence, stated explicitly for consumers: `relationType` strings
  are a stable public contract** (API + future UI key). Choose them deliberately; renames are
  breaking. The `annotations` wire field stays (always `{}`) so re-adding per-declaration
  metadata later is additive.

### 4.6 Validation — bind-time + analyzer DALE045

Fail loud where today everything is silent:

- **Bind/introspection time** (throws, fails `dale build`'s parse step): `OutwardsInterface` not
  equal to `BetweenInterface` or `AndInterface`; `[ServiceRelation]` on a class without
  `[LogicBlockContract]`.
- **Analyzer `DALE045_ServiceRelationDiscipline`** (error severity, same rules surfaced in-IDE,
  plus): duplicate `RelationType` on one contract; empty/whitespace `RelationType` or
  `OutwardsInterface`. **Warning** severity: a property whose type implements a relation-bearing
  contract interface but is not service-bearing — its endpoint wires normally but emits no
  relation half (§4.2); the fix is giving the component a service surface (or implementing the
  interface class-level for block-granularity edges). Additionally, **warning** severity
  (suppressible) when the same
  `RelationType` appears on two contracts in one compilation — legitimate for contract versioning
  (V1/V2 mapping to the same edge type), hazardous when both are wired between the same two
  services (§4.4). Slots in as the next free ID (DALE001–044 in use), registered in
  `DaleDiagnostics` per house pattern; the "discipline" catch-all ID mirrors DALE044.

Not validated here (cloud-side, see §7): unmatched halves at activation, self-mappings,
mappings referencing undeclared interfaces.

### 4.7 Wire compatibility, versioning, package skew

- Publish JSON shape unchanged; `Vion.Contracts` (cloud-api pins 6.3.0) unchanged; cloud-api code
  unchanged. **No cross-repo deployment coupling.** The only observable JSON change is that
  relation halves now appear wherever contracts declare them (and `annotations` is always empty).
- SDK public-API surface changes (attribute redefinition, `DefineRelation` removal) → next
  **minor** (0.10.0), PublicApi snapshot regenerated (CI snapshot bot will assist).
- **Package skew:** halves are derived per package at parse time from the contract assembly that
  package was built against. Within one library (ecocoach's case) this is atomic. For a contract
  shared across libraries, both sides' packages must be built against a contract version carrying
  the declaration; skew degrades to "no row" — the same partial-adoption semantics cloud-api has
  today, never a crash. Documented in the attribute's XML docs.

### 4.8 Migration (this repo, same PR)

- **PingPong** (the only real example): move the declaration to the contract —
  `[ServiceRelation(RelationType = "PingPong", OutwardsInterface = "IPong")]` on
  `Contracts/PingPong.cs`; delete both interface-level attributes. Note this **flips** the emitted
  direction versus today (Ping was Outwards): under the documented convention the *responder*
  (Pong, the provider) is the outwards side — the example previously demonstrated the opposite of
  LightToToggle, and one of them had to flip. Regenerate the checked-in publish JSON.
- **LightToToggle**: delete the orphan `ILightService`/`IToggleService`/`IOtherService` from
  `Vion.Dale.Sdk/Examples/` (dead code shipped in the SDK assembly; would not compile against the
  new shape). The `Toggling` contract may carry a doc-example `[ServiceRelation]` instead.
- **Tests** (net-new; none exist):
  - Introspection: dual-role class (cascade — one inward + one outward, same type, distinct
    identifiers); component halves with `{Property}_{Interface}` identifiers on component
    services; property-level `Identifier` override flowing into the half; non-service-bearing
    component emits no half; multiple `[ServiceRelation]` per contract; RFC 0016-gated
    property endpoint (Definition emits, Live omits when gated out); invalid `OutwardsInterface`
    throws.
  - Golden JSON: PingPong publish JSON asserted (shape pin the integrator asked for).
  - Analyzer tests for DALE045 per house pattern.
- **Docs**: XML docs on the attribute are the canonical convention statement (the request asked
  for exactly "a line in the XML docs"); this RFC is the prose reference.

## 5. Gating interplay (RFC 0016)

A property-bound endpoint gated out by `[IncludedWhen]` never binds in Live mode → its halves are
never registered, consistent with "no endpoint, no wiring, no relation". Definition mode binds the
full set → halves always present in the definition JSON, and activation-side exclusion already
handles the rest (cloud-api filters relation services by inclusion at
`ActiveLogicConfigurationDataReadModelUpdater.cs:389` and rejects mappings to excluded members at
activation). The root service is never gated, so class-implemented halves are unconditional. No
new gating surface.

## 6. Alternatives considered

1. **`AllowMultiple = true` + `AttributeTargets.Class` on the block** (request option 1): smallest
   diff, but ecocoach would write ten halves across their blocks, *every* third-party device block
   must remember its half (silent partial-adoption returns), cross-side consistency stays
   advisory, and each class-level half still needs an anchor to pick which implemented interface
   it rides — reinventing the `FunctionInterfaceType` matching this RFC deletes. Rejected.
2. **On `[LogicBlockInterfaceBinding]`** (request option 3): solves the identifier by
   construction, but is still two-sided/advisory, forces binding attributes to exist purely to
   carry relations (six on `EnergyManager`), and mixes wiring metadata with graph semantics.
   Rejected as the primary carrier — but it is the natural **future extension slot** for a
   per-binding opt-out/override if a block ever must not participate (YAGNI today).
3. **Fluent `DefineRelation` from `Configure()`** (request option 4): imperative, undiscoverable,
   zero users, still needs an attribute anchor for cross-side consistency. Deleted.
4. **Declaring on services directly** (per-service attribute, the "define on actual services"
   idea): the halves *do* land on actual services in this design — but authored per-service the
   declaration would again be two-sided, need an endpoint anchor, and be repeated per implementing
   block. The contract is the unique place where both sides and the edge semantics are known
   simultaneously; deriving per-service data from it gives the same result with strictly fewer
   failure modes.
5. **Folding into `[LogicBlockContract]` properties** (e.g. `RelationType = …` on the contract
   attribute): equivalent semantics, but a separate attribute keeps relations opt-in and
   `AllowMultiple`, and avoids growing the already five-property contract attribute. Cosmetic
   preference; either works.

## 7. Out of scope — follow-ups this RFC surfaces but does not implement

**cloud-api hardening** (separate issue in that repo; none of it gates this RFC):

- log-warn unmatched relation halves and empty pre-filter results (parity with the
  missing-block/definition warnings at :354-378);
- guard self-mappings at activation and dedupe before `Add` — today a self-wire or duplicated
  pair **crashes the whole activation projection** via the composite PK;
- harden the `.Single()` service-id join (:395) against definition/draft skew;
- decide the relation label story (platform-side `relationType`-keyed names vs reading the
  definition-catalogue annotations).

**Elsewhere:** dashboard rendering of `relations[]` (nothing consumes it yet);
DevHost visualisation of derived relations (would ride the observe model, cf. RFC 0012);
`Vion.Dale.Cli`'s stale `DalePluginInfo` mirror (predates this work);
vion-contracts XML-doc example strings (cosmetic, next contracts release).

## 8. Decision points

1. **Carrier = contract** (§4.1) — the core decision the integrator is waiting on.
2. **Direction convention** — Outwards = subordinate/providing (arrow subordinate → aggregator),
   named explicitly per contract; PingPong flips to comply (§4.8).
3. **Drop relation-level `DefaultName`** (§4.5) — makes `relationType` formally a UI/API contract.
4. **Component halves attach to the component service; non-service-bearing components emit no
   half** (DALE045 warning, no root fallback — a fallback would collapse edges and can crash the
   activation projection via PK-identical rows) (§4.2).
5. **Redefine `ServiceRelationAttribute` in place** (breaking, 0.10.0) rather than introducing a
   parallel attribute and deprecating.
