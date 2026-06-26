# RFC 0013 — DevHost topology authoring (semi-automated, mappings-first) + dashboard export

- **Status:** Proposed
- **Date:** 2026-06-25
- **Author:** jonas.bertsch
- **Related:** RFC 0006 (scenario files), RFC 0008 (unified scenario+topology), RFC 0012 (DevHost UI observe/drive model), RFC 0003 (headless control). Cross-repo: `dashboard` (Logic Editor), `cloud-api` / `vion-contracts` (introspection contract).

> This is a design contract, not an implementation. It is the document an implementation plan is generated from. It deliberately stops at the seams (exact field names, route bodies, component layout) so the plan can fill them in against current `main`.

## 1. Summary

Today a DevHost **topology** (`topologies/*.topology.json` — which logic-block instances exist and how their interfaces wire) is authored only by hand-editing JSON or via the CLI export. The wiring layer is the least-guarded, most error-prone surface in the whole flow: long fully-qualified `typeFullName` strings with no completion, 12–14-row `interfaceMappings` whose compatibility and multiplicity the schema never checks, and **no offline validate** (a bad topology fails only at DevHost load, i.e. the nightly lane).

This RFC adds a **lightweight, semi-automated topology editor inside the DevHost UI** — a `view ⇄ edit` sub-mode of the existing topology slide-over (the RFC 0012 §7 extension point), built under the no-build discipline — whose spine is **AutoConnect-first wiring**: the machine wires the unambiguous interface pairs, the human resolves the residue (contested / required-but-unmatched endpoints) by picking from **compatible-only** candidates. It is complemented by a one-way **"Download as DevHost topology"** button in the dashboard's Logic Editor for the cases where a topology is authored there.

The architecture follows the dashboard's own split: the **server exposes introspection data and does authoritative JSON write/validate**; the **client owns the wiring intelligence** (candidate matching, cardinality bounds, AutoConnect-preview, residue), reusing the *frozen, cross-repo* matching contract (`LinkMultiplicity` + `MatchingInterfaceTypeFullNames`).

## 2. Motivation

From the consumer field log and the real corpus (18 topologies / 44 scenarios in `logic-block-libraries`):

- **Identity is hand-typed.** Every `typeFullName` is a long namespace-qualified string with no completion from the (structural-only) topology schema, and no validation until the DevHost resolves the plugin assembly at load.
- **Wiring is unguarded.** `interfaceMappings` are 4-string rows where both the block names *and* the interface-pair compatibility are unchecked by the schema; `em-closed-loop` / `operator-steering` carry 12–14 rows. Compatibility and single-writer-vs-fan-in multiplicity are decided only inside `AutoConnect`, and its residue is discarded (`Console.WriteLine` only). DF-19 (a legitimate fan-in silently dropped) lived in exactly this gap.
- **No offline topology validate.** Scenario files have `dale scenario validate`; topology files have nothing equivalent (DF-17). A hand-edited topology only fails at `DevTopologyLoader.Load` time.
- **Two near-identical JSON shapes.** Strict `JsonUnmappedMemberHandling.Disallow` means a wrong field name fails opaquely (DF-11).

The user verdict that scopes this RFC: **topology is priority 1, especially getting the mappings right, semi-automated; scenario authoring is secondary.** The mapping layers that matter: **interface (block↔block) wiring, type/instance identity, and multiplicity/validity** — *not* contract (HAL/provider) wiring, which stays auto-mocked in v1.

## 3. The decision (and the alternatives rejected)

**Chosen — native lightweight wiring (AutoConnect-first) in the DevHost + a dashboard-export importer.** Synthetic test rigs are authored locally and offline against the DI catalog, so the spine belongs *in* the DevHost; the SDK already owns the wiring knowledge, so the UI only surfaces it. The dashboard export rides along for the "came from a dashboard test setup" case.

Rejected:

- **Dashboard-export-first (DevHost only imports + clone-tweaks).** Inverts the dependency the wrong way: it drags the cloud dashboard (online, auth, GUID-keyed) into authoring *local* synthetic rigs. Fights "mostly synthetic."
- **Shared vue-flow editor across both apps.** Requires a Vue/Vite build inside the no-build SPA and couples the local tool to the dashboard stack — RFC 0008 §6.3 already made this a non-goal. Over-built for "lightweight."

