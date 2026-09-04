---
name: spec-pass
description: Use when running an SDD area pass for the migration to docs/specs/ — a launcher brief points here, or the operator says "run the <area> pass", "extract the behavior table for <area>", "implement the classified table", "spec-pass". Phase A extracts, Phase B implements after the operator classifies. Not for ordinary feature change docs (docs/spec-process.md owns those); this skill retires with the migration's last pass.
---

# Area pass — extract, classify, implement

One pass turns one roster area ([`docs/spec-process.md`](../../../docs/spec-process.md) § Corpus) into a
current-truth spec page with a fully rewritten, id-cited test suite — and leaves the area's code,
tests and page **mutually consistent**: defects the extraction finds are fixed in the pass, not
parked elsewhere. Two phases with a hard operator STOP between them. The brief that dispatched you
names: the **area code**, the **scope** as folders and projects (never a transcribed file list —
two passes running, the brief's file and descriptor counts were the thing that was wrong; count
them yourself and record a Drift checkpoint where the brief differs — and the **test scope** the
same way, as the test project's folders with the suites another page owns named by their
citations, never a list of files: the brief that listed suites missed three in the area's own
project), the **anchor kinds**
(attributes, descriptor ranges, schema, manifest — you enumerate the instances; an anchor kind that
turns out empty, such as an attribute with no named parameters, is a Drift checkpoint, not a row),
the **RFCs to absorb** (often none), the **spec page path**, and the **attempt number**. Decisions
in the brief and in `docs/spec-process.md` are binding; contradictions go to the change doc's Drift
checkpoints, never silent divergence.

Work on branch `spec-pass/<code-lower>-a<N>` (N = attempt). Attempts are disposable by design: if
the operator discards this attempt, the branch dies and the *skill* gets fixed — never hand-fix a
discarded attempt's output.

## Phase A — extract the behavior table

Produces the pass's change doc, `proposed`, carrying the table. **No production edits, no test
edits, no fixes, no Jira writes in this phase** — reading and one document only.

1. Branch, then scaffold: `pwsh scripts/spec-change.ps1 new <code-lower>-pass -Areas <CODE>`.
   Fill *Summary* (what this pass covers) and *Spec implications* (the page it will create).
2. **Anchor inventory first.** Enumerate the area's machine-readable surface mechanically — grep
   the attributes, descriptors, schema fields, manifest entries the brief's anchor kinds name — and
   list the inventory in the change doc. This list is the completeness checklist for the sweeps; a
   sweep without it starts from a blank page and misses silently. For every attribute in the
   inventory, compare its `AttributeTargets` with the targets its readers actually walk: a target
   nothing reads is a row (one pass found that shape twice — a method target and a property target
   that compiled, emitted nothing and warned about nothing).
3. **Statement sweep.** Walk the scope statement-by-statement (not branch-by-branch —
   [`docs/testing-conventions.md`](../../../docs/testing-conventions.md) §9's observability
   discriminator decides what is a row). Every claim comes from code read **this session**; old
   RFC prose and comments are hints at best.
4. **Consumer sweep.** The statement sweep finds what the code *does*; it misses what a consumer
   *depends on*. Walk every in-repo consumer of the area and then read the area once more as the
   author of a plugin/library/topology would: what does the *layout* of my input have to be; who
   *owns* an instance and what follows (dependency resolution, lifetime, identity); what happens
   when one of my files is *broken* (tolerated, aborted, what state is left); which *mechanics* of
   a marker/attribute/contract does my declaration have to satisfy. Each answer is a row. A Tier C
   example is a consumer to read, not a test bed: it references a published package, so it cannot
   prove a same-PR SDK fix.
5. **Edge-value sweep.** For every knob, token or input the area accepts — durations, thresholds,
   names, paths, versions — walk the edge values and write a row for what each *observably* does:
   negative, zero, empty, whitespace, non-finite (`NaN`, infinity), out of range or overflow, an
   equivalent spelling (`"250"` vs `"250ms"`, case, culture), a value the compile-time validator
   accepts that the runtime then ignores or inverts. The pass that skipped this sweep had six of its
   ten critic misses here.
