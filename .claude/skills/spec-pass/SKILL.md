---
name: spec-pass
description: Use when running an SDD area pass for the migration to docs/specs/ — a launcher brief points here, or the operator says "run the <area> pass", "extract the behavior table for <area>", "implement the classified table", "spec-pass". Phase A extracts, Phase B implements after the operator classifies. Not for ordinary feature change docs (docs/spec-process.md owns those); this skill retires with the migration's last pass.
---

# Area pass — extract, classify, implement

One pass turns one roster area ([`docs/spec-process.md`](../../../docs/spec-process.md) § Corpus) into a
current-truth spec page with a fully rewritten, id-cited test suite — and leaves the area's code,
tests and page **mutually consistent**: defects the extraction finds are fixed in the pass, not
parked elsewhere. Two phases with a hard operator STOP between them. The brief that dispatched you
names: the **area code**, the **scope** (projects, files, consumer sites), the **anchors** (grepped
by the brief's author before dispatch — an anchor that does not exist is the brief's most common
defect; verify each and record a Drift checkpoint for any that is wrong), the **RFCs to absorb**
(often none), the **spec page path**, and the **attempt number**. Decisions in the brief and in
`docs/spec-process.md` are binding; contradictions go to the change doc's Drift checkpoints, never
silent divergence.

Work on branch `spec-pass/<code-lower>-a<N>` (N = attempt). Attempts are disposable by design: if
the operator discards this attempt, the branch dies and the *skill* gets fixed — never hand-fix a
discarded attempt's output.

## Phase A — extract the behavior table

Produces the pass's change doc, `proposed`, carrying the table. **No production edits, no test
edits, no fixes, no Jira writes in this phase** — reading and one document only.

1. Branch, then scaffold: `pwsh scripts/spec-change.ps1 new <code-lower>-pass -Areas <CODE>`.
   Fill *Summary* (what this pass covers) and *Spec implications* (the page it will create).
2. **Anchor inventory first.** Enumerate the area's machine-readable surface mechanically — grep
   the attributes, descriptors, schema fields, manifest entries the brief names — and list the
   inventory in the change doc. This list is the completeness checklist for step 3; a behavior
   sweep without it starts from a blank page and misses silently.
3. **Statement sweep.** Walk the scope statement-by-statement (not branch-by-branch —
   [`docs/testing-conventions.md`](../../../docs/testing-conventions.md) §9's observability
   discriminator decides what is a row). Every claim comes from code read **this session**; old
   RFC prose and comments are hints at best.
4. **Consumer sweep.** The statement sweep finds what the code *does*; it misses what a consumer
   *depends on*. Walk every in-repo consumer of the area (the brief names them) and then read the
   area once more as the author of a plugin/library/topology would: what does the *layout* of my
   input have to be (flat? named? ordered?); who *owns* an instance and what follows from that
   (dependency resolution, lifetime, identity); what happens when one of my files is *broken*
   (which failures are tolerated, which abort, what state is left behind); which *mechanics* of a
   marker/attribute/contract does my declaration have to satisfy. Each answer is a row. The pilot's
   fresh-context critic found four misses in a statement-complete table — all four were of these
   shapes.
5. **The table** is the deliverable — one row per observable behavior, **all six columns
   required**:

   | # | Behavior (EARS) | Evidence | Test today | Rec | Why |
   |---|---|---|---|---|---|
   | 1 | WHEN a plugin binds to an assembly marked `[DaleSharedAssembly]` THE SYSTEM SHALL resolve the shared instance. | `PluginLoadContext.cs:340` | `PluginLoadContextShould.…` | intended | the cross-plugin type-identity contract |

   - **Behavior** — written for the **spec page**, so in **published-surface vocabulary only**:
     public types and members, attribute names, exception types, file/wire formats. No private
     method names, no `file:line`, no mechanism words a refactor would falsify. The altitude test
     is *could a consumer observe the difference?* — a sentence an implementation change would force
     you to reword is at the wrong altitude.
   - **Evidence** — `file:line`, read, not recalled. This column is where implementation detail
     lives; it dies with the archived doc.
   - **Test today** — the existing test proving it, or `GAP`.
   - **Rec** — your recommendation, one of four: `intended` (→ spec) · `fix` (a defect, small and
     area-local → AC worded for the correct behavior, fixed in Phase B) · `park` (a defect too big
     or too far-reaching for this pass → one line in `docs/specs/_findings.md`) · `out-of-spec`
     (implementation shape, not contract). One-line **Why** each. A row that *names a harm* names
     the consumer that suffers it by `file:line`, or it is a guess wearing evidence's clothes.
   - A `fix`/`park` rec is flagged `⚠` and gets a two-line failure sketch under the table.
6. **Map every existing test in scope** to a row; tests mapping to no row go in an
   *unmapped tests* list (Phase B merge/delete candidates).
7. **Self-check** against the anchor inventory and the PublicApi manifest's entries for the
   area's assemblies (where the area has any — some are `[InternalApi]` by design): every member
   visited or explicitly out-of-scope. State the counts.