## 4. UX principles

The DevHost editor and the dashboard's Logic Editor author the *same conceptual graph* (definitions + instances + interface/contract mappings) at **two different layers**: the dashboard composes a **deployment** (cloud-backed, spatial, enforced, versioned, gateway-bound, drag-first); the DevHost stages a **test rig** (local, file-based, guided, agent-co-authored, fast-loop). These principles operationalize that difference — each is paired with the dashboard contrast that justifies it.

1. **Edit the file, not a diagram.** The surface mirrors the artifact — instances + mapping rows — not a node-graph canvas; the topology file has no geometry, so the editor maintains none. *(Dashboard: a spatial canvas with ELK auto-layout + a geometry sidecar.)*
2. **Machine wires the obvious; you resolve the ambiguous.** AutoConnect is a first-class authoring engine; the UI spends the human's attention on the residue. *(Dashboard: auto-*layout*, not auto-*wire*.)*
3. **Offer only what's compatible — guide hard, never block what the runtime accepts.** Compatible-only candidate pickers with multiplicity hints; but honoring "SDK declares, cloud enforces," the editor guides, it does not reject a config the runtime tolerates. *(Dashboard: same advisory stance.)*
4. **Local, offline, instant inner loop; the heavy step is explicit.** Edit→validate runs in-process against the live catalog (one oracle); the only expensive action — recycle-to-run — is deferred to an explicit "switch & run." *(Dashboard: every change round-trips to cloud-api.)*
5. **The editor is a guest, not a workspace.** An opt-in, gateable sub-mode built from the host's existing primitives under the no-build rule; never a peer of Explore/Verify. *(Dashboard: a full Quasar/Pinia/vue-flow app.)*
6. **Human, agent, and CLI are co-authors over one file.** The topology is git-committed JSON that agents and `dale` also write; the editor round-trips it byte-faithfully (raw-JSON escape hatch) and holds no privileged state the file can't express. *(Dashboard: the cloud entity is the source of truth; the editor is the sole author.)*
7. **Scoped to the rig, never the deployment.** Instances + interface wiring (contracts mostly auto-mocked); no gateways, versions, activation, or templates/variants. *(Dashboard: models all of them.)*

Throughline: **the dashboard composes a system; the DevHost stages a test** — that is why one is a canvas and the other is a guided list.

## 5. Terminology

Align on the platform-canonical vocabulary where it is cheap and net-new; keep `topology` for the dev artifact.

- **Logic block definition** — a catalog block type (cloud-api `LogicBlockDefinitions`, dashboard `LogicBlockDefinition`, the shared `LogicBlockIntrospectionResult`). The new catalog surface uses this term.
- **Logic block instance** — an instantiated block. The DevHost topology already uses `logicBlockInstances` / `interfaceMappings` / `contractMappings`, which match the dashboard `LogicConfigurationModel` field-for-field. Unchanged.
- **Topology** — the DevHost's dev-time file. **Kept.** It is the *dev-time, name-keyed, deployment-free projection* of a **logic configuration** (cloud `SetLogicConfigurationPayload`, the dale runtime's `LogicSystemConfiguration.json`, dashboard `LogicConfigurationModel`), which additionally carries gateways, versions, and activation that the dev rig omits. This equivalence is documented in `concepts/logic-block-wiring.md` (architecture repo).

The `topology` → `logic configuration` rename is **out of scope** (large breaking cross-repo cascade; meaningful layer distinction). If pursued, it is its own architecture ADR + migration, decoupled from this feature.

## 6. The wiring model (what already exists)

The matching/multiplicity logic is a **frozen, data-driven, cross-repo contract** — the dashboard consumes it client-side and is explicitly "not the enforcement authority" (`dashboard/.../multiplicity.ts` header).