6. **State-interaction sweep.** Where the area is a multi-step decision (a gate, a resolution chain,
   a lifecycle), take each decision path once more *while another state is pending*: a drop while a
   value is held, a stop while a flush is armed, a reset while a run is active. Each combination
   whose outcome differs from the naive reading is a row — these are the rows that leave a consumer
   stale forever and never show in a statement walk. Where an initialization can **fail closed** (a
   refused configuration, a binder that throws), walk every later message the instance can still
   receive — a stop, a snapshot, a restore, a write, a second configuration — and write a row
   wherever the answer differs from a healthy instance's: four of one pass's nine critic misses
   were exactly these.
7. **The table** is the deliverable — one row per observable behavior, **all six columns
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
     lives; it dies with the archived doc. **A probe is evidence only for the shape it ran:** when
     a claim rests on a scratch probe, the row records the probe's fixture shape (the declaration
     as written — `get;` is not `get; init;`) and the surface it read (the definition view is not
     the live view) beside the result. A probe over a different shape or surface than the row
     names is a guess wearing evidence's clothes; one pass wrote off a correct reading on such a
     probe and paid three amendments for it.
   - **Test today** — the existing test proving it, or `GAP`.
   - **Rec** — your recommendation, one of four: `intended` (→ spec) · `fix` (a defect, small and
     area-local → AC worded for the correct behavior, fixed in Phase B) · `park` (a defect too big
     or too far-reaching for this pass → one line in `docs/specs/_findings.md`) · `out-of-spec`
     (implementation shape, not contract). One-line **Why** each. A row that *names a harm* names
     the consumer that suffers it by `file:line`, or it is a guess wearing evidence's clothes. A
     `park` rec that rests on a member's *history* — added last, newer than its siblings — has no
     evidence column: recency is not a reason to treat a member differently (an operator overruled
     one such park; the fix was four lines and retired two special cases). Where the brief
     pre-classifies a class of rows as *propose-and-wait* — a wire shape, a public member's
     semantics — a fifth value **`propose`** carries the recommendation on the row: implemented as a
     `fix` if the operator accepts it, written to the ledger as a `park` if not, so that neither
     `fix` (fixed on the session's judgment) nor `park` (not fixed at all) misstates the row (one
     pass minted it for 21 rows; it was the right reading of its brief).
   - A `fix`/`park`/`propose` rec is flagged `⚠` and gets a two-line failure sketch under the table;
     a `propose` sketch ends with the recommendation.
8. **Map every existing test in scope** to a row; tests mapping to no row go in an
   *unmapped tests* list (Phase B merge/delete candidates).
9. **Self-check** against the anchor inventory and the PublicApi manifest's entries for the
   area's assemblies (where the area has any — some are `[InternalApi]` by design): every member
   visited or explicitly out-of-scope. State the counts. *Visited by a row* is not *covered by a
   criterion*: in Phase B this self-check is re-read so that every classified `intended`/`fix`
   row maps to a criterion id or to a checkpoint that says why not, and every hole in the id
   sequence has a checkpoint naming the id (`spec-trace` refuses a hole no archived change doc
   names) — one pass claimed two fields covered that no criterion stated. The self-check also asks
   the reverse question per anchor instance: *which observable behaviours have neither a row nor a
   criterion?* Counting rows against criteria never asks it, and two behaviours reached a page that
   way (a lifecycle entry point, and the one silent success the pass had left).
