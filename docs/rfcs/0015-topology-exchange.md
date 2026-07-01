# RFC 0015 — Topology exchange (dashboard ↔ dale-sdk, agent-first debugging)

- **Status:** Proposed — 2026-07-01
- **Author:** jonas.bertsch
- **Related:** RFC 0006 (scenario files — the `topology.json` format), RFC 0013 (DevHost topology authoring; this RFC **completes its §11 "Dashboard export", still pending**), RFC 0008 (unified scenario+topology; deterministic stepping), RFC 0005 (runtime observability). Cross-repo: `dashboard` (Logic Editor ↔ export **and** import), `architecture` (`libraries/dale-sdk.md`, `systems/dashboard.md`).

> This is a design contract, not an implementation. It is the document an implementation plan is generated from. It deliberately stops at the seams (exact store-action names, dialog layout, i18n keys) so the plan can fill them in against current `main` of each repo.

## 1. Summary

Make field-debugging reproducible in an agent-first loop, **bidirectionally**: **export** a running project's / edge gateway's logic configuration from the dashboard as a dale-sdk topology file, consume it in the places a debugging session lands (an agent working on a logic-block library, the DevHost topology editor, the SDK scenario/test flow), **and import** an edited topology back into the dashboard as a new draft config.

The unit of exchange is the **existing `<id>.topology.json`** (RFC 0006 R5), unchanged. Nothing new is embedded in the file. Compatibility information travels as human/agent-readable context alongside it. Export and import are each a **pure transform** in the dashboard; neither is a lossless round-trip (import is import-as-new — §8). The DevHost side needs **no new code** (its editor already accepts pasted JSON); the agent side is a **documented convention**, not tooling.

The one genuinely new obligation this feature creates is a **cross-repo format coupling**: the dashboard now both **produces and consumes** the file, and dale-sdk consumes it strictly. §10 makes that coupling explicit, states its evolution policy, and enforces it with a contract test — because dale-sdk parses the file strictly, so the two repos can drift into silent breakage otherwise.

## 2. Motivation

The RFC 0006 thesis — *"export an installation's logic configuration, run it locally in DevHost with mocks"* — is the local-reproduction story for field debugging. RFC 0013 §11 sketched the dashboard export button but left it pending and scoped to a file download. Three things push it further now:

- **Debugging is increasingly agentic.** The common loop is: something misbehaves in the field → copy the config → hand it to an agent working in the logic-block-library repo → the agent reproduces it locally against mocks and iterates. The natural transport for that hand-off is the **clipboard**, not a downloaded file, and the natural next step is the agent **building a scenario on top** of the exported topology.
- **The format is already the lowest common denominator.** The dashboard's `LogicConfigurationModel` is the same three arrays (`logicBlockInstances` / `interfaceMappings` / `contractMappings`) as `DevTopologyFile`, differing only in identity (GUID vs name) and a few field names. A topology file is the deliberate "dev profile" of the cloud `SetLogicConfigurationPayload`. So export is a transform, not a format negotiation.
- **The coupling is real and currently undocumented.** Once the dashboard emits files that dale-sdk parses, `topology.schema.json` becomes a contract spanning two repos with two release cadences. That needs to be visible to whoever touches either side next.

## 3. The decision (and the alternatives rejected)

**Chosen — reuse the plain `topology.json` verbatim; bidirectional export/import (each an independent, lossy-by-design transform — not a GUID-stable round-trip) with clipboard + download transports; warn-don't-block in both directions; agent flow by convention; DevHost import unchanged; dashboard import reuses the template-instantiation path; coupling governed by a contract test + reciprocal architecture-repo notes.**

Each axis, with the alternative rejected:

- **Format = plain `topology.json`, compat context out-of-band.** *Rejected:* a richer envelope or extra schema fields. The schema is `additionalProperties: false` and shared with the SDK; adding fields forks the contract and buys nothing the file needs. Package/version context is advisory and lives beside the file (§4).
- **Import = warn, don't block.** *Rejected:* a strict gate. The authoritative gate already exists at DevHost load (`DevTopologyLoader.Build`); a second hard gate at import time only adds friction and duplicates catalog knowledge. *Also rejected:* informational-only, which throws away the actionable "this block isn't in your project" signal we can cheaply compute.
- **Bidirectional, but import-as-new (not a lossless round-trip).** *Rejected:* preserving instance GUIDs / gateway binding / editor geometry to reconstruct the *exact* original config. Each direction is an independent transform: export drops deployment/geometry; import (Flow D) regenerates GUIDs, asks for a target gateway, and lets the editor auto-lay-out. There is no live sync and no GUID-stable round-trip — but reverse import *as a new draft* is cheap and valuable, so it is now **in** scope (it was RFC 0013's deferral).
- **Dashboard import = reuse the template-instantiation path.** *Rejected:* a new server endpoint. The dashboard already mints instance/service GUIDs client-side and instantiates a whole graph from a saved template; import sources that same operation from pasted JSON, resolving `typeFullName`→latest definition. Pure client-side + the existing create/save path (pending R4).
- **Agent flow = convention + docs.** *Rejected:* a `dale` CLI verb (import-from-clipboard + scaffold scenario). The agent already writes files; a documented file convention plus the existing DevHost validation is enough. A verb can come later if the convention proves fiddly (§13).
- **DevHost import = nothing new.** *Rejected:* a bespoke "import from clipboard" affordance. The topology editor's **Raw tab already** parses pasted JSON into the draft, then validates and saves. A human who wants the exported JSON in the editor pastes it there; files arrive via folder-discovery. No third path.

## 4. The exchange artifact

### 4.1 The file — unchanged

The unit of exchange is exactly the RFC 0006 topology file: `Vion.Dale.DevHost/Topologies/topology.schema.json` (`$id: https://vion.swiss/dale/topology.schema.json`), strict (`additionalProperties: false`, required `["id", "logicBlockInstances"]`), parsed by `DevTopologyFile` with `JsonUnmappedMemberHandling.Disallow`. No new fields, no version key, no envelope.

### 4.2 The export transform (dashboard side)

A pure, unit-testable function `toDevTopologyFile(config, definitions)` — the file RFC 0013 §11 named, `src/pages/tenant/logicConfiguration/devTopologyExport.ts` (new). Verified field-level mapping (dashboard models on the left are confirmed against `src/domain/apis/logicConfiguration/models.ts` and `logicBlockDefinition/models.ts`):

| Topology field | Source | Rule |
|---|---|---|
| `id` | `LogicConfigurationModel.name` | slugify to `^[A-Za-z0-9][A-Za-z0-9._-]*$` |
| `logicBlockInstances[].typeFullName` | `LogicBlockDefinitionModel.typeFullName ?? .name` (looked up by `instance.logicBlockDefinitionId`) | cloud-api's tenant DTO already returns `TypeFullName` (= CLR full name); the plan captures that field on the dashboard model and prefers it, falling back to `name` (equal today). R1 resolved by Phase 0. |
| `logicBlockInstances[].name` | `LogicBlockInstanceModel.name` | sanitize `.` → `_` (scenario name-paths split on `.`); dedupe post-sanitization collisions |
| `interfaceMappings[]` | `InterfaceMappingModel` | resolve GUID→name **and** rename: `logicBlockInstanceId` → `sourceLogicBlockName`, `interfaceIdentifier` → `sourceInterfaceIdentifier`, `mappedLogicBlockInstanceId` → `targetLogicBlockName`, `mappedInterfaceIdentifier` → `targetInterfaceIdentifier` |
| `contractMappings[]` | `ContractMappingModel` | resolve GUID→name for `logicBlockName`; pass `mappedServiceProviderIdentifier` / `mappedServiceIdentifier` / `mappedContractIdentifier` through; **drop** `mappedEdgeGatewayId` |
| *(dropped)* | `edgeGatewayId`, `editorLayoutJson`, service GUIDs | absent from the dev profile; **regenerated on dashboard import** (§8) |

The transform builds a GUID→instance-name table first, then rewrites every mapping through it. Pure: no store, no DOM, no network — so it is directly the contract-test subject (§10).

### 4.3 The compatibility note — out of band

Alongside the file, the exporter surfaces the set of `{ packageId, packageVersion }` for every library the config uses (both live on `LogicBlockDefinitionModel`, already carried into the page's `LogicBlockInstanceViewModel`). This is **advisory context**, never written into the strict file: it tells a human/agent *which block libraries and versions the DevHost project must reference* for the topology to load. It is rendered in the export dialog and can be copied with the JSON.

## 5. Flow A — Dashboard export

**Trigger.** A `VionIconButtonGhost` (export icon) in `EditorToolbar.vue`'s `editor-toolbar__actions` group, left of the versions button (`fileHistoryIcon`), emitting `export`. Available in **both** toolbar modes — `readonly` (export the deployed/active config: the field-debugging case) and editing (export a draft). Export is a pure read of whatever graph is loaded, so it is not gated behind edit mode.

**Surface.** The button opens a `VionDialog` that shows the generated JSON (read-only, monospace) with the §4.3 compat note above it, and two actions off the identical string:

- **Copy to clipboard** (primary) — for pasting into an agent chat or the DevHost Raw tab. (`navigator.clipboard.writeText`; there is codebase precedent in the DevHost's verification-report copy.)
- **Download `<id>.topology.json`** — for dropping into a DevHost project's `topologies/` (folder-discovery import).

The transform and both transports are driven by a store action (`useLogicConfigurationPageStore`); the page stays a thin orchestrator. Export warnings (name collisions after sanitization, an instance whose definition can't be resolved to a name) are shown in the dialog but do **not** block copy/download — the authoritative gate is DevHost load.

## 6. Flow B — Agent workflow (convention + docs)

No new tooling. A documented convention added to the library template's existing agent docs, `templates/vion-iot-library/{CLAUDE.md,AGENTS.md}`:

> **Given a topology JSON** (e.g. pasted from the dashboard export), save it to `topologies/<id>.topology.json` where `<id>` is the JSON's `id` field. **To build a scenario on it,** create `scenarios/<id>-<case>.scenario.json` with `"topology": "<id>"` and author `setup` / `steps` / `watch` (RFC 0006 vocabulary). **To validate,** run the DevHost (or the xunit `[ScenarioFiles]` lane): an *"type … is not loadable"* error means your project doesn't reference the block's library — add the `PackageReference` named in the export's compatibility note.

That is the entire "paste → agent saves it in the right place → agent builds a scenario on top" loop, riding on file conventions the SDK already enforces and validation that already exists.

## 7. Flow C — DevHost / SDK editor import (nothing new)

The DevHost topology editor already supports the "create a topology from pasted JSON, as if clicked together" case:

- `TopologyEditor` (`Vion.Dale.DevHost.Web/wwwroot/components.js`) has a **Form ⇄ Raw** toggle; the Raw tab is a textarea (placeholder *"paste / type JSON"*) whose commit does `store.topologyDraft = JSON.parse(rawText)`.
- Save → `PUT /api/topologies/{id}` runs the full structural + catalog + compatibility validation and returns structured `422` errors; `POST /api/topologies/validate` validates without writing; read-only is gated by `DALE_DEVHOST_READONLY_TOPOLOGIES`.

So the human path is: **New topology → Raw tab → paste → commit → validate → save.** Files (rather than clipboard) arrive via folder-discovery — drop `<id>.topology.json` into `topologies/`. No in-editor file-upload widget: that would be a redundant third path.

## 8. Flow D — Dashboard import (reverse transform)

The reverse direction is valuable and cheap, because the dashboard **already does its shape** under another name: adding a block mints `crypto.randomUUID()` ids and synthesizes services from the definition (`store.ts` ~727-731), and **template instantiation** takes a pre-built graph, generates fresh GUIDs, remaps every mapping, and merges layout (`store.ts` ~900-916). Importing a topology is that same operation, sourced from pasted JSON.

**Import is import-as-new, client-side, warn-don't-block.** A pure `fromDevTopologyFile(topology, catalog, targetGatewayId)` (new `src/pages/tenant/logicConfiguration/devTopologyImport.ts`) builds a draft `LogicConfigurationModel`, handed to the **existing** `createLogicConfiguration` path (`api.ts:18`). No new server endpoint (pending R4).

Verified field-level mapping — the mirror of §4.2:

| Dashboard field | From topology | Rule |
|---|---|---|
| instance `id` | — | `crypto.randomUUID()` per instance; build a **name→newGUID** table |
| `logicBlockDefinitionId` | `typeFullName` | resolve to the **latest** definition where `definition.name === typeFullName` in the tenant's accessible catalog (`isLatestVersion`); unresolvable → warn + skip that instance |
| instance `name` | `name` | verbatim (sanitized dots are not restored — names are labels) |
| instance `services[]` | — | synthesize from the resolved `definition.services` (fresh `serviceId` each) — identical to adding a block |
| `interfaceMappings[]` | `source*` / `target*` | name→GUID + rename to `logicBlockInstanceId` / `interfaceIdentifier` / `mapped*`; drop mappings whose endpoints didn't resolve |
| `contractMappings[]` | `logicBlockName` + `mapped*` | name→GUID; `mappedEdgeGatewayId` ← target gateway; warn if provider/service/contract identifiers don't resolve against that gateway |
| `edgeGatewayId` | *(absent)* | ← the user-chosen **target gateway** (gap 1 below) |
| `editorLayoutJson` | *(absent)* | `null` → the editor auto-lays-out (ELK). This is what makes an imported graph look "clicked together". |

**Surface.** An "Import topology (JSON / clipboard)" action in the logic-configuration create flow, mirroring `AddTemplateDialog.vue`: paste or read the clipboard → the dialog shows resolution results (blocks matched to definitions + versions, unresolved types, dropped mappings) → pick a target edge gateway → create-as-new-draft → land in the editor (auto-laid-out) for review before activation.

**The three semantic gaps (all warn-don't-block, mirroring export):**

1. **Edge gateway.** The file has none; the instance requires one. Convention: the import dialog picks **one target gateway** for all instances. Multi-gateway distribution can't be recovered (export flattened it); the user reassigns per-block in the editor afterward if needed (R5).
2. **Contract / provider bindings.** Dev exports often have empty `contractMappings` (auto-mocked), so usually moot. When present, default `mappedEdgeGatewayId` to the target and **warn** on identifiers that don't resolve against that gateway's service providers (reuses the store's existing "required unfilled bindings").
3. **Version drift / foreign library.** If the latest definition no longer declares a named interface/contract, or the `typeFullName` isn't in the accessible catalog at all → **warn, import the rest, flag the missing** (the inverse of the export compat note: "you don't have library X@Y"). The editor's existing compatibility validation (`useInterfaceMatching`) then vets the imported wiring for free.

## 9. Compatibility — the "small things to consider" (warn, don't block)

Because the file carries no version metadata, checks are layered. Export-time is structural + the package-set note; import-time is catalog resolution + the interface/contract validation that already exists. Nothing blocks *loading into an editor* — you can paste, read the warnings, and fix; **running** (DevHost `Build` / switch) is the authoritative gate.

Flow D (dashboard import) applies the same stance from the other side: resolution runs against the tenant's **definition catalog** (not an assembly scan), and unresolved types / drifted mappings are surfaced in the import dialog while the rest imports — specifics in §8.

| Concern (your import checklist) | Where detected | Severity | Handling |
|---|---|---|---|
| Block from a library the DevHost project doesn't reference (unresolvable `typeFullName`) | `DevTopologyLoader.Build` / `/validate` (AppDomain assembly scan) | **warn → must-fix-to-run** | list the missing type(s) + the `{packageId, packageVersion}` from the export note; agent adds the reference |
| Version drift (interface/contract reshaped across package versions) | export-time note vs. the project's referenced versions | **warn** | best-effort compare; **cannot** be seen in-file (no versions) — this is why the note exists |
| Interface incompatibility (an explicit mapping fails the frozen `MatchingInterface` relation) | `DevConfigurationBuilder.InterfacesMatch` | error | all incompatible pairs collected, reported at once |
| Missing / renamed contract | `DevTopologyLoader` | error | "block X has no contract Y" |
| `typeFullName` drift (`definition.name` ≠ CLR type) | manifests as an unresolvable block | warn | same handling as row 1 |
| id / instance-name collisions; dots in instance names | export-time transform + editor save | warn | sanitize + dedupe; prompt on `<id>` file clash |
| Dropped gateway binding / geometry / service GUIDs | n/a (expected) | info | absent in the file; **regenerated** on dashboard import (§8) — user picks a target gateway, GUIDs minted, layout auto-generated |

## 10. The cross-repo contract and its evolution policy

This is the load-bearing section. Once the dashboard emits files dale-sdk parses, `topology.schema.json` is a **shared contract across two repos with independent release cadences.**

Both sides now play **both roles** — the dashboard exports *and* imports (Flow D); dale-sdk's DevHost consumes *and* authors topology files. The asymmetry below is about **dale-sdk's strict parse specifically**; the dashboard as a consumer parses leniently in TypeScript and ignores fields it doesn't model, so it imposes no ordering constraint. The evolution policy is therefore driven entirely by dale-sdk's parser.

**The coupling is asymmetric, because dale-sdk parses strictly** (`additionalProperties: false` + `JsonUnmappedMemberHandling.Disallow`). That dictates the only safe evolution order:

- The **producer (dashboard) can never lead.** If the dashboard emits a field dale-sdk doesn't yet know, strict parse **rejects the whole file**.
- Adding an **optional** field to dale-sdk (schema + `DevTopologyFile`) is backward-compatible: older dashboard exports that omit it still load.
- Adding a **required** field is a **breaking change**: every existing exported file fails to load. It must be released in lockstep across both repos.

**Policy (one sentence):** *format changes land in dale-sdk first as optional, the dashboard starts emitting them second; new required fields are breaking and released in lockstep.*

**Ownership.** dale-sdk is the canonical owner of `topology.schema.json` and `DevTopologyFile`. The dashboard is a **conformer**.

**Enforcement (not just docs — docs rot).** A dashboard-side **contract test** validates `toDevTopologyFile(...)` output against a vendored copy of dale-sdk's `topology.schema.json`. The day either side drifts, dashboard CI goes red. (Vendoring vs. fetching the schema, and how the vendored copy is refreshed, is a plan-level detail; vendoring keeps CI hermetic.)

**Discoverability (architecture repo).** Reciprocal shared-contract notes so an engineer in *either* repo — or in neither — finds the coupling:

- `architecture/libraries/dale-sdk.md` — a note (Consumers table row and/or a Key-invariant bullet) that `topology.json` is a shared contract also **produced and consumed** by the dashboard's Logic Configuration export/import; link to `systems/dashboard.md` and this RFC; state the strict-parse evolution order.
- `architecture/systems/dashboard.md` — a note (a bullet under Key invariants, or a short "Topology exchange (dev/debug)" subsection) that the Logic Editor **exports and imports** dale-sdk topology files (export the active config for local reproduction; import a topology as a new draft); link to `libraries/dale-sdk.md` and this RFC; state that the format is owned there.

## 11. Future extensions (hooks, not built here)

All ride on the plain file **by reference**, not by embedding — which is why the format stays untouched:

- **Scenario base.** An exported topology's `id` is directly what a `*.scenario.json` names (`"topology": "<id>"`). Flow B is the first consumer.
- **Scenario recording.** The deferred `Recorder` (RFC 0006 §7 — an `IDevHostControl` decorator that captures a run as a sibling `*.scenario.json`) runs against a topology loaded from the exchange file; no format change.
- **Trace / debug capture.** `ScenarioRunReport` + `WatchTrace` already carry per-step samples; on a stepped host (RFC 0008) `VirtualElapsedMs` is bit-reproducible — *only if the topology is identical*.
- **Topology identity pin (the one real gap).** Scenarios pin their own `FileHash` but **not** the topology's. For "export → hand to agent → regression-diff" to be tamper-evident across topology edits, a future revision could have a scenario / run-report reference a topology hash. This is a dale-sdk-side scenario-format change (its own RFC amendment) and is **out of scope here**, but named so it isn't lost.

## 12. Scope & phasing

- **Phase 0 — verify the invariants (R1, R4).** Confirm the cloud-api guarantee `definition.name === TypeFullName` (or add an explicit `typeFullName`), and that cloud-api `create` accepts a client-assembled config (the normal editor save path). Gates the export transform (R1) and Flow D (R4).
- **Phase 1 — dashboard export.** `toDevTopologyFile` + its unit tests; the export dialog (copy + download + compat note); the toolbar button; i18n keys.
- **Phase 2 — the contract test.** Vendor `topology.schema.json` into the dashboard; assert `toDevTopologyFile` output validates against it.
- **Phase 3 — docs & governance.** The `vion-iot-library` template convention (Flow B); the reciprocal architecture-repo notes (§10).
- **Phase 4 — dashboard import (Flow D).** `fromDevTopologyFile` + unit tests; the import dialog (paste/clipboard, resolution results, target-gateway picker) mirroring `AddTemplateDialog.vue`; feed the existing `createLogicConfiguration` path. The symmetric partner to Phase 1.

Phases 1–4 are independent enough to land in any order after Phase 0. There is **no dale-sdk *code* change** in this RFC — Flow C already works; the only dale-sdk edits are the `vion-iot-library` template doc notes (Phase 3). The other artifacts land in `dashboard` (Phases 1, 2, 4) and `architecture` (Phase 3).

## 13. Out of scope (deferred)

- **Lossless / GUID-stable round-trip.** Import is import-as-new (fresh GUIDs, user-chosen gateway, auto-layout); it does not reconstruct original instance ids, multi-gateway distribution, or editor geometry. Live/continuous sync between a running config and a topology file is also out.
- A `dale` CLI import/scaffold verb (revisit if the Flow B convention proves fiddly).
- Per-version export from the dashboard Versions panel (only the loaded config exports in v1).
- In-file version/compat metadata and the scenario→topology hash pin (§11).
- Any change to `topology.schema.json` itself.

## 14. Risks

- **R1 — `definition.name === TypeFullName` is load-bearing and unbacked.** `LogicBlockDefinitionModel` has no `typeFullName` field; the transform depends entirely on this cloud-api naming convention. If a definition's `name` ever diverges from its CLR type (rename, display-name drift), the export writes an unresolvable `typeFullName` with **no export-time error** — it surfaces only as an unresolvable block at DevHost load. *Mitigation:* Phase 0 confirms the invariant or adds an explicit field. (Import (Flow D) uses the inverse lookup — `typeFullName`→definition — so it shares this dependency.) **Resolved (Phase 0 — verified in SDK `LogicBlockIntrospection.cs`, `vion-contracts`, cloud-api `LogicBlockDefinitionsController`, dashboard):** `name === TypeFullName === CLR Type.FullName` end-to-end; the `[LogicBlock(Name=…)]` display name flows to `annotations.DefaultName`, never `TypeFullName`. cloud-api's tenant DTO already exposes `TypeFullName`, so the plan captures it on the dashboard model and prefers it over `name` — eliminating the fragility.
- **R2 — schema drift between repos.** Addressed by §10 (contract test + policy + reciprocal notes); the risk is that the test is skipped. It is a Phase-2 deliverable, not optional.
- **R3 — clipboard as a debugging channel is lossy for large configs.** Very large topologies produce large clipboard payloads; the download transport is the fallback. No size cap is imposed, but the dialog shows the file so the user sees what they're sending.
- **R4 — cloud-api `create` must accept a client-assembled graph.** Flow D posts a fully client-built `LogicConfigurationModel` (client GUIDs, all mappings) to the existing create endpoint — the normal editor save path, so it should hold, but the plan must confirm no server-side validation rejects a hand-assembled import. If it does, Flow D needs a thin server accommodation — the one place "completely client-side" could break. **Resolved (Phase 0):** the create handler regenerates only the top-level config id and persists instance/service GUIDs + mappings verbatim, so a client-assembled config is accepted. Caveat: server-side referential validation is currently a TODO in cloud-api, so `fromDevTopologyFile` must guarantee integrity itself (only emit mappings whose endpoints resolve; only instances whose definitions exist) — which it does.
- **R5 — import flattens multi-gateway configs.** A config spanning several edge gateways exports to a single gateway-less topology; on import it lands on one chosen gateway. Inherent to the dev profile; surfaced as an import note and resolved by per-block reassignment in the editor.

## 15. References

- RFC 0006 (scenario files; topology-file format, §"Topology files").
- RFC 0013 §11 (dashboard export, Phase 3 — completed by this RFC).
- RFC 0008 (deterministic stepping; `VirtualElapsedMs` reproducibility).
- Schema: `Vion.Dale.DevHost/Topologies/topology.schema.json`; model: `DevTopologyFile.cs`; loader/validator: `DevTopologyLoader.cs`, `DevConfigurationBuilder.cs`.
- Dashboard: `src/domain/apis/logicConfiguration/{models.ts,api.ts}`, `src/domain/apis/logicBlockDefinition/models.ts`, `src/pages/tenant/logicConfiguration/store.ts` (template instantiation ~900-916, block-add ~727-731), `.../editor/EditorToolbar.vue`, `.../editor/AddTemplateDialog.vue`; export/import targets (new) `.../devTopologyExport.ts` + `.../devTopologyImport.ts`.
- DevHost editor: `Vion.Dale.DevHost.Web/wwwroot/{components.js (TopologyEditor),store.js}`; API `TopologiesController`.
- Architecture: `libraries/dale-sdk.md`, `systems/dashboard.md`.