- **Compatibility** = `LogicInterfaceAttribute.MatchingInterface` back-reference. In code: `DevConfigurationBuilder.DiscoverMatchingInterfaces` (the predicate at `Vion.Dale.DevHost/DevConfigurationBuilder.cs:201` — `srcAttr.MatchingInterface == tgtIface || tgtAttr.MatchingInterface == srcIface`). On the wire / client: `LogicBlockIntrospectionResult.InterfaceInfo.MatchingInterfaceTypeFullNames` vs `InterfaceTypeFullNames` (set membership). These are the same relation expressed via `Type` vs type-full-name.
- **Multiplicity** = `LinkMultiplicity { ExactlyOne, ZeroOrOne, OneOrMore, ZeroOrMore(default) }` (`Vion.Dale.Sdk/Core/LinkMultiplicity.cs`), declared consumer-side on `[LogicBlockInterfaceBinding(Multiplicity=…)]`. On the wire it rides as an annotation token (`LogicBlockWiringConventions.MultiplicityAnnotationKey`) in `InterfaceInfo.Annotations`. **Declared, not enforced** by the SDK — cloud-api enforces; the DevHost editor only *guides*.
- **AutoConnect arbitration** (`DevConfigurationBuilder.AutoConnectInterfaces:251`): per endpoint it builds the set of matching counterpart blocks; a single match is always accepted; >1 is accepted only when the endpoint's multiplicity is `OneOrMore`/`ZeroOrMore` (legitimate fan-in), else refused (single-writer contention). The refused residue is currently only `Console.WriteLine`d, never returned.
- **The catalog** = `DevHostBuilder.GetBlockCatalog()` (`Vion.Dale.DevHost/DevHostBuilder.cs:69`) — all `LogicBlockBase` types every plugin assembly's `IConfigureServices` registers. In-process only; not exposed over HTTP today.

Two consequences this RFC builds on:
- The DevHost **drops** `InterfaceTypeFullNames` + `MatchingInterfaceTypeFullNames` when it projects introspection to `ConfigurationOutput.LogicBlockInterface` (`DevHostIntrospection.cs:305-308` keeps only `Identifier` + `Annotations`). The multiplicity token *does* ride through (in `Annotations`); the contract back-ref `MatchingContractType` is already carried.
- A topology's **explicit `interfaceMappings` are applied verbatim** by `DevTopologyLoader.Build` and the wire-up — compatibility is *never* re-checked for authored mappings (only AutoConnect checks it). Closing this is new capability (see §8 decision 1).

## 7. Architecture: server data + client logic

The server exposes *data* and is the authoritative *writer/validator*; the client does the *wiring intelligence* — mirroring how the dashboard already works (cloud-api supplies definitions; Pinia composables do the matching).

```
            ┌────────────────────── DevHost SPA (no-build) ──────────────────────┐
  GET /api/logic-block-definitions ─▶ definitions (catalog, introspection shape)  │
  GET /api/configuration ───────────▶ current instances + wiring                  │
            │                          client computes (frozen contract):         │
            │                            • candidate matching (MatchingInterface…) │
            │                            • cardinality bounds (slotAffordance)      │
            │                            • AutoConnect-preview (arbitration)        │
            │                            • residue (required-unmatched / contested) │
  POST /api/topologies/validate ◀──── draft JSON (authoritative: Parse+Build+compat)
  PUT  /api/topologies/{id} ◀──────── draft JSON (save: validated, confined, gated) │
  POST /api/topologies/{id}/switch ── recycle onto the saved file (EXISTS)          │
            └─────────────────────────────────────────────────────────────────────┘
```

**Server has no matching logic.** It introspects the catalog (reusing `LogicBlockIntrospection`) and persists/validates files. The client ports the dashboard's ~50-line pure helpers (`multiplicity.ts` `slotAffordance` + the `matchingInterfaceTypeFullNames` core). They cannot be imported (TS/build vs no-build vanilla JS) but cannot drift, because the `LinkMultiplicity` tokens are an SDK-owned frozen contract.

## 8. Server surface (Phase 1)

All mirror the existing scenario surface (`ScenarioStore.Save` / `ScenariosController`), so the work is "copy the scenario pattern," not greenfield.

### 8.1 Introspection carry-through + catalog

