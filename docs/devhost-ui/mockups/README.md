# DevHost UI mockups — "Dale Verify" (Explorer + Player)

Static, self-contained HTML mockups from the 2026-06-11 design session. Open any file directly in a
browser (no build, no server; icons load from CDN). They are **reference artifacts for implementation**,
not pixel specs — layout zones, density targets, and interaction vocabulary are the contract.

Companion documents: [RFC 0006 — scenario files](../../rfcs/0006-scenario-files.md) (accepted) and the
roadmap below.

## Files

| File | Screen state | Shows |
|---|---|---|
| [01-explorer-landing.html](01-explorer-landing.html) | Explorer, landing | Block rail, collapsed-by-default groups with counts, primary strip, one-row value-as-control density, folded watch strip, filter affordances (`/`, `Ctrl K`) |
| [02-explorer-pinned-baseline.html](02-explorer-pinned-baseline.html) | Explorer, mid-scenario | Active value filter (matches collapse the rest), baseline-diff chip + per-block changed counters, expanded vertical watch column (drive above observe), sparkline tiles with since-baseline deltas, "save as scenario" promotion affordance |
| [03-struct-editor.html](03-struct-editor.html) | Struct editor dialog | Schema-generated field form for `EnergyManager.GridConfig` (units, min/max from `[StructField]`), dirty-draft guard ("live update arrived — draft kept"), Form / Raw JSON tabs, payload preview, copy-as-C#-snippet |
| [04-scenario-player.html](04-scenario-player.html) | Player, scenario loaded | Ordered acknowledged steps (incl. a condition-gated wait with elapsed time), watch tiles only for the scenario's working set, human-judgment checklist, copy-verification-report |
| [05-player-guard-states.html](05-player-guard-states.html) | Player guard states | Blocking topology-mismatch interstitial; failure taxonomy — mechanical step failure (404, points at file) rendered distinctly from a human "judged not ok" |
| [06-scenario-unification.html](06-scenario-unification.html) | Diagram | Scenario-file unification flow: 3 producers → `*.scenario.json` → one C# ScenarioRunner → Player / xunit / CLI |

## Design vocabulary (the deliberate choices these mockups encode)

- **Identifiers first, mono, dev-styled** — rows show `AllocatedActivePowerKw`, not localized titles;
  titles/descriptions live in the inspector and the filter index. Deliberate divergence from the consumer
  dashboard.
- **Value-as-control** — no separate value and control columns; the value cell *is* the input when writable.
- **Badges demoted** — type/annotation chips move to a per-row expander / inspector panel; struct properties
  collapse to `{ } n fields · view`.
- **Vertical watch column, drive above observe** — PLC watch-table precedent; value scanning is a vertical
  eye movement.
- **Baseline diff as the verification primitive** — reset (`b`) → stimulate → read what lit up (amber dots,
  per-block changed counters in the rail).
- **Condition-gated waits, never sleeps, in Player steps** — rendered with elapsed time.
- **Failure taxonomy** — blocking interstitial (mis-staged bench) ≠ red step failure (mechanical, points at
  file/step) ≠ amber judgment (human verdict). Three visual registers so a 404 never reads as "behavior is
  wrong".

## Provenance and caveats

- Grounded in the real `EnergyManagerClosedLoop` topology (7 blocks / 176 properties) and the
  `PeakShavingShould` working set (9 properties); *values* are illustrative.
- Property identifiers were corrected against the verified consumer surfaces after a fact-check pass
  (`GridHeadroomConfigUi` fields, `ComputedGridActivePowerKw`, `StateOfChargePercent`). Rail labels use
  short names (`RefCtrlBuffer`) to demonstrate truncation; the real preset assigns no `name:`, so name paths
  default to type names — see RFC 0006 "Name paths".
- Light palette only; the production implementation themes via the same CSS-variable layer.

## Roadmap context (value-first phasing, decided 2026-06-11)

| Release | Contents |
|---|---|
| R0 | Correctness patch in the existing page (edit-clobber fix, initial-value priming, vendor CDN deps, honor decimals/enumLabels/units/min-max, topology name in header) |
| R1 | New shell: master-detail navigation + filtering + watch window + struct field forms + baseline diff (mockups 01–03) |
| R2 | Run control (pause / resume / reset, topology switch) + read-only setup/topology panel |
| R2.5 | Presentation preview gallery per block (sample values from introspection) |
| R3 | Scenario files + Player v1 (mockups 04–05; RFC 0006) |
| R4 | xunit theory + `dale scenario` verbs + template/AGENTS.md enablement (RFC 0006) |
| R5 | Topology files (`*.topology.json`, dev profile of `SetLogicConfigurationPayload`; RFC 0006) |
| Later | Sparklines/history, evidence panels (logs + message tap), `checks[]`, `ramp`, Recorder |

Substrate decision: vendored Preact + htm + @preact/signals (+ uPlot) as static ESM in the embedded
`wwwroot` — no node build, no CI change. Client state lives in a signals store (DOM is a projection);
dirty drafts shadow live values; Player run state is server-side.
