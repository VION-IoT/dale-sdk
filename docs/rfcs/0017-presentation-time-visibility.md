# RFC 0017 — Presentation-time member visibility

- **Status:** Proposed — 2026-07-09
- **Author:** jonas.bertsch
- **Related:** **RFC 0016 (configuration-time structural gating)** — this RFC is its deliberately-shallow sibling and **depends on the predicate-expression grammar defined in RFC 0016 §7**. RFC 0004 (emission policy / `Presentation` hints), RFC 0013 (Logic Editor). Cross-repo: `vion-contracts` (the `Presentation` model), `dashboard` (config form rendering). Explicitly **not** `cloud-api` runtime or `dale`. Origin: the `DirectMeasurement` pattern in `logic-block-libraries/.../ElectricityMeterSiemensPac2200.cs`.

> This is a design contract, not an implementation. It stops at the seams (exact field name, store wiring, i18n) so the per-component spec can fill them in against current `main`.

## 1. Summary

Some properties are only *relevant* under certain configurations, but they still **exist and function** — the block's own logic decides their behavior. Example: `ElectricityMeterSiemensPac2200.DirectMeasurement` (a runtime-settable `[ServiceProperty] bool`); when true, the CT-ratio commissioning inputs (`PrimaryCurrentToWriteA`, `SecondaryCurrentToWrite`, `WriteCtRatio`) become internal no-ops. Showing those inputs when they do nothing is clutter; hiding them is a **pure display** decision.

This RFC adds one operator: **`[VisibleWhen(<predicate>)]`** on a service property — a **presentation hint** evaluated **reactively in the UI only**, against **live property values** (including runtime-mutable ones). When false, the property is hidden from the configuration/monitoring form; it stays fully present in the runtime, MQTT, the cloud DB, and introspection, and keeps functioning per the block's logic. Nothing outside the dashboard changes.

It is the **soft twin** of RFC 0016's structural gate, and the two are deliberately kept distinct: RFC 0016 changes what **exists** (hard, config-time, all layers, driven only by structural inputs); this changes what is **shown** (soft, runtime-reactive, UI-only, driven by any property). They share **only** the RFC 0016 §7 predicate grammar and its evaluator behavior — not their semantics, evaluation sites, or effects.

## 2. Motivation

- **The demand is real and forecast.** Choosing the structural-gating direction (RFC 0016) with human-readable Model/variant inputs directly creates "these settings only matter for this variant" requests. `DirectMeasurement` is the same shape without any charging-station involvement, so it generalizes.
- **Today there is no mechanism.** Confirmed across the stack: the introspection/`Presentation` model (`vion-contracts/TypeRef/Presentation.cs`) and the dashboard (`src/domain/apis/service/schema.ts`) carry only static rendering hints — `displayName, group, order, category, importance, uiHint, decimals, format, statusMappings, enumLabels` — **no** `show-if`/`visibleIf`/dependency field. The only conditional concept anywhere in the logic domain is the *template*-level `conditionalOnParameter` (a whole-block gate) and the template parameter `visibleWhen` (a template-form field hint) — neither reaches a *block's* service property.
- **It must be separate from structural gating, or it becomes wrong.** A property whose visibility depends on a **runtime-mutable** value (`DirectMeasurement` can flip at runtime) **cannot** gate existence: existence is frozen at config-apply (RFC 0016 §3); a runtime value cannot add/remove a member without the runtime-dynamic machinery both RFCs reject. So this operator is intrinsically display-only. Conflating the two would either wrongly freeze `DirectMeasurement` at config time or wrongly make plug-count reactive at runtime.

## 3. The decision (and the alternatives rejected)

**Chosen — a `[VisibleWhen]` presentation hint on service properties, carried in the introspection `Presentation` document, evaluated reactively in the dashboard against live property values; UI-only, zero runtime/cloud change; reusing the RFC 0016 §7 predicate grammar.**