- Add `interfaceTypeFullNames: string[]` and `matchingInterfaceTypeFullNames: string[]` to `ConfigurationOutput.LogicBlockInterface`; populate them in `DevHostIntrospection.BuildLogicBlock` from `InterfaceInfo.InterfaceTypeFullNames` / `.MatchingInterfaceTypeFullNames`. (Multiplicity annotation + contract `MatchingContractType` already flow.)
- `GET /api/logic-block-definitions` — run `LogicBlockIntrospection` over `DevHostBuilder.GetBlockCatalog()` and return `LogicBlockDefinition[]` (the `LogicBlockIntrospectionResult` shape the dashboard already consumes: `typeFullName`, `interfaces[{identifier, interfaceTypeFullNames, matchingInterfaceTypeFullNames, annotations}]`, `contracts[{identifier, matchingContractType, annotations}]`, `annotations`). The catalog `IReadOnlyList<Type>` must be threaded into DI (it is a throwaway in `DevHostWebRunner` today).

### 8.2 Topology persistence

Extend `DevTopologyStore` with `ReadRaw(id)`, `Save(id, rawJson)`, `TryGetPath` (path-confine), `ExistsExactCase`, and an `IsReadOnly` gate — copied from `ScenarioStore`. New env var **`DALE_DEVHOST_READONLY_TOPOLOGIES`** (none exists today; surface `readOnly` in `GET /api/topologies` and document next to the scenarios gate).

### 8.3 Endpoints (on `TopologiesController`, mirroring `ScenariosController`)

- `GET /api/topologies/{id}` (NEW) → `Content(store.ReadRaw(id), "application/json")`; 404 if absent. Byte-for-byte.
- `PUT /api/topologies/{id}` (NEW) — body = raw `*.topology.json`. → `store.Save`; 200 `{saved, directory}`; 403 on the read-only gate; 422 `{error, errors[]}` on validation failure.
- `POST /api/topologies/validate` (NEW, **id-less**) — body = a draft (possibly un-named). Dry-run, writes nothing. → 200 `{valid:true}` | 422 `{valid:false, errors[]}`. (The client already holds the draft, so the success body is just the pass/fail — no echoed block/service counts.)
- `POST /api/topologies/{id}/switch` (EXISTS) — reused to recycle onto the just-saved file (no watcher needed; discovery is rescan-on-read).

### 8.4 Validation layers (and the resolved decisions)

1. **Structural** — `DevTopologyFile.Parse(json)` (id slug, ≥1 instance, `typeFullName`/`name` required, unique names, no `.` in names, mappings reference declared instances). No catalog, no boot. Collects all errors.
2. **Catalog-bound** — `DevTopologyLoader.Build(file)` (resolve each `typeFullName` against loaded assemblies, `IsAssignableFrom(LogicBlockBase)`, contract-must-exist for explicit `contractMappings`). In-process; **does not boot a host**.
3. **Compatibility (NEW)** — a pass over `interfaceMappings` checking the `matchingInterfaceTypeFullNames` relation (+ the AutoConnect multiplicity rule), reported as structured errors.

**Resolved decisions:**
- (1) **`validate` and `save` run the compatibility check (3) too** — so a hand-edited or imported file cannot smuggle an incompatible mapping. Same frozen relation the client uses; advisory-strength is fine because the runtime tolerates it, but the editor/validate flags it.
- (2) **Route shape:** id-less `POST /api/topologies/validate` (a brand-new draft has no id yet) for validate-as-you-type, plus `PUT` re-validating on save.
- (3) **Save runs the full catalog `Build`** (rejects unloadable types at save), not just structural `Parse`. Save also enforces embedded-`id` == path-`id` (Parse does not).

Type-not-referenced (`ResolveType` returns null → "the DevHost project must reference the library that declares it") must surface as an actionable **per-instance** diagnostic naming the missing `packageId@version`, not a concatenated string.

### 8.5 Topology location, save target, and multi-library

Discovery is a **single resolved directory**, via `DevDataDirectory.Resolve("topologies", null)` (`Vion.Dale.DevHost/DevDataDirectory.cs`; scenarios resolve identically):