10. Commit the change doc (status stays `proposed`), push the branch, and STOP with exactly this
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
   **Minting is a consolidation.** Extraction and specification want different granularities: the
   sweeps over-produce rows on purpose, and the page states one criterion per *rule*, with the
   fields, tokens or sites the rule ranges over as the `[DataRow]`s of its test — never one
   criterion per field. A family of schema mirrors folds into the rule they mirror; a doc-comment
   defect row is a fix without a criterion; a row another page owns is cited there, never
   re-minted. Aim for roughly half the classified rows as criteria, and record a
   **consolidation map** (row → criterion, or row → the line saying why it mints nothing) in the
   change doc, so no classified row vanishes silently — the critic checks it, and a row with
   neither is a blocker (230 rows became 135 criteria without losing one). A criterion you
   already know no test can reach carries its `GAP: <reason>` marker **on the delta line** —
   `spec-trace` honours it there exactly as on a page. **A criterion's text on the page and on its
   delta line are one text:** reword one, reword the other in the same commit (a `MODIFIED` line
   for a criterion already delta'd as `ADDED`) — `spec-change.ps1 archive` refuses a delta whose
   text the page no longer carries, and one pass reworded a criterion on the page with no delta
   line at all, so page and delta disagreed until review. An id hole that opens before the page is
   published — a leaf dropped for want of a reachable mutation, in the pass's own PR — is closed by
   renumbering the leaves below it and their citations in one pass over the page and the tests;
   after publication a hole is named in the change doc and never renumbered, because a consumer
   may cite the id.
2. **Write the mutation with the test, before the AC counts.** For every AC: the test, the one
   mutation of the code under it that reddens *that test and no other claim*, run and observed. An
   AC no mutation can redden is not a requirement — reword it to what *is* observable, merge it
   into the AC it converges on, or drop it; never mint it. An AC that two guards enforce is
   **over-determined**: say so on its line, mutate both, and keep it — the binary rule has no other
   verdict for it. The PR body carries the `test → mutation` list, one line each (§11). Two rules
   about *claims* ride on this step: a test cites a criterion for what the criterion's **text**
   states, not for the behavior the test happens to prove (`spec-trace` checks that an id exists,
   not what it says; three tests in one pass cited criteria that said something else — read the
   criterion against the assertion before citing it); and any claim that names a test as its
   guarantee — in a comment, a doc, a checkpoint — is read from that test's **call sites**, not its
   name (a test that feeds one of two parsers pins one parser, whatever it is called). An
   instruction to cite — in an amendment, in a brief — is not evidence the citation fits: re-read
   the assertion before adding the tag (an amendment once named the wrong test for a criterion and
   the session cited it unread). A behaviour that holds by accident of structure — two dictionaries
   that happen to be separate — has no reachable mutation until it is one line that states the
   rule: write that line, then mint. Two assertion shapes read green on exactly the case they
   exist to exclude: a substring test on a rendered number (`Contains("0 s")` holds on `"60 s"` —
   assert the field whole, `testing-conventions.md` §11) and a wall-clock bound standing in for a
   claim about behaviour where a virtual-clock delta or the branch's own effect is observable (§16;
   one pass wrote three and replaced all three). A test that
   pins an implementation *premise* rather than a criterion (two parsers agreeing on every vector)
   stays uncited by design, says so in its class summary, and is listed in the REPORT —
   `docs/spec-process.md` names the category; never mint a criterion to hang it on.
3. **Fix rows**: the test for the *correct* behavior, proven red against the pre-fix code (that red
   run is the defect proof — name it in the list), then the minimal fix. Disciplines a fix owes
   beyond its own lines: a fix that makes previously inert inputs *live* re-checks **every
   validator** over those inputs (the pass that unblocked measuring-point knobs left all six
   analyzers blind to them); a fix that names a defect **shape** sweeps the shape, not the symbol
   (grep for the expression, not the one call site you noticed); a guard added to a `switch`
   belongs to **every arm** — route every arm through one guard, or the arm you did not test skips
   it; a fix that says *match rule R* enumerates R's properties — membership, naming, each
   declaration level — and covers every one in the same round (one pass matched membership, then
   property naming, then class-level naming: three rounds for one rule). **A test's fixture carries
   the observable its assertion needs**: a block with no service members cannot show "the interface
   did not bind" through the service map — find the seam or the fixture that shows it before
   minting the test, and know that a generated seam can exist on one half of a contract only (the
   sender side, not the sink side), so a fixture on the quiet half tests the guard and not the path.
   **Size guard:** if a fix turns out non-local or design-bearing while you implement it, STOP and
   report — it becomes a `park` row or its own change doc, never a silent absorption. **Hedges are
   STOPs:** a brief that says *appears to … — verify* has named an assumption; when it fails,
   record the deviation in the change doc and ask, never improvise a different design in its place
   (one pass improvised "resolve by evaluating" when the promised AST was not there; evaluation
   short-circuits, so the check never reached the name it existed to find — resolution is a
   syntactic question, answered from a parse tree).