8. Commit the change doc (status stays `proposed`), push the branch, and STOP with exactly this
   report shape: row/GAP/⚠/unmapped counts · the ⚠ rows verbatim · the unmapped-test list · the
   line *"Classify: reply with row numbers to override, or 'accept recs' to take all
   recommendations."*

## The operator gate

The operator classifies (the coordinator may add a second opinion on ⚠ rows). No Jira is filed by
default: `fix` rows are fixed in Phase B, `park` rows go to the ledger, and Jira is for the rare
finding the operator actively schedules. Do not proceed to Phase B without the classification.

## Phase B — implement the classified table

1. Flip the doc `in-flight`. Mint ids `AC-<CODE>-NNN.M` (umbrella per behavior cluster, leaves per
   criterion) and author the Spec-delta lines targeting the brief's page path.
2. **Write the mutation with the test, before the AC counts.** For every AC: the test, the one
   mutation of the code under it that reddens *that test and no other claim*, run and observed. An
   AC no mutation can redden is not a requirement — reword it to what *is* observable, merge it
   into the AC it converges on, or drop it; never mint it. The pilot minted three such sentences
   and every one was green and worthless until a mutation exposed it. The PR body carries the
   `test → mutation` list, one line each (§11).
3. **Fix rows**: the test for the *correct* behavior, proven red against the pre-fix code (that red
   run is the defect proof — name it in the list), then the minimal fix. **Size guard:** if a fix
   turns out non-local or design-bearing while you implement it, STOP and report — it becomes a
   `park` row or its own change doc, never a silent absorption.
4. **Author the spec page**: current-truth prose + the AC declarations, frontmatter
   `trace: enforced`. Prose states rules, never rosters (a list of today's instances drifts;
   "grep-enumerable" does not). Rows the operator accepted without a test carry the `GAP: <reason
   or VION-nn>` marker on the declaring line — and if the reason is "no observable", the row is
   `out-of-spec`, not `GAP`.
5. **Rewrite the area's whole test suite** to `docs/testing-conventions.md` §9–17 — ids cited via
   the quoted-literal forms (§17), unmapped tests merged or deleted per their list, no assertion on
   log calls (§15), no articles in names (§12).
6. If the brief lists RFCs to absorb: delete them and run the reference sweep
   (`docs/spec-process.md` § Area passes, step 5). If it lists none, say so in the report.
7. Gates, all of them, results verbatim in the report: `dotnet build` + `dotnet test` on the full
   solution, `scripts/spec-lint.ps1`, `scripts/spec-trace.ps1`, `scripts/run-script-tests.ps1`,
   `/cleanup` once.
8. **Stryker one-shot audit** over the area's project (`dotnet tool install -g dotnet-stryker`,
   then `dotnet stryker` from the test project): read the survivors by hand — expect logger and
   out-of-spec mutants to dominate and ignore them; a survivor on a *behavior* is a test you owe.
   Report score, survivors, runtime and what you closed. Never a gate, never a number to chase.
9. Distill every delta line into the page, then `pwsh scripts/spec-change.ps1 archive
   <code-lower>-pass`.
10. Commit per task, push, and STOP with the REPORT: commands run + results · the test → mutation
    list · GAP list · park rows written to the ledger · friction one-liners (journal candidates,
    `docs/process-journal.md` format) · *no PR yet* — the coordinator runs the completeness critic
    and `/vion-code-review` first; the PR opens on the operator's go.

## Scorecard (coordinator fills, into the change doc before the PR merges)

| Measure | Value |
|---|---|
| Gates (build/test/lint/trace/self-tests/cleanup/CI) | green / what failed |
| Completeness-critic misses | count + rows added |
| Evidence errors found in review | count |
| Mutation evidence | named-mutation list complete? · Stryker survivors on behaviors |
| Operator corrections (table + PR) | count |
| Cost | sessions × model · wall time |

## Non-negotiables

| Temptation | Reality |
|---|---|
| "Fix it in Jira later" | A pass leaves code, tests and page consistent. Small and area-local → `fix` now; otherwise `park` in the ledger. Jira only when the operator schedules it. |
| "This fix is growing, I'll finish it anyway" | The size guard exists for exactly this: STOP, report, `park`. A "bug" that is really a feature band never rides a pass. |
| "This row is obvious, skip the evidence" | Unevidenced prose is this repo's most repeated defect class (journal, D10). Every row cites `file:line`; every harm names its consumer. |
| "The AC reads well, I'll find its mutation later" | An AC without a reddening mutation describes the code instead of constraining it. Mutation first, or it is not minted. |
| "Patch the discarded attempt's table by hand" | The next pass repays the same debt. Fix the skill, delete the branch, rerun. |
| "Enshrine it — the code clearly does this" | The code doing it is evidence of behavior, not intent. Surprising rows get `fix`/`park` or the operator's explicit `intended`. |