- `{cwd}/topologies` wins when it exists (the `dale dev`-from-repo-root posture).
- Otherwise resolution walks **up** the ancestors (max 8 levels), returning the first existing `topologies/`, **bounded by the `.git` repo root** — deliberately **not** by `*.sln`, because nested per-project solutions *below* the data directory are an explicitly-supported mono-repo layout (and it rescues IDE launches where cwd is `bin/Debug/netX.Y`).
- Fallback: `{cwd}/topologies` (possibly non-existent; the UI shows where files would be created). `WithTopologies(path)` overrides verbatim.

`RunFolderDrivenAsync` resolves this **once**; boot/load (`DevTopologyLoader.Load(id, topologiesDir)`) and the switching store share the value, so there is exactly **one** topologies directory at runtime. **The editor saves there**, and surfaces it (`GET /api/topologies` already returns `directory`) so the save target is explicit. There is no multi-location aggregation.

**Multi-library is supported via one DevHost over a unified catalog, not via multiple directories.** `WithDi<T>()` accumulates plugin assemblies (`DevHostBuilder._pluginAssemblies`, deduped) and `GetBlockCatalog()` enumerates `LogicBlockBase` types across all of them — so `.WithDi<LibA>().WithDi<LibB>()` yields a single catalog spanning both. Consequences:

- The block picker (`GET /api/logic-block-definitions`) offers blocks from **every** registered library; a topology in the shared `topologies/` can wire across libraries.
- The canonical layout — top-level `scenarios/` + `topologies/` next to the solution dir, with several library projects a level down (the `logic-block-libraries` shape) — is exactly the `.git`-bounded mono-repo case the resolver targets.

**Out of scope:** multiple *aggregated* topology locations (e.g. a per-library `LibA/topologies/` discovered alongside `LibB/topologies/`) — that would be a multi-root change to `DevDataDirectory` / `DevTopologyStore`, not an editor concern. Noted as a possible future.

## 9. Client surface (Phase 2)

Within the no-build discipline (plain-object Vue + template strings, all I/O via `store.js`, flat files; keymap and palette extended in lockstep with their help).

### 9.1 Placement & navigation

> Refined during build (review round): the initial single `view ⇄ edit` toggle on `TopologyPanel` became a **scenario-style master-detail** surface plus a **header popover**, so switching is discoverable (not only ⌘K) and the `▾` is honest. The shipped shape:

The topology surface is a **master-detail panel** (List → Detail → Editor) driven by a single `store.topologyScreen` (`'list' | 'detail' | 'editor'`), in the spirit of the Verify scenario list → detail. It is reached from a **header popover** on the persistent-shell chip `⛁ <current> ▾` (identical in Explore and Verify): the `▾` opens a `TopologyMenu` with a **switch-to** list (each item recycles immediately onto that topology — the recycle-progress affordance carries the weight), **＋ New topology**, and **Manage / edit…** (opens the List).
- **List** — all topology files (the running one marked `● running`) + ＋New; a row → its Detail.
- **Detail** — read-only blocks/links for the selected file + **⇄ Switch & run** / **✎ Edit** / **⧉ Clone** + ← back.
- **Editor** — `TopologyEditor` (§9.2), reached from Edit / Clone / ＋New; Save returns to the file's Detail (so a freshly-saved topology is immediately re-editable).

Other entry points: the ⌘K palette (`new topology` / `edit topology: <id>`) and **Shift+T**. Never a peer of Explore/Verify; the editing affordances (＋New / Edit / Clone) are gated off on a read-only host (`DALE_DEVHOST_READONLY_TOPOLOGIES`; server 403 backstop). The editor body's wiring list also shows **inline conflicts** (incompatible / over-wired-single-writer) computed client-side, distinct from the residue (unwired) — see §9.4.

### 9.2 Editor body (`TopologyEditor`)

