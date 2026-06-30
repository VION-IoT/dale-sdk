# RFC 0014 — DevHost scenario authoring (form-based, live-coupled)

- **Status:** Proposed
- **Date:** 2026-06-26
- **Author:** jonas.bertsch
- **Related:** RFC 0006 (scenario files / Player), RFC 0008 (unified scenario+topology data model), RFC 0012 (DevHost UI observe/drive model), **RFC 0013 (DevHost topology authoring — the sibling this mirrors)**, RFC 0010 (serviceProvider contracts + struct values).

## 1. Summary

Author scenarios — `setup` / `steps` / `watch` / `judge` — **interactively in the DevHost UI** instead of hand-editing `*.scenario.json` and reloading. A **form-based** editor, **live-coupled** to the running host, that mirrors RFC 0013's topology editor in placement (a master-detail inside Verify), row/picker vocabulary, and draft/save flow.

The decisive difference from RFC 0013: **the backend already exists.** Scenarios already have `GET list/schema/{id}/{id}/run`, `POST {id}/apply` (run, recycle-on-run), `PUT {id}` (validated Save), and `ScenarioStore.Save`/`ReadRaw`/`List`; the Player already parses and renders the steps read-only (`fileSteps`); and the runner's `ScenarioResolver` already resolves struct-field property paths. So this RFC is **almost entirely the SPA** — making that read-only step view editable.

## 2. Scoping verdict (the priorities that frame this)

- **Primary:** a **form-list step editor** (precise, deliberate composition) and **assertion correctness** — capturing the *real* live value for an assert instead of guess-run-paste.
- **Secondary / deferred:** **capture-from-driving** (a record mode — "use current value" is its first sip), **run-to-step snapshot** (sequence-accurate assert capture), **run/iterate loop polish**.

## 3. Current flow & friction

**Today:** hand-write `scenarios/<id>.scenario.json` (a `topology` to run against, `setup` initial drives, `steps` with the closed step vocabulary, `watch`, `judge`); lean on `$schema` autocomplete (shape only) + `dale scenario validate`; open in the **Player** (Verify) → **Run** → trace + judge; iterate by editing the JSON (hot-reloads).

**Friction (the "mappings-first" analog):**
- You hand-type a **sequence** with a rich step vocabulary → easy to get a step's shape wrong.
- `Block.Property` / `logicBlock`+`contract` paths are typed blind — the schema validates *shape*, not whether the path exists on the running topology.
- `expect` / `waitUntil` values are **guessed** (run it, read the number, paste it back).
- No in-UI authoring — the Player runs and views but never edits.

## 4. Principles (mostly inherited from RFC 0013)

1. **Live-coupled — the running host is the single source of truth.** The editor authors against whatever topology is loaded; block/property/contract names come from `store.config`, live values from `store.values`. Authoring a scenario for a different topology recycles onto it (decision below).
2. **Static recipe — author, then run.** Composing steps does **not** mutate the host; "use current value" reads the live host at click time but changes nothing. `Save → Run` (the existing `apply`, recycle + replay) executes the recipe. Clean author/run split.
3. **Reuse, don't reinvent.** Same master-detail-in-Verify, same row / picker / raw-JSON-tab vocabulary as the topology editor; same draft-state and validated-save flow.
4. **Schema-aware pickers and value editing.** Writable-filtered `set` pickers; struct-field-drilling assert pickers; a type-driven value editor (scalars, enum, struct, array-of-struct).
5. **One step vocabulary in the SPA.** The editor and the Player's existing `fileSteps` rendering share a single source of the kinds and their shapes — they cannot drift.

## 5. Terminology

- **Scenario** — the dev-time `*.scenario.json`: a `topology` + `setup` + `steps` + `watch` + `judge` (RFC 0006). Unchanged.
- **setup** — the **arrange** phase: drive-shapes only (`set` / `serviceProviderSet`), runs *before* `steps`, reported as its own group. Distinct from `steps` (act + assert).
- **The seven step kinds** (`ScenarioStep`, exactly one per step, each with optional `label` + `spec`):
  `set` (`"Block.Property"` + `value`) · `serviceProviderSet` (`{logicBlock, contract}` + `value`) · `serviceProviderExpect` (`{logicBlock, contract, equals, tolerance?}`) · `waitUntil` (`{property, equals}` + `timeoutSeconds`) · `expect` (`{property, equals|above|below, tolerance?}`) · `advance` (`{seconds}`) · `settle` (budget). The **schema / `ScenarioStep` model stays the source of truth** for exact fields — no new kinds (so the four cross-repo vocabulary sites are untouched).

