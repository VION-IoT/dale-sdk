---
slug: sdd-process
status: archived           # proposed | in-flight | parked | archived
blocked-on: none
areas: process             # which docs/specs/ areas this targets; `process` = the process itself
author: jonas.bertsch (design drafted with Claude)
created: 2026-09-01
updated: 2026-09-01
supersedes: none
---

# Spec-driven development for dale-sdk

> Change doc — one in-flight change. The `Spec delta` below is distilled into
> the current-truth specs under `docs/specs/` in the implementation PR, then
> this doc is archived. Process: `docs/spec-process.md` (created by this very
> change). Never put change narrative inline in a spec page — it lives here.
>
> **Bootstrap exception:** this is the first change doc; the lifecycle gates it
> names (`spec-change.ps1`, `spec-lint.ps1`, `spec-trace.ps1`) arrive as its
> tasks, so this one doc is reviewed and archived by hand.

## At a glance

### Summary

dale-sdk adopts spec-driven development: a living current-truth spec corpus
(`docs/specs/`, ~14 contract-anchored pages — 10 full-formality, 4 prose)
seeded by extracting behavior from
code and tests area by area, a dated change-doc lane (`docs/changes/`) for
everything that changes specified behavior, and a full per-area rewrite of the
test suite to a settled style. The RFC corpus retires — deleted as absorbed.
Origin: the 2026-09-01 meta session (bad coverage, RFC-list-as-quasi-standard,
the VION-131 band and emission-policy post-mortems).

### Spec implications

Bootstrap: no existing spec page changes (none exist). This change creates the
substrate — `docs/spec-process.md`, `docs/specs/` (empty until the pilot pass),
`docs/changes/`, the ported gates, and the two convention docs the test rewrite
depends on. Every subsequent area pass is its own change doc.

### Decisions

- `D1` **Corpus cut: contract-anchored areas** — an area is one contract with
  its own anchor artifact (schema, manifest, golden file, diagnostic registry,
  or committed scenario set). The package cut splits cross-package behaviors
  (scenario semantics spans DevHost + Xunit + CLI); the author-capability cut
  shares mechanisms and is a docs layer, not a truth layer. The 23 RFCs cluster
  cleanly onto this cut, which validates it empirically.
- `D2` **Change docs replace RFCs** — "Request for Comments" names a moment in
  a conversation; the corpus must state current truth. RFCs serve as hints
  during extraction only (code is primary — RFC prose has been stale in the
  tree since at least 0018 §9), are **deleted** by the pass that absorbs them,
  and every living doc/skill/code-comment citation is re-pointed or dropped in
  that pass. Append-only logs (journal, retro notes, PRs) keep their citations.
- `D3` **Extraction via behavior tables** — per area, an agent drafts one row
  per observable behavior: EARS-style sentence + `file:line` evidence + citing
  test or explicit `GAP`. The maintainer reviews the *table* (fast,
  reviewable), classifying surprising rows: intended → spec; bug → Jira, never
  enshrined; out-of-spec → explicitly dropped. Seeding, test-gap discovery and
  the test rewrite are thereby one workstream.
- `D4` **Area passes with full test rewrite** — one pass = table → review →
  the area's whole test suite brought to the settled style (Tier A tests cite
  AC ids) → spec page lands → the trace gate's covered-area list grows (a
  ratchet, never shrinks) → change docs mandatory for that area onward.
- `D5` **Pass 0 first: the settled style** — mesh's testing-conventions
  (behavior tables, prove-red with named mutation, naming, AAA, Moq patterns)
  and comment-conventions (inline why-comments) ported and adapted before any
  full-rewrite pass starts. MSTest + Moq are already house-native here; the
  xunit.v3 side gets equivalents for `examples/*`/`libraries/*`.
- `D6` **Pilot: plugin loading ABI** — smallest rewrite (8 tests), retires the
  0.11.1/#132 hazard class (`[DaleSharedAssembly]`, ALC semantics), extraction
  purely from code. Pass 2 (config gating or emission) validates the
  map-existing-tests-onto-rows half on a populated suite.
- `D7` **Scoped formality** — Tier A (EARS ids + trace gate): the behavioral
  kernel. Tier B (prose page, changes ride in the PR): I/O contract families,
  TestKit surfaces, CLI, Http. Tier C (exempt): examples, templates,
  DevHost.Web SPA internals, SmokeHost fixture. Committed scenarios are
  citable as AC evidence alongside unit tests.
- `D8` **Routing by scope, not discovery point** — multi-repo work keeps the
  architecture `/spec` → `/implement` lane (report-back included), but briefs
  to this repo slim to pointers at spec pages + a change-doc obligation
  (`brief` is the journal's second-largest friction category, and every
  incident was a restatement gone stale). Single-repo work starts here as an
  implementer-owned change doc — the session that codes it writes it; the PR
  is the report. lbl's two-phase rule self-fires: open design points or
  unratified decisions → STOP after the change doc for ratification.