- **Block list** — add an instance from a `definitions` dropdown (the catalog) + a name field; rename/remove. Reuses the draft `EnumSelect`/`TextControl` controls.
- **Wiring list** — every interface endpoint with a **compatible-only candidate picker** (manual wire/unwire/override anywhere), plus an **AutoConnect** button and an **"auto-connect the rest"** that does not clobber manual choices. Candidate set + multiplicity hint computed client-side from `definitions`.
- **Residue panel** — "needs wiring" (required-but-unmatched: `ExactlyOne`/`OneOrMore` with zero counterparts) and "contested" (single-writer matched by many), reusing the watch-panel **tombstone** vocabulary for orphans.
- **Raw-JSON tab** — reuses `JsonEditor`'s Raw pattern verbatim, for hand-edit/paste; the byte-faithful escape hatch (principle 6).

### 9.3 State + actions (`store.js`)

- **Screen state** (the single source of truth for §9.1's master-detail): `topologyScreen` (`'list'|'detail'|'editor'`), `topologySelectedId`, `topologyDetail` (the fetched file shown on Detail). **Navigation actions** set it consistently from every entry: `openTopologyList()`, `openTopologyDetail(id)`, `editTopology(id)` (clone same-id → overwrite), `cloneTopology(id)` (clone + blank id → new file), `newTopology()`, `closeTopologyEditor()` (back to source Detail or List).
- **Draft state**: `topologyDraft` (a parsed topology clone), `topologyDraftDirty`, `topologyDraftErrors`, `definitions` (from `loadDefinitions()`).
- Draft actions: `cloneTopologyDraft(fromId)` / `newTopologyDraft()`, `validateTopologyDraft()` (→ `POST /api/topologies/validate`, renders the server's authoritative error list), `saveTopologyDraft(id)` (→ `PUT …/{id}`, on 200 re-runs `loadTopologies()` and navigates to the saved file's Detail), then the existing `switchTopology(id)` to recycle & run.
- The draft is guarded so a recycle (switch/reset) cannot silently clobber unsaved edits.

### 9.4 Client wiring intelligence (ported, pure)

- `linkMultiplicityOf` / `slotAffordance` (`required` / `multiple` / `capacity`) — ported from `multiplicity.ts`.
- `candidatesFor(endpoint)` — over the instance set, the interfaces **compatible** with the endpoint, where compatibility is the **bidirectional** test `A.matchingInterfaceTypeFullNames ∩ B.interfaceTypeFullNames ≠ ∅` (either direction) — the same relation the server's authoritative `DiscoverMatchingInterfaces` gate uses (the dashboard `useInterfaceMatching` core, made bidirectional for server parity).
- `autoConnectPreview(instances)` — apply the §6 arbitration to fill the unambiguous mappings and classify the residue.

## 10. Authoring flow

1. **Seed** — "Clone & tweak" the current topology (or "New, blank"); the draft is pre-filled from what's running.
2. **Shape the block set** — add instances from the catalog (bounded to referenced types — an unloadable type cannot be picked) + names; rename/remove.
3. **AutoConnect** — one click wires the unambiguous pairs; auto-wired links show in a reviewable list. Manual wiring/override is available on every endpoint at any time.
4. **Resolve the residue** — required-but-unmatched and contested endpoints, each with a compatible-only candidate list + multiplicity hint.
5. **Live-validate** — recycle-free, in-process against the live catalog (structural + catalog + compatibility); inline errors.
6. **Save + run** — `PUT` writes `<id>.topology.json`; "switch & run" recycles onto it (reusing the RFC 0012 host-busy/`{recycling}` progress affordance) and drops into Explore.

## 11. Dashboard export (Phase 3, complementary)

A dependency-free addition to the dashboard; **no DevHost code** on the import side.

- **Pure transform** `toDevTopologyFile(config, definitions)` (new `pages/tenant/logicConfiguration/devTopologyExport.ts`, unit-testable, no store/DOM deps): GUID→instance-name, `definitionId`→`typeFullName` via `definition.name` (cloud-api sets `name === TypeFullName`), pass-through interface/contract identifiers, **drop** `mappedEdgeGatewayId` / `editorLayoutJson` / service GUIDs, slugify `id`, **sanitize** `.` in instance names. Emit **only** the typed keys (strict parse).
- **Button** in `EditorToolbar.vue` (ghost icon by the versions button) → a `downloadDevTopology()` store action → Blob download `<id>.topology.json`.
- **Surface the `{packageId, packageVersion}` set** the config uses (toast/modal): the file only *loads* where the DevHost project references those block libraries.
- **Import** = drop into `topologies/`; folder discovery + the switcher pick it up.
- **One-way.** Reverse (DevHost→dashboard) is import-as-new (no GUIDs/gateways/geometry) and out of scope.

## 12. Scope & phasing

Three independently-shippable phases:

- **Phase 1 — backend (data + persistence).** §8. Useful on its own: offline topology validate + a save endpoint that hand/agent authoring already benefit from. Covered by the headless smoke tier (`devhost-smoke`).
- **Phase 2 — the SPA editor.** §9 + §10. The core UX.
- **Phase 3 — dashboard exporter.** §11. Separate repo; lands in parallel.

## 13. Out of scope (deferred)

- **Contract (HAL / provider-side) wiring authoring** — contracts stay auto-mocked / raw-JSON only in v1. DF-27 provider-side service-provider-contract wiring is a separate concern.
- **`topology` → `logic configuration` rename** (§5).
- **Reverse dashboard import** (DevHost→dashboard).
- **`DELETE /api/topologies/{id}`** — neither scenarios nor topologies expose delete today.
- **A node-graph canvas** — an explicit non-goal (principle 1; RFC 0008 §6.3).

## 14. Risks

- **Preview ≠ Build divergence.** Client AutoConnect-preview and server compatibility check must read the *same* frozen relation (`matchingInterfaceTypeFullNames` + `LinkMultiplicity`). They will not drift if both go through that contract rather than re-deriving a predicate. The server compat check (§8.4) is the backstop.
- **Required-but-unmatched is new semantics.** AutoConnect only guards the >1 case; "required with zero counterparts" (`ExactlyOne`/`OneOrMore`) is a new classification. Misclassifying floods the residue with false warnings — `ZeroOrOne`/`ZeroOrMore` with zero counterparts is *fine*.
- **Advisory, not enforcing.** Multiplicity is declared-not-enforced; the editor must never *reject* a config the runtime accepts (principle 3). Guidance and validation-flags only.
- **Recycle cost.** "Switch & run" is a full host teardown+rebuild (~250 ms port release + actor-system restart, fresh service ids, dropped SignalR clients). It is deferred to an explicit action precisely so the edit/validate inner loop stays recycle-free.
- **Draft survives recycle.** An unsaved `topologyDraft` must be guarded against a mid-edit switch/reset.
- **New write surface = new footgun.** The `PUT`/`validate` endpoints must replicate `ScenarioStore`'s path-confinement, exact-case guard, and the new read-only gate, or risk path traversal.

## 15. References

- Code: `Vion.Dale.DevHost/DevConfigurationBuilder.cs` (matching/arbitration/multiplicity), `Vion.Dale.Sdk/Core/LinkMultiplicity.cs`, `Vion.Dale.DevHost/DevHostBuilder.cs:69` (catalog), `Vion.Dale.DevHost/Control/ConfigurationOutput.cs` + `DevHostIntrospection.cs:305-308` (the dropped fields), `Vion.Dale.DevHost/Topologies/DevTopologyFile.cs` + `DevTopologyLoader.cs` (parse/build), `Vion.Dale.DevHost/Scenarios/ScenarioStore.cs` (the Save pattern), `Vion.Dale.DevHost.Web/Api/Controllers/{Topologies,Scenarios}Controller.cs`, `Vion.Dale.DevHost.Web/wwwroot/{store,components,format}.js`.
- Contract: `vion-contracts/.../Introspection/LogicBlockIntrospectionResult.cs`, `vion-contracts/.../Conventions/LogicBlockWiringConventions.cs`.
- Dashboard: `dashboard/src/pages/tenant/logicConfiguration/editor/composables/useInterfaceMatching.ts`, `dashboard/src/domain/apis/logicBlockDefinition/multiplicity.ts`, `dashboard/.../logicConfiguration/models.ts`.
- Architecture: `architecture/systems/dale.md` (logic configuration), `architecture/systems/cloud-api.md` (LogicBlockDefinitions), `architecture/libraries/dale-sdk.md` + `concepts/logic-block-wiring.md` (the multiplicity vocabulary).