- **UI-only display hint, not existence gate.** *Rejected:* routing `DirectMeasurement`-class hiding through RFC 0016's structural gate. The referenced value is runtime-mutable, so it fails the "structural input, immutable at runtime, applied pre-bind" contract; and the properties must keep existing (they still function, just no-op). Existence gating here would be a category error.
- **Reference any service property.** *Rejected:* restricting to structural inputs (RFC 0016's rule). The whole point is to react to ordinary runtime settings like `DirectMeasurement`. The trade-off — the predicate can reference values that only exist at runtime — is exactly why it is display-only.
- **Reuse the grammar, not the machinery.** *Rejected:* a second, bespoke expression language. One grammar (RFC 0016 §7) with one evaluator behavior (guarded by the shared conformance vector) keeps humans and both codebases speaking one dialect. But the *semantics* stay separate (see §5), so the two operators never blur.
- **Presentation document, not a new top-level field.** *Rejected:* a new sibling to `Schema`/`Presentation`/`Runtime`. Visibility is a rendering concern; it belongs in `Presentation`, which the dashboard already consumes and where the template layer's `visibleWhen` already lives conceptually.

## 4. Authoring surface (SDK)

```csharp
[ServiceProperty(Title = "Direkte Messung (ohne Stromwandler)")]
[Presentation(Group = PropertyGroup.Configuration)]
public bool DirectMeasurement { get; set; }

[ServiceProperty(Title = "Primärstrom (schreiben)", Unit = "A", Minimum = 1, Maximum = 5000)]
[Presentation(Group = PropertyGroup.Configuration)]
[VisibleWhen("DirectMeasurement == false")]        // hidden when direct-measurement is on
public double PrimaryCurrentToWriteA { get; set; }

// …SecondaryCurrentToWrite, WriteCtRatio likewise
```

- `[VisibleWhen]` is a **presentation** annotation; a property with none is always shown (backward compatible).
- The predicate follows RFC 0016 §7 but with the widened reference scope of §5.
- The property's *behavior* is unchanged and remains the block's responsibility (the CT-writes already no-op when `DirectMeasurement` is true). `[VisibleWhen]` never alters behavior — only display.

## 5. Semantics vs RFC 0016 (the load-bearing distinction)

| | RFC 0016 `[ExistsWhen]` (structural gate) | RFC 0017 `[VisibleWhen]` (this) |
|---|---|---|
| Predicate may reference | only `[StructuralConfig]` scalar inputs | **any** service property (incl. runtime-mutable) |
| Evaluated **when** | config time + config-apply (bind) | **reactively, live** |
| Evaluated **where** | UI + cloud + Dale/SDK (all layers) | **UI only** |
| Affects | existence: introspection, routing, MQTT, cloud DB, UI | **display only** |
| Member, when off | does not exist (never activated) | **exists, still runs** (per block logic), just hidden |
| Changes via | reconfigure → redeploy (recycle) | **flips live** |
| Shared with the other | — | **only** the §7 grammar + evaluator behavior |

Two guardrails that keep them from blurring:

- **No cross-boundary predicates.** A single `[VisibleWhen]` predicate should not `&&`/`||` a structural input with a runtime property in a way that implies existence semantics — visibility is always display-only regardless of what it references. (Referencing a structural input from `[VisibleWhen]` is *allowed* but is still only a display hint; if the intent is to remove the member, that is `[ExistsWhen]`.)
- **A hidden property still round-trips.** Because it exists, its value is still set/persisted/published normally; the UI merely does not render its editor. Re-showing it (the predicate flips back) reveals its current value.

## 6. Predicate reference scope and evaluation

- **Grammar:** RFC 0016 §7, verbatim.
- **Reference scope:** any service property on the same block (or, per the spec's decision, the same service). Unlike RFC 0016, runtime-mutable references are the norm.
- **Evaluation site:** the dashboard config/monitoring form, **reactively** against the live value stream the dashboard already subscribes to (the `Active*` read-model / retained-state values surfaced through `src/components/widgets/shared/*` and `src/components/ui/values/*`). As the referenced value changes, visibility re-computes with no redeploy.
- **The evaluator is the same TypeScript implementation** RFC 0016 introduces for the editor, exercised against the same conformance vector (RFC 0016 §7). No C# evaluator is required for this RFC — nothing server-side or runtime evaluates `[VisibleWhen]`.

## 7. Cross-repo impact

Deliberately narrow — two repos.

### 7.1 dale-sdk

- `Vion.Dale.Sdk/Core/` — a `[VisibleWhen]` attribute contributing to `Presentation`.
- `Vion.Dale.Sdk/Introspection/**` + the presentation-emitting path — carry the predicate string into the property's `Presentation` document.
- `Vion.Dale.Sdk.Generators/` — an analyzer (DALE#### tbd): the predicate parses, type-checks, and references properties that exist on the block/service. Weaker than RFC 0016's (no structural-only restriction).
- **No binder, routing, or runtime change** — `[VisibleWhen]` is inert to `DeclarativeServiceBinder` beyond passing the hint through.

### 7.2 vion-contracts

- `Vion.Contracts/TypeRef/Presentation.cs` — **one additive optional field** (the visibility predicate). Add-fields-only; no other model changes. It reuses the §7 grammar owned alongside RFC 0016.

### 7.3 dashboard

- `src/domain/apis/service/schema.ts` (`Presentation`) — carry the predicate.
- `src/components/widgets/shared/ServicePropertyGroups.vue` / `WritableServiceProperty.vue` (and the config-form rendering used by RFC 0016's `AddBlockDialog` work) — **evaluate the predicate reactively and hide the property's editor when false.** Empty groups should collapse.
- The **TypeScript predicate evaluator** from RFC 0016 (shared).

### 7.4 Not touched

`cloud-api` (no storage, projection, or validation change — a hidden property is a normal property server-side) and `dale` (no runtime awareness of visibility). This is the whole reason the two operators are separate RFCs: RFC 0017 ships without any runtime or cloud work.

## 8. Out of scope

- **Existence / structural gating** — RFC 0016.
- **Visibility of interfaces / wireable slots** — an interface's *wireability* is a structural (existence) concern (RFC 0016); `[VisibleWhen]` is for **service-property display** only. Hiding a *wireable slot* based on a runtime value is incoherent (wiring is config-time-frozen).
- **Cross-property enable/disable of behavior** — the block already owns that (the CT-writes no-op); `[VisibleWhen]` never changes behavior.
- **Server-side or agent-facing evaluation** — there is no need to evaluate visibility outside the human UI.

## 9. Risks and under-examined areas

- **R1 — Reference scope boundary.** Same-block vs same-service reference resolution, and how a predicate names a property on a *sibling* service, were not designed. Simplest cut: same-service only; widen if a real need appears.
- **R2 — Live-value source & timing.** Which value stream the reactive evaluation reads (draft/edit value vs deployed live value vs retained MQTT) matters when the referenced property is being edited but not yet deployed. The dashboard's draft-vs-active split (RFC 0016 context) means "current value" is ambiguous during editing; the spec must pick one and be explicit.
- **R3 — Shared evaluator drift.** Same mitigation as RFC 0016 §7 (conformance vector). Since only the TS side runs here, the risk is that TS diverges from the C# behavior used for `[ExistsWhen]`; the shared vector covers both.
- **R4 — Trigger properties.** `WriteCtRatio` is a `UiHint = Trigger` (a button whose getter is always false). Hiding a trigger is fine, but the interaction of `[VisibleWhen]` with `UiHint`-driven controls (buttons, status pills) is untested.
- **R5 — Accessibility / empty groups.** Reactive hide/show can leave empty `PropertyGroup`s or shift focus; the rendering spec should collapse empties and preserve form stability.
- **R6 — Scope discipline over time.** The temptation to let `[VisibleWhen]` grow existence-like powers (e.g. "hide *and* stop publishing") must be resisted — that path is RFC 0016. Keep this operator display-only.

## 10. Requirement tags

- **R-SDK-1** `[VisibleWhen]` attribute → `Presentation`; predicate analyzer (parse/type/reference-exists).
- **R-CON-1** One additive `Presentation` field for the predicate.
- **R-DASH-1** Reactive predicate evaluation in the config/monitoring form; hide editor + collapse empty groups; reuse the shared TS evaluator.
- **R-SHARED-1** Predicate grammar + evaluator behavior shared with RFC 0016 §7, guarded by the common conformance vector.

## 11. Sequencing note

RFC 0017 is independently shippable and much cheaper than RFC 0016 (two repos, no runtime/cloud work). But its *value* is highest once RFC 0016 lands the human-readable structural inputs (Model/variant) that create the "only relevant for this variant" cases — and it should reuse the predicate grammar/evaluator RFC 0016 introduces rather than inventing its own. Recommended order: define the shared §7 grammar with RFC 0016; ship RFC 0016; then RFC 0017 as a small follow-on. If a pressing `DirectMeasurement`-class need lands first, RFC 0017 can ship ahead by standing up the grammar + TS evaluator on its own, and RFC 0016 adopts them.
