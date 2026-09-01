---
name: spec-pass
description: Use when running an SDD area pass for the migration to docs/specs/ — a launcher brief points here, or the operator says "run the <area> pass", "extract the behavior table for <area>", "implement the classified table", "spec-pass". Phase A extracts, Phase B implements after the operator classifies. Not for ordinary feature change docs (docs/spec-process.md owns those); this skill retires with the migration's last pass.
---

# Area pass — extract, classify, implement

One pass turns one roster area ([`docs/spec-process.md`](../../../docs/spec-process.md) § Corpus) into a
current-truth spec page with a fully rewritten, id-cited test suite. Two phases with a hard operator
STOP between them. The brief that dispatched you names: the **area code**, the **scope** (projects,
files, consumer sites), the **anchors**, the **RFCs to absorb** (often none), the **spec page path**,
and the **attempt number**. Decisions in the brief and in `docs/spec-process.md` are binding;
contradictions go to the change doc's Drift checkpoints, never silent divergence.

Work on branch `spec-pass/<code-lower>-a<N>` (N = attempt). Attempts are disposable by design: if
the operator discards this attempt, the branch dies and the *skill* gets fixed — never hand-fix a
discarded attempt's output.

## Phase A — extract the behavior table

Produces the pass's change doc, `proposed`, carrying the table. **No production edits, no test
edits, no bug fixes, no Jira writes in this phase** — reading and one document only.

1. Branch, then scaffold: `pwsh scripts/spec-change.ps1 new <code-lower>-pass -Areas <CODE>`.
   Fill *Summary* (what this pass covers) and *Spec implications* (the page it will create).
2. **Anchor inventory first.** Enumerate the area's machine-readable surface mechanically — grep
   the attributes, descriptors, schema fields, manifest entries the brief names — and list the
   inventory in the change doc. This list is the completeness checklist for step 3; a behavior
   sweep without it starts from a blank page and misses silently.
3. **Behavior sweep.** Walk the scope statement-by-statement (not branch-by-branch —
   [`docs/testing-conventions.md`](../../../docs/testing-conventions.md) §9's observability
   discriminator decides what is a row). Every claim comes from code read **this session**; old
   RFC prose and comments are hints at best. The table is the deliverable, one row per observable
   behavior, **all six columns required**:

   | # | Behavior (EARS) | Evidence | Test today | Rec | Why |
   |---|---|---|---|---|---|
   | 1 | WHEN a plugin requests an assembly marked `[DaleSharedAssembly]` THE SYSTEM SHALL resolve the host's loaded instance. | `PluginLoadContext.cs:340` | `PluginSdkVersionGateShould.…` | intended | the cross-plugin type-identity contract |

   - **Evidence** — `file:line`, read, not recalled.
   - **Test today** — the existing test proving it, or `GAP`.
   - **Rec** — your recommendation: `intended` (→ spec) · `bug` (→ operator files it; never into
     the spec) · `out-of-spec` (implementation detail, not contract). One-line **Why** each.
   - A `bug` rec is flagged `⚠` and gets a two-line failure sketch under the table.
4. **Map every existing test in scope** to a row; tests mapping to no row go in an
   *unmapped tests* list (Phase B merge/delete candidates).
5. **Self-check** against the anchor inventory and the PublicApi manifest's entries for the
   area's assemblies: every member visited or explicitly out-of-scope. State the counts.
6. Commit the change doc (status stays `proposed`), push the branch, and STOP with exactly this
   report shape: row/GAP/⚠/unmapped counts · the ⚠ rows verbatim · the unmapped-test list · the
   line *"Classify: reply with row numbers to override, or 'accept recs' to take all
   recommendations."*

## The operator gate

The operator classifies (the coordinator may add a second opinion on ⚠ rows). Accepted `bug` rows
are the operator's to file; they never become spec rows. Do not proceed to Phase B without the
classification.

## Phase B — implement the classified table

1. Flip the doc `in-flight`. Mint ids `AC-<CODE>-NNN.M` (umbrella per behavior cluster, leaves per
   criterion) and author the Spec-delta lines targeting the brief's page path.
2. **Author the spec page**: current-truth prose + the AC declarations, frontmatter
   `trace: enforced`. Rows the operator accepted without a test yet carry the `GAP: <reason or
   VION-nn>` marker on the declaring line.
3. **Rewrite the area's whole test suite** to `docs/testing-conventions.md` §9–17 — ids cited via
   the quoted-literal forms (§17), unmapped tests merged or deleted per their list, GAP rows
   closed where the classified table says so. **Every new behavioral test is proven red and its
   mutation named** — the PR body carries a `test → mutation` list, one line each (§11).
4. If the brief lists RFCs to absorb: delete them and run the reference sweep
   (`docs/spec-process.md` § Area passes, step 5). If it lists none, say so in the report.
5. Gates, all of them, results verbatim in the report: `dotnet build` + `dotnet test` on the full
   solution, `scripts/spec-lint.ps1`, `scripts/spec-trace.ps1`, `scripts/run-script-tests.ps1`,
   `/cleanup` once.
6. Distill every delta line into the page, then `pwsh scripts/spec-change.ps1 archive
   <code-lower>-pass`.
7. Commit per task, push, and STOP with the REPORT: commands run + results · the test → mutation
   list · GAP list · friction one-liners (journal candidates, `docs/process-journal.md` format) ·
   *no PR yet* — the coordinator runs the completeness critic and `/vion-code-review` first; the
   PR opens on the operator's go.

## Scorecard (coordinator fills, into the change doc before the PR merges)

| Measure | Value |
|---|---|
| Gates (build/test/lint/trace/self-tests/cleanup/CI) | green / what failed |
| Completeness-critic misses | count + rows added |
| Evidence errors found in review | count |
| Mutation evidence | named-mutation list complete? · Stryker trial result (pilot) |
| Operator corrections (table + PR) | count |
| Cost | sessions × model · wall time |

## Non-negotiables

| Temptation | Reality |
|---|---|
| "Fix this small bug while I'm here" | A pass ships spec + tests, never behavior changes. The row says `bug`, the operator files it. |
| "This row is obvious, skip the evidence" | Unevidenced prose is this repo's most repeated defect class (journal, D10). Every row cites `file:line`. |
| "Patch the discarded attempt's table by hand" | The next pass repays the same debt. Fix the skill, delete the branch, rerun. |
| "The suite is green, skip the mutation list" | Green proves nothing about new tests (journal 2026-08-31, PublishAllStates). Named mutation or the test doesn't count. |
| "Enshrine it — the code clearly does this" | The code doing it is evidence of behavior, not intent. Surprising rows get `bug` or the operator's explicit `intended`. |