## 6. Server surface (≈ unchanged)

Everything needed already ships:

| Endpoint | Role | Status |
|---|---|---|
| `GET /api/scenarios` | list (+ `readOnly`) | EXISTS |
| `GET /api/scenarios/schema` | editor schema | EXISTS |
| `GET /api/scenarios/{id}` | raw file (byte-for-byte) | EXISTS |
| `GET /api/scenarios/{id}/run` | latest run report | EXISTS |
| `POST /api/scenarios/{id}/apply` | run (recycle-on-run) | EXISTS |
| `PUT /api/scenarios/{id}` | validated Save (+ read-only gate, id-mismatch guard — both tested) | EXISTS |

- `ScenarioResolver` **already resolves struct-field paths** (`ResolvedProperty.FieldPath` descends into a struct member's scalar; `Block.Property.Field` is a valid `expect`/`waitUntil`/`set` path) — so struct-field asserts need **no** runner change.
- **No new endpoint.** Unlike RFC 0013 (which needed the server-authoritative interface-compat check), scenario validation is shape + path-existence + per-kind structural rules — all of which the client can mirror from the live `store.config`; the `PUT` Save is the authoritative gate. A server-authoritative `POST /api/scenarios/validate` (validate-without-write) is **deferred** unless a need surfaces.

## 7. Client surface (Phase 2 — the work)

Within the no-build discipline (plain-object Vue + `template:` strings, all I/O via `store.js`, flat files, keymap/palette extended in lockstep).

### 7.1 Placement & navigation

Extend **Verify** into a master-detail, identical to the topology panel. Verify is already list→detail (open a scenario), so:
- **List** (no scenario open) — discovered scenarios + **＋ new**.
- **Detail** (scenario open) — today's read-only step view + Run/trace, plus **✎ Edit** and **⧉ Clone**.
- **Editor** — the new form-list editor; **Save** → back to Detail, **Run** from Detail.

The scenario's `topology` is **locked to the running topology** (principle 1). **＋new** uses the running topology directly; **Edit / Clone of a scenario whose `topology` differs from the running one first auto-recycles the host onto that topology** (reusing the RFC 0013 switch path), so the editor's pickers and live values are always for the right rig (decision Q3). Editing/cloning is gated off on a read-only host (the existing `DALE_DEVHOST_READONLY_TOPOLOGIES`-sibling scenario gate). Reuses the `#/scenario` deep link; ⌘K palette gains `new scenario` / `edit scenario: <id>` verbs and a keybinding, in lockstep with the help.

### 7.2 Editor body (form-list)

**Four sections, each a list with a uniform affordance set — insert at any position, ↑/↓ reorder, remove** (no drag-and-drop: it would need a library the no-build rules out and native HTML5 drag is poor for accessibility; arrow-moves are dependency-free and keyboard-friendly, with native drag a possible later enhancement):

- **setup** — `set` / `serviceProviderSet` rows only (drive-shapes).
- **steps** — full vocabulary: each row = a **kind dropdown** + the per-kind fields + a **label** (+ optional `spec` tag).
- **watch** — `Block.Property` picker rows.
- **judge** — free-text prompt rows.

Per-kind field forms map 1:1 to §5; they reuse the topology editor's compact-row controls. A **raw-JSON tab** (the topology editor's pattern) is the byte-faithful escape hatch.

### 7.3 Schema-aware pickers + type-driven value editor

Pickers draw from the live `store.config`:

- **Property pickers** flatten each block's service-properties + measuring-points to `Block.Property` (and `Block.Service.Property` when ambiguous), **de-duplicated by name** — a member that is *both* `[ServiceProperty]` and `[ServiceMeasuringPoint]` (the dual-annotation gotcha, gated as two streams server-side per #104) appears **once** in the picker. For **`set` / setup** they are **filtered to writable** (`format.js`'s existing `isWritable`, driven by `schema.readOnly`) — read-only props and measuring points are excluded, matching the server's read-only-write 400. For **`expect` / `waitUntil` / `watch`** all observable members are offered, and **struct-typed members drill into their fields** (block → property → field), emitting `Block.Property.Field`.
- **Contract pickers** (`serviceProviderSet` / `serviceProviderExpect`) enumerate the blocks' `[ServiceProviderContractType]` members → `{logicBlock, contract}`.
- **Value editor — type-driven from the picked member's schema:** scalars / enum / bool / duration reuse the existing writable controls; **struct → a nested per-field form (honoring nullable/optional fields — the value editor must let a nullable struct field be null/omitted, now that #105 emits them as nullable+optional); array (incl. array-of-struct) → add/remove element rows**, each element edited per its element schema. Seeded from the schema→sample-value template `format.js` already builds.
- **Contract values have no schema in the config — a UI-only stopgap (decision Q1).** Service *properties* carry a full value `Schema`, but service-provider *contracts* carry only a type **token** (`ContractType`/`MatchingContractType`). So the value editor is **schema-presence-gated**: scalar contract families (DI/DO → bool, AI/AO → number) are form-driven by convention; a **non-scalar (struct/array) contract value falls back to a raw-JSON editor**. This special-casing lives **entirely in the SPA value-editor component — no C#/model changes.** It is written so that *if/when the introspection later exposes a value schema for service-provider contracts generally (for all SP messages), the same type-driven path lights up automatically and the raw-JSON fallback simply stops triggering* — the stopgap retires itself with no further UI work.

### 7.4 Assertion correctness ("use current value")

Every `expect` / `waitUntil` / `serviceProviderExpect` row carries a **"use current value"** button that fills `equals` from the **live `store.values`** for the picked property/contract (navigating into the leaf for struct-field paths). Per principle 2 this reads the host's *current* state.