- `D9` **Mutation testing enters via the pilot only** — Stryker.NET (alive:
  v4.16.0, 2026-08 releases) is trialed on the plugin pass; surviving mutants
  get a test or a waiver row. Its in-memory Roslyn compilation vs this
  toolchain is verified there, not assumed. No standing mutation *or* coverage
  number in CI (anti-Goodhart; coverage report explicitly declined).
- `D10` **Jira epic split deferred** — GAP rows live in spec pages (durable,
  in-repo, reviewable); extraction-discovered bugs go to Jira as bugs under
  the existing intake conventions. Whether engineering items get their own
  epic beside VION-62 is decided when the first passes show the volume.
- `D11` **Launcher dispatch supported from day one** *(added at ratification)* —
  area passes and ratified change docs are dispatchable to fresh sessions via
  the architecture repo's `scripts/launch-session.ps1`, exactly as `/implement`
  and `/fix` dispatch: brief under `architecture/.claude/briefs/` (the
  gitignored home its permission model expects), `-Name "<slug>: dale-sdk"`
  (the launcher lane's `<slug>: <repo>` convention), ratification round-trips
  via `-AmendFile`. `docs/spec-process.md` carries the recipe + brief shape +
  model/effort rubric, so dispatching a pass costs a copy-paste, not a
  reinvention. No launcher changes needed — the support is the documented
  contract on this side.

### Reviewer's questions

1. *(a — ratified)* The decisions above restate the 2026-09-01 session's
   AskUserQuestion outcomes; cite, don't relitigate. OUTCOME: ratified
   2026-09-01 with one addition, folded in as `D11`.
2. *(b — decide-and-document, pass 0)* Exact adapted shapes. OUTCOME, decided
   during pass 0: template is `docs/changes/_template.md` (`areas:` replaces
   `library:`; underscore-prefixed files are never change docs); delta targets
   are **repo-root-relative** (one flat corpus, no per-library resolution);
   the trace ratchet is per-page frontmatter `trace: enforced` (never removed
   once set — Tier B pages simply don't carry it); an AC↔test link is the id
   as a **quoted string literal** — MSTest `[TestProperty("spec", "AC-…")]`,
   xunit `[Trait("spec", "AC-…")]`, or a scenario file's `"specs": ["AC-…"]` —
   never a comment or method name; the gates run in their own lightweight
   workflow (`spec-gates.yml`), because `publish.yml` ignores `docs/**` and a
   docs-only PR must still be gated.
3. *(b — decide-and-document, per pass)* Final page names and merges within
   the Tier A roster (§Corpus below) — the roster is the map, each pass owns
   its page's final boundary.
4. *(c — propose-and-wait, at their area's pass)* RFC 0005 (observability,
   never implemented) and RFC 0011 (parked): recommendation is delete, with
   any still-live idea becoming a Jira item or nothing. Owner decides at the
   scenario/DevHost passes.

---

## Full design

### Corpus — `docs/specs/`

Current-truth pages, present tense, terse, evidence-linked; history lives in
git and the archived change docs. One `_invariants.md` (identifier stability,
determinism, emission floor — cross-cutting rules pages cite instead of
restating). The roster, by group:

| Group | Pages (anchor) | Tier |
| --- | --- | --- |
| Authoring contracts | authoring surface + analyzer registry (44 live DALE descriptors, ids to DALE046) · service properties & measuring points incl. emission policy (throttler semantics, dual-annotation) · contracts & provider faces · instantiation/config gating | A |
| Wire contracts | introspection JSON + identifier stability (golden files, packed-artifact rule) · scenario & topology files + stepping semantics (scenario schema, committed scenarios) · DevHost control API · plugin loading ABI (`[DaleSharedAssembly]`, ALC rules) | A |
| Wire contracts | CLI surface (help snapshot) | B |
| Runtime semantics | block lifecycle (start/stop ordering, teardown delivery) · Modbus link policy (link verdicts, socket lifetime) | A |
| Contract families | DigitalIo/AnalogIo · TestKit surfaces (virtual time) · Http | B |
| Exempt | examples · templates · DevHost.Web SPA internals · SmokeHost fixture | C |

Existing convention docs stay what they are (process, not behavior); where one
carries behavioral truth today (`identifier-stability.md`), its content moves
into the owning spec page at that area's pass and the doc becomes a pointer or
is deleted — one owner per rule.

### Change lane — `docs/changes/`

Dated docs `YYYY-MM-DD-<slug>.md` on the lbl grammar (this file is the shape),
archived to `docs/changes/archive/` once every Spec-delta line is distilled.
Two lanes by size:

- **Fix-sized** (most VION-62 items): no change doc. The PR updates the
  touched spec page in the same commit set; journal + tests + review cover the
  rest, unchanged.
- **Feature-sized** (changes specified behavior beyond a page edit — the
  VION-131 kind): full cycle. Default PR shape: doc rides with code, one PR.
  Two-phase (doc → ratification STOP → code) self-fires on open design points
  or decisions beyond what the operator ratified.

### Extraction and the area pass

Per area: (1) agent drafts the behavior table from code and tests — every row
carries evidence, RFC prose is a hint at best; (2) maintainer reviews and
classifies rows (intended / bug → Jira / out-of-spec); (3) the area's whole
test suite is rewritten to the settled style — Tier A tests cite AC ids, GAP
rows get tests or become explicit parked items; (4) the spec page lands; (5)
the trace gate's area list grows by one; (6) the RFCs the page absorbed are
deleted and the reference sweep runs (no living doc, skill or code comment
cites them — `grep -r "RFC 00"` clean outside `docs/process-journal.md`,
`docs/retro/`, `docs/changes/archive/`). Each pass is one change doc, one
band-sized PR, reviewable on its own; un-passed areas live under today's rules
until their pass — the repo is never half-migrated.

Pilot order: plugin ABI → config gating or emission → introspection +
identifiers → scenario/stepping/pairing (the largest, taken once the shape is
proven) → remainder. Stryker.NET runs inside the pilot as an exit check
candidate (D9); adopted into the pass DoD only if signal beats noise there.

### Gates

Ported from lbl and adapted: `spec-change.ps1` (scaffold + archive-refuses-
until-distilled), `spec-lint.ps1` (AC well-formedness; narrative-in-spec-page
detection), `spec-trace.ps1` (every AC/SYS id in a covered area has a citing
test — unit or committed scenario; covered-area list is the ratchet). Wired
into the existing CI workflow beside the style gate. No coverage or mutation
number in CI (D9).

### What does not change

The process journal, metrics and retro loop (retro-1 runs right after pass 0,
so its promotions land on the new substrate). `/vion-code-review` and the
D1–D10 taxonomy. The DevHost demonstrate-don't-assert culture and the
committed-scenario corpus (now additionally citable as AC evidence). The
cleanup/style gate. Releasing and the reference-bump obligation. The
`dale-sdk-feedback` intake skill and VION-62's curation rules.

---

## Drift checkpoints

- _(none yet — append as implementation diverges from Full design)_

---

## Spec delta (to distill)

> Bootstrap form: targets are created, not edited. Applied by the pass-0 PR;
> this doc archives when all lines exist.

- ADDED spec-process -> docs/spec-process.md : the operational playbook (corpus roster + tiers, change-doc lifecycle + two lanes, area-pass protocol + DoD, extraction method, routing + launcher dispatch, gate reference)
- ADDED changes-lane -> docs/changes/ : this directory + `archive/` + `_template.md`
- ADDED specs-corpus -> docs/specs/ : directory + `_invariants.md` stub naming the roster (pages arrive per pass)
- ADDED testing-style -> docs/testing-conventions.md : the authoring half (mesh port — behavior tables, prove-red with named mutation, naming, AAA, Moq, both framework flavors), keeping the existing repo-specific sections
- ADDED comment-style -> docs/comment-conventions.md : inline-comment conventions (mesh port); XML-doc rules stay in `sdk-surface-conventions.md` §2, cross-linked
- ADDED gates -> scripts/spec-change.ps1 : with spec-lint.ps1, spec-trace.ps1, run-script-tests.ps1, their *.tests.ps1 self-tests, and .github/workflows/spec-gates.yml
- MODIFIED claude-md -> CLAUDE.md : read-before-write rows for the new docs; working agreement gains the two-lane rule; `docs/rfcs/` marked frozen ("historical; being absorbed into docs/specs/ — do not cite"), removed entirely with the last pass

---

## Tasks

> One-commit tasks for **pass 0 only** — each area pass is its own change doc.
> The pilot (plugin ABI) starts as `docs/changes/<date>-spec-pass-plugin-abi.md`
> after this doc is ratified and pass 0 lands.

- `T-001` (spec-process): write `docs/spec-process.md` (incl. the D11 launcher-dispatch recipe) + `docs/changes/_template.md` + create `docs/specs/` with `_invariants.md` stub
- `T-002` (gates): port + adapt `spec-change.ps1`, `spec-lint.ps1`, `spec-trace.ps1` + `run-script-tests.ps1` with `*.tests.ps1` self-tests; wire into the new `spec-gates.yml` workflow
- `T-003` (testing-style): expand `docs/testing-conventions.md` with the mesh-adapted authoring half (MSTest + xunit.v3 flavors; the two known reflection sites scheduled for seams, not grandfathered)
- `T-004` (comment-style): add `docs/comment-conventions.md`; cross-link from `sdk-surface-conventions.md` §2
- `T-005` (claude-md): rewire `CLAUDE.md` (read-before-write table, working agreement, frozen-RFC note in `docs/rfcs/README.md`)
- `T-006` (archive): distill-check this doc's delta lines, move it to `docs/changes/archive/`, flip status
