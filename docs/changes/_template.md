---
slug: <kebab-case-slug>
status: proposed           # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: <AREA[, AREA]>      # roster codes from docs/spec-process.md (e.g. EMIT, SCEN)
author: <name>
created: YYYY-MM-DD
updated: YYYY-MM-DD
supersedes: none           # path of a superseded change doc, or none
---

# <Title>

> Change doc — one in-flight change. The `Spec delta` below is distilled into the current-truth
> pages under `docs/specs/` in the implementation PR, then this doc is archived
> (`pwsh scripts/spec-change.ps1 archive <slug>`). Process: `docs/spec-process.md`. Never put
> change narrative inline in a spec page — it lives here.

## At a glance

### Summary

<≤5 lines, BLUF: what changes, for whom, why now.>

### Spec implications

<≤15 lines, prose: which spec pages change and how. If this stays empty, the change may not need
a change doc — fix-sized work edits the touched page directly in its PR.>

### Decisions

- `D1` — <decision> — <one-line rationale>

### Reviewer's questions

<3–5 items. Surface uncertainty; direct the reviewer. Pre-classify each open point:
(a) ratified — cite, don't relitigate; (b) decide-and-document — the session's call, recorded with
rationale; (c) propose-and-wait — options + a recommendation, the operator decides. Before archive,
each question carries its OUTCOME (accepted / changed → what, and where decided) — merge-silence is
not a recorded resolution.>

1. <question>

---

## Full design

<Free-form: mechanism, sequences (Mermaid), interface sketches, error model, alternatives
considered. The narrative that would otherwise bloat a spec page goes here. Claims about existing
code carry `file:line` evidence — code and tests are primary; comments and old design prose are
hints at best.>

---

## Drift checkpoints

> One line per divergence discovered during implementation:
> `YYYY-MM-DD: <what changed and why>`. Never inline in a spec page. A checkpoint that fixes a
> CLASS bug states the sibling sweep (done / N/A / handed off).

- _(none yet — append as implementation diverges from Full design)_

---

## Spec delta (to distill)

> The machine-readable change, one line per id. Grammar:
> `<OP> <ID> -> <target> : <payload>`
>
> - `OP` ∈ `ADDED` | `MODIFIED` | `REMOVED`
> - `target` is **repo-root-relative** (normally `docs/specs/<page>.md`)
> - `payload`: for `ADDED`/`MODIFIED` the EARS text; for `REMOVED` the reason
>
> On the implementation PR each line is applied into the named target; `spec-change.ps1 archive`
> refuses until every line is applied. The `ID` must be an exact token greppable in the target
> after distill (backticks stripped) — a real `AC-`/`SYS-` id, never an ad-hoc label.

- ADDED AC-<AREA>-001.1 -> docs/specs/<page>.md : <EARS text>
- MODIFIED AC-<AREA>-002.1 -> docs/specs/<page>.md : <new EARS text>
- REMOVED AC-<AREA>-003.1 -> docs/specs/<page>.md : <reason>

---

## Tasks

> One-commit tasks, each tagged with ≥1 AC id. Ephemeral — they live and die with this change doc.
> Plain list, no checkboxes — the PR's per-task commits are the completion record.

- `T-001` (`AC-<AREA>-001.1`): <one-commit task>