- **Clock-mode hint (decision Q2).** A captured value is only *reproducible* on a **stepped** host (at a known virtual tick); on a real-clock host (the DevHost default) values move continuously and `advance`/`settle` wait wall-clock. The editor surfaces a one-line hint when capturing on a real-clock host ("values are live — switch to stepped for reproducible captures"), but does **not** hard-require stepped.
- **Reaching the right state (decision Q5).** For sequence-dependent asserts the author drives/advances the host to the intended point, then captures — acceptable for v1. A cheap **"apply setup"** button (runs just the scenario's `setup`) gives a sane starting state without a full run; the deferred run-to-step snapshot automates the rest.
- **`expect` form scope (decision Q4):** the form ships the **`equals`** comparator for v1 (what "use current value" fills). `above`/`below` against a literal are accepted by the runner and authorable via the **raw-JSON tab**; dedicated `above`/`below` form controls are a small follow-up (§11). The compare-against-another-property *comparand* variant the resolver supports is **deferred**.

### 7.5 State & actions (`store.js`)

Mirror the topology draft pattern:
- **Screen + draft state:** an editor screen flag layered on the existing `scenarioId` nav; `scenarioDraft` (a parsed clone), `scenarioDraftDirty`, `scenarioDraftErrors`.
- **Actions:** `newScenarioDraft()` / `cloneScenarioDraft(id)` / `editScenario(id)`; `saveScenarioDraft()` → the existing `PUT /api/scenarios/{id}` (on 200, back to Detail); then the existing `applyScenario(id)` to run. The draft is guarded so a recycle cannot silently clobber unsaved edits.

### 7.6 Pure policy (`scenario-forms.js`)

A pure module (the `wiring.js` analog): the **step-kind catalog** (kinds → their field specs), **per-kind structural validation** (mirroring the server's `StructuralErrors`, incl. setup = drive-only), **value coercion**, and **path/contract/struct-field enumeration** from a config. No DOM / Vue / store / fetch — unit-testable by reading. **Unify it with the Player's existing `fileSteps`** so rendering and editing share one vocabulary (principle 5).

### 7.7 Bundled: Explore watch (pins) reorder

A small adjacent enhancement requested alongside: the live **`WatchPanel`** (Explore pins) gets the same **↑/↓ reorder**, so both the Explore pins and the scenario `watch` are reorderable with one vocabulary.

## 8. Authoring flow

1. **Pick the rig** — with the host on the target topology, open Verify → **＋ new** (or **Clone** an existing scenario, or **Edit** the open one). `topology` is pre-filled from the running host.
2. **Arrange** — add `setup` `set` rows (writable pickers + type-driven value editor).
3. **Compose steps** — insert/append/reorder steps from the kind dropdown; fill per-kind fields with live pickers; drill into struct fields for asserts.
4. **Make asserts true** — drive/advance the host as needed, then **"use current value"** to snapshot the real value into each assert.
5. **Watch / judge** — pick watch paths (reorderable), write judge prompts.
6. **Validate inline** (client-side, against `store.config`) → **Save** (`PUT`, authoritative) → land on Detail → **Run** (`apply`) → trace + judge.

## 9. Validation

- **Client-side (inline, advisory-fast):** per-kind shape (via `scenario-forms.js`) + path/contract existence against `store.config` + setup-is-drive-only + writable-for-`set`. Surfaced as inline row errors, the topology editor's vocabulary.
- **Server-side (authoritative):** the existing `PUT` Save runs the full `ScenarioFile` structural validation + resolver bind; rejects on save. (Same relation the client mirrors, so the two agree.)

## 10. Testing

- **Tier 1 (headless smoke):** an **author→save→apply→`succeeded`** round-trip over `PUT` + `apply` (the read-only / id-mismatch guards are already covered on the scenario side).
- **Tier 2 (live UI, chrome-devtools):** a devhost-smoke editor-flow checklist mirroring the topology one — ＋new → a setup `set` + a couple of steps + an `expect` filled via **"use current value"** (incl. a struct-field assert and a struct/array value set on `ShowcaseBlock`) → Save → Run → green. Tear down the authored file. The merged **`Vion.Examples.Emission`** example (`SensorBlock` + a `ThreePhase` l1/l2/l3 struct + a committed `emission.scenario.json`) is a second realistic struct test bed — useful for exercising the struct-field assert + struct value editor against a real example (and a live poke target).
- **Unit:** pure `scenario-forms.js` tests (kind field specs, per-kind validation incl. setup-only, struct-field path enumeration, type-driven value coercion).

## 11. Out of scope / phasing / follow-ups

- **Capture-from-driving (record mode)** — the natural follow-up once the form editor exists; capture live `set`/`advance`/assert actions in Explore into steps. "use current value" is its first sip.
- **Run-to-step snapshot** — recycle + replay prior steps to snapshot a sequence-accurate assert value at the exact point.
- **Run/iterate loop polish** — re-run from a step, richer inline failure surfacing beyond the trace.
- **`expect` `above`/`below` form controls** — the form authors `equals` for v1; literal `above`/`below` are runner-supported and authorable via the raw-JSON tab, but lack dedicated form controls (a small comparator-select follow-up).
- **`expect` comparand-property variant** — `above`/`below` *another property*'s value (the resolver already supports it); v1 is literal-only.
- **Service-provider contract value schema in introspection** — the general backend solution that would retire the Q1 raw-JSON stopgap: expose a value `Schema` for `[ServiceProviderContractType]` contracts (for *all* SP messages), at which point the schema-presence-gated value editor form-drives struct/array contract values automatically.
- **Server-authoritative `POST /api/scenarios/validate`** — only if validate-without-write is wanted.

Phasing: this is a single SPA-centric phase (no Phase-1 backend). It can ship independently of the RFC 0013 follow-ups.

## 12. Resolved review questions (decision log)

Resolved during review (folded into §5–§11 above):

- **Q1 — `serviceProviderSet`/`Expect` value editor for non-scalar contracts.** Contracts expose only a type token, not a value schema. **Decision:** form-drive scalar contract families by convention; **raw-JSON fallback for struct/array contract values, entirely UI-side (schema-presence-gated), no backend special-casing** — it auto-retires when the introspection exposes contract value schemas generally (§11). (§7.3)
- **Q2 — stepped vs real-clock authoring.** **Decision:** author in either mode; surface a hint that captures are reproducible only on a stepped host; don't hard-require it. (§7.4)
- **Q3 — editing a scenario whose topology ≠ the running one.** **Decision:** Edit/Clone auto-recycles the host onto the scenario's topology first (RFC 0013 switch). (§7.1)
- **Q4 — `expect` comparand.** **Decision:** literal `equals`/`above`/`below` for v1; defer the compare-to-another-property comparand. (§7.4, §11)
- **Q5 — assert-capture ergonomics.** **Decision:** manual advance + "use current value" for v1, plus a cheap "apply setup" button; run-to-step deferred. (§7.4)
- **Merge (#104/#105/#106):** picker **de-dupes dual-annotated members** (§7.3); value editor **honors nullable/optional struct fields** (§7.3); the **`Vion.Examples.Emission`** `SensorBlock`/`ThreePhase` is a second struct test bed (§10).