4. **Author the spec page**: current-truth prose + the AC declarations, frontmatter
   `trace: enforced`. Prose states rules, never rosters (a list of today's instances drifts;
   "grep-enumerable" does not). Rows the operator accepted without a test carry the `GAP: <reason
   or VION-nn>` marker on the declaring line — and if the reason is "no observable", the row is
   `out-of-spec`, not `GAP`.
5. **Rewrite the area's whole test suite** to `docs/testing-conventions.md` §9–17 — ids cited via
   the quoted-literal forms (§17), unmapped tests merged or deleted per their list, no assertion on
   log calls (§15). A `[DataRow]` merge is a rewrite of the assertion: re-derive the mutation after
   it, or the merge is a deletion (one dropped a criterion's own sentence). A scripted fixture edit
   asserts its match count, or it can be applied, reported and absent. Names and Triple-A markers
   are **gated**, not remembered:
   `pwsh scripts/test-style-lint.ps1` fails on any cited test with an article in its name or no
   markers (§12/§13) — run it **before** the REPORT and budget the rename round (it caught fourteen
   names in one pass that the session wrote after reading §12; the natural phrasing of an assertion
   carries an article). The round also invalidates every test name the change doc carries — the
   behavior table's Test column, the unmapped list, the mutation list: after it, resolve every
   `Class.Method` token in the doc against a declaration with a script, paste its count, and rewrite
   the stale cells from the declarations (a per-class declaration list before and after the round is
   what works; matching bodies by similarity does not on a rewritten suite — 104 stale names survived
   two rounds that way). §17's "no other string" includes an expectation array: read the ids off the
   artifact under test, never write them as literals in a `CollectionAssert`. A project you cite from
   without owning (another area's registry) is on the
   script's exempt list with its reason until that area's pass; **one citation never exempts a
   project**, and a file this pass *authors* in an exempt project conforms anyway — run the lint
   once with the exemption removed locally, fix what it lists, commit nothing of the exemption
   change. A fixture inserted above an attribute block steals the doc comment above the anchor
   (the new declaration gets two `<summary>` blocks, the old one none): `scripts/doc-comment-lint.ps1`
   fails the double; re-read the insertion point for the bare half. A test deleted because "another
   gate already covers this" names the gate **and its failure mode** in the commit — a snapshot that
   is regenerated and auto-committed gates nothing, and a warning-level diagnostic fails no build.
6. If the brief lists RFCs to absorb: delete them and run the reference sweep
   (`docs/spec-process.md` § Area passes, step 5). If it lists none, say so in the report. **Sweep
   discipline:** a scripted sweep deletes only the spans it matched and proves each rewritten line
   is the original minus exactly those spans (assert the match count, diff the rest); it reads and
   writes bytes with the file's own line endings (`newline=''`); and it applies no whole-file
   tidy-up after the edit — two sweeps in one pass reset the tree, one by stripping every empty `()`
   in the repo, the other by rewriting every CRLF file to LF. `scripts/sweep-residue-lint.ps1` names
   the shapes a sweep leaves behind (an orphaned `()`, a doubled space, a Markdown line ending on
   `(`); run it, then re-read every touched sentence anyway, because a regex sweep cannot see the
   sentence it leaves without a subject, the bare `§` pointing at a deleted file, or the stub a
   reflow left mid-paragraph. A file `grep` calls binary is skipped by every sweep — check with
   `grep -a` / `grep -c` before trusting a zero. Vendored
   files (their header says so) are exempt from the sweep; a cross-reference in a still-living RFC
   is re-pointed with one line, not absorbed.
7. If the area reaches the DevHost SPA (`Vion.Dale.DevHost.Web/wwwroot`): the change is
   **demonstrated**, not reasoned — `devhost-smoke` Tier 2 on a live host, evidence in the change
   doc — and if no committed fixture can show it, grow the SmokeHost with the member that can
   (`docs/devhost-conventions.md` § 1, `testing-conventions.md` § 6). **A Tier 2 row is a paste** —
   the page text or a described screenshot the browser tool returned — never a sentence composed
   from what the code should do. One pass recorded an observation that could not have occurred,
   because the export that produces the string was imported by nothing; the row read as evidence
   for a whole round. The observation is made through the UI's own controls — click the option,
   type in the box — never a scripted DOM write: a value set from a script reaches the DOM and not
   the model, and the verdict read back is about the row that was still there; open the raw view
   and read the model back before believing it (`docs/devhost-conventions.md` § 1).
8. Gates, all of them, results verbatim in the report — **every line a paste from the terminal,
   including the ones whose numbers did not move**; a number carried from an earlier run is stale by
   default and a composed one is the same defect (three recurrences in one pass): `dotnet build`
   (zero `DALE` warnings in the SDK's own build — a deliberately illegal fixture gets
   `#pragma warning disable` with its reason) + `dotnet test` on the full solution,
   `scripts/spec-lint.ps1`, `scripts/spec-trace.ps1`, `scripts/test-style-lint.ps1`,
   `scripts/doc-comment-lint.ps1`, `scripts/bom-lint.ps1`, `scripts/journal-lint.ps1`,
   `scripts/sweep-residue-lint.ps1`, `scripts/run-script-tests.ps1`,
   `/cleanup` once — and a run under CI's shape where a fixture asserts a build-time literal
   (`dotnet test <project> -p:Version=0.0.0-ci.1`: CI passes `Version` as a global property, and a
   project's own `<Version>` loses to it). A suite that gained a real-clock interaction — a runner
   loop, a captured console, a background host — runs **five times in a row**, every result line
   pasted; a pasted run count is still not a proof against load (one passed three runs and failed
   the reviewer's first), so the race is fixed, never outrun. Stryker.NET is
   **optional**: run it only where the test project references a single mutatable project (it
   cannot run otherwise — MTP runner in preview, multi-reference crash), read survivors by hand,
   never a gate or a score.
9. Distill every delta line into the page, then `pwsh scripts/spec-change.ps1 archive
   <code-lower>-pass` — it refuses a delta line whose id or text the page does not carry.
10. Commit per task, push, and STOP with the REPORT. Its **mandatory preamble is the self-check, in
    writing** — the coordinator's checks read it first:
    1. every gate line in the REPORT and the scorecard is pasted from the terminal, never typed;
    2. for every test added or changed, the cited criterion's *text* was read against the
       assertion — list the pairs;
    3. every sentence in the change doc that a later checkpoint disproved has been corrected in
       place or annotated;
    4. every Tier 2 row is a pasted observation;
    5. every number and tense in the sections above the append was re-read — an amendment appends
       to the change doc and nothing else re-reads what stands above it (a `37` for a 33-method
       file and a future-tense plan survived two rounds that way).

    Then: commands run + results · the test → mutation list · GAP list · premise tests left
    uncited, with reasons · park rows written to the ledger · friction one-liners (journal
    candidates, `docs/process-journal.md` format) · *no PR yet* — the coordinator runs the
    completeness critic and `/vion-code-review` first; the PR opens on the operator's go.

## After the REPORT — the coordinator's checks

Two fresh-context Opus subagents (`docs/spec-process.md` § Dispatching): a completeness critic and
an adversarial review of the branch diff, both reading every cited criterion's text against its
test. Findings return as one numbered amendment, and **the amendment is worked by a fresh session**:
the session that wrote Phase B retires at its REPORT, and the amend file is the fresh session's
brief (the fix-up shape — item, artifact, proof — with the same self-check preamble). Four passes
in a row showed the Phase B session producing amendment items done wrongly, with checkpoints that
did not read true — context depth degrades exactly the disciplines an amendment asks for — and each
needed a further fresh round; retiring at the REPORT costs one session's ramp-up and saves that
round. The coordinator closes the round with targeted reads of every item at its call site, and a
further Opus check runs only when a targeted read finds a blocker. A message reaches you only
when both sessions run in bypass mode; the amend file is the artifact and the message a
notification, and the coordinator reads delivery off your transcript (`docs/spec-process.md`
§ Checks and amendments) — the classification relay of Phase A arrives that way; an amendment
arrives as a brief.

An amendment's premise is a hypothesis until the tree confirms it: a check reading a branch at one
commit gets the shape right and the constants wrong (an "empty schedule" that was the framework's
60 s periodic event; a "CRLF file" that was LF). Verify the mechanism at the call site before
implementing, annotate a refuted premise in the checkpoint, and still test the behaviour the item
names when it is real — both refuted items of one round were worth doing. An item with two clauses
owes two proofs: do both and paste each, because the half without a number is the half that gets
skipped ("the mutation list **and the test map**" — the map stayed stale for a round).

## Scorecard (coordinator fills, into the change doc before the PR merges)

| Measure | Value |
|---|---|
| Gates (build/test/lint/trace/style/doc-comment/bom/self-tests/cleanup/CI) | green / what failed |
| Completeness-critic misses | count + rows added, by the sweep that should have caught them |
| Evidence errors found in review | count |
| Mutation evidence | named-mutation list complete? · over-determined criteria stated? |
| Operator corrections (table + PR) | count |
| Cost | sessions × model · amendments · wall time |

## Non-negotiables

| Temptation | Reality |
|---|---|
| "Fix it in Jira later" | A pass leaves code, tests and page consistent. Small and area-local → `fix` now; otherwise `park` in the ledger. Jira only when the operator schedules it. |
| "This fix is growing, I'll finish it anyway" | The size guard exists for exactly this: STOP, report, `park`. A "bug" that is really a feature band never rides a pass. |
| "This row is obvious, skip the evidence" | Unevidenced prose is this repo's most repeated defect class (journal, D10). Every row cites `file:line`; every harm names its consumer. |
| "The probe came back green, so my reading was wrong" | A probe is evidence for the shape it ran. Record the fixture shape and the surface; re-probe on the row's own shape before writing a reading off. |
| "The AC reads well, I'll find its mutation later" | An AC without a reddening mutation describes the code instead of constraining it. Mutation first, or it is not minted. |
| "The test proves this, so the id fits" | A citation is for the criterion's text. Read it against the assertion; if it says something else, reword the criterion (with its `MODIFIED` line) or cite the right one. |
| "I fixed the site I found" | Sweep the shape, re-check the validators the fix just made relevant, route every switch arm through the guard, cover every property of the rule. The site you found is the one the critic will not need to. |
| "The brief said verify — it didn't hold, so I'll do it another way" | A failed hedge is a STOP. Record the deviation, propose, wait. |
| "Another gate already covers this test" | Name the gate and its failure mode. Two passes deleted a test on that sentence and had to restore it. |
| "I'll write the Tier 2 row from the code" | A Tier 2 row is a paste. No paste, no row. |
| "The numbers didn't change, I'll carry them" | Paste every gate line, every time. |
| "The amendment told me to cite it" | A citation is read from the assertion, whoever asked for it. |
| "I merged the rows, the mutation is the same" | A merge rewrites the assertion; re-derive the mutation. |
| "Patch the discarded attempt's table by hand" | The next pass repays the same debt. Fix the skill, delete the branch, rerun. |
| "Enshrine it — the code clearly does this" | The code doing it is evidence of behavior, not intent. Surprising rows get `fix`/`park` or the operator's explicit `intended`. |
| "It was added last, so it is special" | Recency is not a reason to treat a member differently. A park rec argued from a member's history has no evidence column; classify it by its shape. |
| "One criterion per row keeps the map simple" | Extraction over-produces on purpose; the page states one criterion per rule with the fields as `[DataRow]`s, and the consolidation map is what keeps the rows. |
| "The sweep is done; a tidy-up regex will polish the rest" | A sweep deletes matched spans and nothing else. Two tidy-ups reset a tree. Run `sweep-residue-lint`, then read every touched sentence. |
| "I set the input's value from the script and read `valid`" | A scripted write reaches the DOM, not the model. Click the control; read the model back. |
| "The amendment says the schedule is empty" | A check's finding is a hypothesis. Verify the mechanism at the call site, annotate a refuted premise, test the real behaviour. |
| "I'll work the amendment myself, I know this code" | The session that wrote Phase B retires at its REPORT; a fresh one works the amendment. Four passes paid a round each for the alternative. |
| "Every row maps to a criterion, so the page is complete" | Ask the reverse per anchor: which observables have neither a row nor a criterion. Two reached a page that way. |
