---
slug: sdd-closeout
status: in-flight          # proposed | in-flight | parked | archived
blocked-on: none           # for parked docs: what's blocking + ref
areas: process             # the process itself, as in 2026-09-01-sdd-process.md
author: jonas.bertsch (drafted with Claude in the session `sdk: sdd big picture`)
created: 2026-09-07
updated: 2026-09-07
supersedes: none
---

# Closing the SDD migration: the corpus into steady state, the pass machinery into the standing process

> Change doc — one in-flight change. This doc changes no criterion; it is the **handover
> infrastructure** for a multi-session effort, the tracked successor of the coordinator's
> machine-local handoff brief. Every session that works a task below updates this file in the
> same PR. Process: `docs/spec-process.md`. Archived when the last task's row reads done.

## At a glance

### Summary

Fourteen area passes, the unification pass and the ledger review closed the migration to
`docs/specs/` on 2026-09-06. What is open is closing it out (the pass skill retired with its lessons
kept, the leftovers decided, the ledger and Jira dispositioned), turning the pass machinery into a
standing three-lane process with its tooling, running the first retro over the migration's data, and
releasing what the passes fixed. Four phases, nineteen tasks, one fresh session per task, the
operator at the STOP lines. Decided in the `sdk: sdd big picture` session on 2026-09-07.

### Spec implications

None on any criterion. The pages this doc changes are process pages: `spec-process.md`,
`testing-conventions.md`, `CLAUDE.md`, the commands under `.claude/commands/`, the gate scripts, and
the architecture repo's two lane commands and the SDK's substrate page. `_findings.md` shrinks as
the fix-now batch lands and the Jira-filed entries are struck with their keys.

### Decisions

- `D1` — **This doc is the handover.** Machine-local coordinator state (the 2030-line handoff brief,
  the `sdd-kit/` folder) is superseded; durable state lives here, per decision 0113. Sessions are
  titled `sdk: sdd closeout T-0NN`.
- `D2` — **Phase order: close the migration → standing process → retro-1 → release.** The release
  goes last on the operator's word ("no pressure right now"); no consumer is pressing.
- `D3` — **Three lanes by size, coordinator only in lane 3.** Lane 1: fix-sized, inline — the page
  edit rides the PR, a fresh-context review subagent, `/cleanup`. Lane 2: a change doc, one
  implementing session, a gate review on the PR. Lane 3: the pass shape generalised — a coordinator,
  a brief checked before dispatch, an implementer, two fresh-context checks, a fresh fix-up session.
  Triage happens at the start of the work, by three questions: does it change specified behaviour,
  does it cross an area, is a design point open. The passes measured why: implementer sessions ran
  at 2–8 MB with no compaction; the pass-7 coordinator ran to 28 MB with four.
- `D4` — **Issue lanes by origin, one owner per fact.** Consumer feedback → Jira through the
  `dale-sdk-feedback` skill (a consumer needs a key and a relay text). Engineering findings → the
  in-repo ledger, no Jira unless scheduled. Specified-behaviour changes → a page edit (fix-sized) or
  a change doc (feature-sized). Jira items cite spec ids and page sections, never `file:line`, because
  ids are stable and the trace gate keeps them alive; a GAP marker carries its `VION-nn`. This settles
  the founding doc's `D10`: no engineering epic beside VION-62 — the ledger is that home.
- `D5` — **Corpus leftovers: leave.** The `AC-CLI-006.10`–`.13` emitter family stays four rules;
  `AC-MODB-014.1` and `AC-SCEN-004.1/.5/.6/.7` stay; the `io.md` mirror test is not written; the 44
  unscreened composites are screened only when a change touches one. The one gate change is the
  trace gate's hole check narrowed to `REMOVED` lines (`T-003`).
- `D6` — **HTTP: fix 28n, decline 54.** The client ceiling's `TaskCanceledException` is normalised
  to `TimeoutException`; `[assembly: DaleSharedAssembly]` on the HTTP package stays declined and
  stated.
- `D7` — **Jira: close VION-133; file the five candidates with an external reader; the rest stay
  in the ledger.** `#46` (`DALE043` false error on a gated generated-interface property) and `#60`
  (`dale upload` telling the two 409s apart by message text) under VION-62; `#36` (the runtime's
  stale handler map), `#74` (`Vion.Contracts`' unusable generated verifiers) and `#75` (`hal-sim`'s
  transposed identity strings) under VION-16 with the `dale-sdk` label, routed to their repos; `#70`
  is VION-130 — link only. VION-132 gets a comment pointing at the BIND park on the same three lines
  rather than a bundle item. "External reader" names who observes the defect; no code moves.
- `D8` — **Conventions import from mesh is narrow:** the harness conventions, the commit-subject
  rule, the draft-PR gate record, the codify and retro commands. This repo's testing and comment
  docs stay the owners of their subjects; mesh's retro has never run, so its loop is a design.
- `D9` — **The skill retires with its lessons kept.** Every non-negotiable row and Phase B rule of
  `spec-pass` that is general moves to the doc that owns its subject or becomes a named review
  check; migration-only rules are dropped with the skill. The kit's check prompts become tracked
  commands if lane 3 keeps them (`T-002` decides where).
- `D10` — **No coordinator session for this effort.** This doc is the coordinator's memory; the
  operator is the coordinator at the STOP lines; each task is one fresh lane-2 session that
  dispatches its own review subagent. Sequential by default — parallel sessions need worktrees and
  disjoint rows in this file.
- `D11` — **Mesh-inspired commands are imported for their consistency, never for their stops.**
  The rules they enforce are kept — the commit-subject shape, the draft-PR gate record, the
  `→ codified:` stamp; the approval prompts on process steps (sign off each commit, confirm each
  grouping, confirm each codify row) are not. The operator reviews at the PR and at the STOP lines,
  not at every process step. Applies to `T-014`, `T-015`, `T-016` and to any later port. What this
  means for this repo's own commit stop (working agreement 2) is `T-013`'s to settle with the
  operator.

### Reviewer's questions

1. *(a — ratified)* The lanes, the issue lanes, the phase order and the Jira routing were decided in
   chat on 2026-09-07; cite, do not relitigate. OUTCOME: ratified.
2. *(c — propose-and-wait, `T-008`)* The public-API ratchet's scope: the two DevHost packages,
   `Vion.Dale.ProtoActor`, the four undeclared SDK namespaces and the 43 unmarked Modbus types are
   outside the manifest today. One decision record covers five ledger entries. OUTCOME: **include
   them all** (operator, 2026-09-07); `T-008` records the decision and implements it, no STOP.
3. *(c — propose-and-wait, `T-005`)* BOM policy for C#: 199 of 941 `.cs` files carry one and
   `bom-lint` skips `.cs`. Recommendation: normalise once and gate. OUTCOME: **normalise** once
   and gate `.cs` (operator, 2026-09-07); `T-005` has no STOP.
4. *(b — decide-and-document, `T-002`)* Where lane 3's check prompts live as tracked files.
   OUTCOME: the operator cannot decide yet (2026-09-07). `T-002` lands the two shapes as an
   appendix of the Lanes section — the cheapest place to move them from — and names the
   command-file option in its PR; decided there or later, never blocking `T-002`.
5. *(b — decide-and-document, `T-004`)* Whether the pragma-reason lint fails or warns on first
   landing. OUTCOME: **fails** from the first landing (the operator leans yes, 2026-09-07).
6. *(b — decide-and-document, `T-016`)* Whether `/vion-commit` is imported or only its subject rule.
   OUTCOME: **neither as-is** — the rule without the stops; generalised as `D11`
   (operator, 2026-09-07).
7. *(c — propose-and-wait, `T-019`)* The release version and its moment. Recommendation: `0.12.0`
   after retro-1. OUTCOME: agreed (operator, 2026-09-07); the tag itself stays the operator's.

---

## Full design

### Where the migration stands (main at `66dc32c`, 2026-09-07)

| What | State |
|---|---|
| Area passes merged | 14 (#161 PLUG · #164 EMIT · #166 GATE · #168 INTRO · #170 SCEN · #172 CTRL · #174 LIFE · #176 BIND · #178 ANLZ · #180 MODB · #182 CLI · #184 IO · #186 TKIT · #188 HTTP), unification #189, ledger review #190 |
| Skill versions | v1 #160, v2–v14 in #163 … #187, one per pass from the previous pass's friction |
| Corpus | 15 traced pages incl. `_invariants.md`, 1117 criteria, 67 GAP markers, `spec-trace` clean |
| Ledger | 77 entries, all reviewed with the runtime as reader on 2026-09-06 |
| Gates on every PR | `run-script-tests`, `spec-lint -Diff`, `spec-trace`, `test-style-lint`, `doc-comment-lint`, `bom-lint`, `journal-lint`, `sweep-residue-lint` (spec-gates.yml); the pack gate, snapshot bot and drift docs (publish.yml); `spec-change archive` on demand |
| Journal | 275 entries; 203 since 2026-09-01 (review 90 overall, agent 102, gate 38, brief 33); 5 second asks, 0 escapes; the retro-0 marker is the only one, never rotated |
| Jira epic VION-62 | 34 issues: 20 closed (17 Done, 3 Entfernt), 13 open, all 13 rewritten on 2026-09-06 and still valid — no pass resolved one |
| Releases | none since v0.11.2 (2026-08-31); every pass fix is unreleased |
| Machine-local residue | `C:\_gh\architecture\.claude\briefs\coordinator-handoff-sdd.md` (+ `-history.md`), `sdd-kit/` (~100 files: prompts, reports, close-out scripts), 25 pass/fix-up briefs and amend files, `dale-sdk/docs/superpowers/plans/` (ten dead plans from June–July) |

Read for this picture: the handoff brief's state head, decision lists, rules and scorecard; the
founding change doc; `spec-process.md`; the skill; the unification docs; the ledger; the journal
header and its entries by count; the three coordinator reports (consistency, findings, Jira); the
architecture repo's commands and decisions 0100–0102, 0113, 0114; mesh's and lbl's process substrate
by subagent. **Not read:** the middle 1400 lines of the handoff brief, the fourteen archived pass
docs, the Jira review beyond its first five items. The compaction counts below are a string count
over the transcripts, not a harvest (`T-011` harvests).

### What the passes measured, for the lanes

Sessions of the SDD window, from the transcript store on 2026-09-07 (heuristic; `T-011` replaces it):

| Session kind | Count | Transcript | Tool uses | Compactions |
|---|---|---|---|---|
| Pass sessions (Phase A + B) | 14 | 2.0–8.3 MB | 137–723 | 0 (one at 1) |
| Fix-up sessions (the fresh round after the checks) | 11 | 2.2–3.1 MB | 148–245 | 0 |
| Coordinator sessions | 4 | 6.2–28.2 MB | 318–1409 | 1–4 |

Wall time per pass fell from ~27 h (INTRO, with a 10 h stall) to ~3 h (TKIT, HTTP); critic misses
settled at 2–4 per pass; amendments at one per pass after v7 moved the amendment to a fresh
session; the brief check before dispatch corrected 13–18 claims per brief in 4–8 minutes.

### The phases and their tasks

Each task is one fresh session unless it says otherwise. Model rubric: Opus unless the work is
mechanical; High effort where the work is design-bearing. A task's **STOP** line names where the
operator decides; a task with none is reviewed on its PR.

**Phase 1 — close the migration.**

- `T-001` *(this PR, the `sdk: sdd big picture` session)* — this doc; the handoff brief marked
  superseded; the journal line; `D1`–`D10` recorded.
- `T-002` *(Opus, high)* — **skill retirement with lesson migration; `spec-process.md` for steady
  state.** Delete `.claude/skills/spec-pass/`. Sort every non-negotiable row and Phase A/B rule by
  owner: test-side rules (a test that pins an ordering runs its own mutation first; a `[DataRow]`
  merge re-derives the mutation; the fixture carries the observable; premise tests uncited by design)
  → `docs/testing-conventions.md`; spec-side rules (a citation is for the criterion's text; the GAP
  marker on the declaring line; page and delta are one text; the archive commit is the last touch;
  minting is a consolidation; a park is never folded) → `docs/spec-process.md`; check-side rules
  (every count pasted with its command; a finding's mechanism verified at the call site; a premise is
  a hypothesis; the reverse question per anchor) → named checks in `.claude/commands/vion-code-review.md`;
  migration-only rules (RFC sweeps, the pass's branch names) dropped. Rewrite `spec-process.md`:
  the sections *Area passes*, *Dispatching a pass*, *Checks and amendments* become one **Lanes**
  section stating `D3`, with lane 3 keeping the dispatch, brief-check, REPORT, two-check and
  fresh-fix-up shape as the general recipe; the corpus roster stays; the pass order paragraph goes.
  Decide question 4 (the kit's `critic-v10.md` / `review-v10.md` shapes → tracked files under
  `.claude/commands/` or an appendix of the Lanes section). Update `CLAUDE.md`'s read-before-write
  table (drop the spec-pass row; the spec-process row names lanes, not passes). Done when
  `grep -rn "spec-pass" --include=*.md` is clean outside `docs/changes/archive/`, `docs/retro/` and
  the journal, and the review subagent confirms every dropped rule is named as dropped.
- `T-003` *(Opus, medium)* — **two gate fixes, self-tests red first.** `spec-trace.ps1`'s hole
  check accepts only a `REMOVED <id>` line under `docs/changes/archive/` (today any mention, so the
  pass that minted an id already "explains" its hole). `test-style-lint.ps1`'s attribute-block
  regex handles a `]` inside a string (`[DataRow("Mode in ['Eco', 'Fast']", …)]` hides its test
  today).
- `T-004` *(Opus, medium)* — **pragma-reason lint.** A gate failing on `#pragma warning disable
  DALE…` with no reason on the line or the line above; the 23 bare sites get their reason, each read;
  self-test; wired into spec-gates.yml, **failing** from the first landing (question 5).
- `T-005` *(Sonnet, medium)* — **BOM normalisation for C#** (question 3: decided). Probe three
  files through `cleanup-code.ps1` to confirm cleanupcode leaves a BOM-less file alone, then one PR:
  the 199 files normalised, `bom-lint` extended to `.cs`, the ledger entry closed.
- `T-006` *(Opus, medium)* — **ledger buckets.** A script over `_findings.md` lists all 77 entries
  with a bucket and a one-line reason: *fix-now* (small, area-local, no decision), *decision*
  (`T-008`'s five), *Jira* (`D7`'s five, already decided), *leave*. The table goes into this doc's
  *Ledger dispositions* section. STOP: the operator rules on the buckets, not the lines.
- `T-007` *(Opus, high; one PR per 3–6 fixes, grouped by area)* — **the fix-now batch.** Each fix
  red-first with its named mutation, the page edited in the same PR (fix-sized lane), the ledger
  entry deleted. Size guard as in the passes: a fix that grows goes back to the ledger as a change
  doc candidate.
- `T-008` *(Opus, high)* — **the public-API ratchet covers everything shipped** (question 2:
  decided — the two DevHost packages, `Vion.Dale.ProtoActor`, the four undeclared SDK namespaces and
  the 43 Modbus types all join the manifest). Record the decision in the architecture repo
  (`/decision`, one paragraph of rationale: a consumer's build is otherwise the first thing that
  notices a removal), then the follow-through PR here — `PublicApiNamespace` declarations, the
  marks with `DALE014` asking for each, the manifest regenerated by the bot — and the five ledger
  entries closed. No STOP; the review reads the manifest diff.
- `T-009` *(Opus, medium; the operator present — every Jira write announced first)* — **Jira.**
  File the five candidates per `D7` in the intake skill's shape (≤250 words, Origin line, spec ids
  cited); link `#70` to VION-130; close VION-133 as `Wird nicht gemacht` with the decision-0021
  reasoning (the drafted closing comment is in the coordinator's Jira review, to be re-derived if the
  kit is gone); comment on VION-132 with the BIND-park pointer; strike the ledger lines with their
  keys in one docs PR.
- `T-010` *(Opus, high)* — **the HTTP decisions.** 28n: the ceiling's expiry arrives as
  `TimeoutException`, the message naming the bound only where the executor knows it; red-first test;
  `AC-HTTP-008.*` edited on the page in the same PR. 54: the decline stated on the page and the
  ledger line closed.
- `T-011` *(Sonnet, medium; machine-local plus one docs PR)* — **transcript harvest, then kit
  disposal.** Harvest every SDD-window session from `~/.claude/projects/C---gh-dale-sdk*` and
  `C---gh-architecture` into `docs/retro/2026-09-sdd-pass-data.md`: pass, role, start, end, wall
  time, messages, tool uses, compactions, transcript size — before the transcripts age out. Then
  delete the machine-local residue listed above, after `T-002` has copied whatever it keeps.

**Phase 2 — the standing process.**

- `T-012` *(Opus, medium)* — **`/check`.** `scripts/check.ps1` runs every gate spec-gates.yml runs,
  optionally build and test, and prints one pass/fail line per gate; `.claude/commands/check.md`;
  `spec-process.md` § Gates points at it. The passes pasted nine gate lines by hand each time.
- `T-013` *(Opus, medium)* — **the lanes made operable.** `spec-process.md` § Lanes completed with
  the triage questions, the session start (the pointer prompt below), the title scheme, the REPORT
  shape and the STOP convention; `CLAUDE.md`'s working agreement gains one line pointing there, and
  its commit stop (agreement 2) is re-shaped for lane sessions with the operator per `D11` — the
  operator wants fewer interaction stops on process steps, the PR as the review point;
  `.github/pull_request_template.md` with the pass PR body's shape: what a consumer sees (the relay
  notes), the gate lines pasted, the review round.
- `T-014` *(Opus, medium)* — **the PR gate.** `/vion-pr` ported from mesh under `D11`: refuses on
  `main` or a dirty tree, opens a draft PR, runs `/vion-code-review` in a fresh-context subagent
  with the model passed explicitly, records `Gate review N — reviewed at <sha>` as a PR comment with a
  status column, reruns with `since:<sha>`, never merges, and asks the operator nothing on the way —
  the findings and their dispositions are read on the PR; `/vion-code-review` gains the
  `since:<ref>` scope.
- `T-015` *(Opus, medium)* — **collect and retro.** `/vion-codify` ported under `D11` (journal
  lines on the branch → codify / already covered / wait, stamped `→ codified:`, the table proposed
  and applied in one pass, the operator ruling on the PR rather than row by row); `/vion-retro`
  ported and adapted to this repo's journal header (reader subagent over the window, clusters by
  count, the enforcement ladder DALE diagnostic > gate > review check > prose, ≤6 landings, the
  marker moved, the window rotated into `docs/retro/journal-<from>-to-<to>.md`, a metrics row, a
  dated note). Retro-0's D6 said wait for data; fourteen passes are the data.
- `T-016` *(Sonnet, medium)* — **conventions import.** `docs/harness-conventions.md` from mesh's
  `harness.md` (a file describes itself, one owner per rule, written to be copied); the
  commit-subject rule as a rule in the working agreement, enforced by whatever commits — not
  `/vion-commit`'s grouping-and-confirm flow (question 6, `D11`). Nothing from `testing.md` or
  `comment.md` wholesale.
- `T-017` *(Opus, medium; cwd `C:\_gh\architecture`)* — **the architecture side.** `/fix` and
  `/implement` gain a dale-sdk clause: briefs point at spec pages and cite ids, oblige a page edit for
  a fix-sized behaviour change and a change doc for a feature-sized one, and the REPORT names the
  ids touched; `docs/fix-workflow.md` says the same in a sentence; `libraries/dale-sdk.md` re-pointed
  from RFC 0005/0007/0015/0017/0018 to the spec pages that absorbed them; the drift loop's signals
  gain `dale-sdk/docs/specs/**` inside decision 0102's boundaries (flag, never edit).

**Phase 3 — retro-1.**

- `T-018` *(Opus, high; `T-015`'s command)* — **retro-1 over the migration.** The reader subagent
  over the journal from the retro-0 marker, the sixteen archived change docs' scorecards and
  `T-011`'s data; the seven questions below answered; promotions down the ladder, ≤6 landings; the
  `(escape)` marker's silence explained or the marker retired; the journal rotated; the metrics row;
  `docs/retro/2026-09-<dd>-sdd-migration-retro.md`. STOP: the landings are proposed; the operator
  rules.

**Phase 4 — the release.**

- `T-019` *(Opus, medium)* — **0.12.0.** Release notes assembled from the sixteen archived docs'
  *Relay notes for the PR body* sections; a migration note under `docs/migrations/` for every
  consumer-visible behaviour change (refusals moved to the caller, the inbound payload guard, new
  diagnostics, CLI defaults); STOP: the version and the tag are the operator's; then
  `docs/releasing.md`'s bump and the consumer relay for each VION-62 item the release touches; the
  substrate page's version note.

### Session protocol

Start a task in a **fresh session** with the repo as project folder, title `sdk: sdd closeout T-0NN`,
model and effort from the task line, `C:\_gh\architecture` read-only where the task reads there.
First message:

```
Read docs/changes/2026-09-07-sdd-closeout.md and execute task T-0NN. Follow ./CLAUDE.md and
docs/spec-process.md; their commit and approval rules win. Branch sdd-closeout/T-0NN-<slug>
from origin/main, open a PR, never merge. Before the PR: every gate spec-gates.yml runs (or
/check once it exists), /cleanup once, and a fresh-context read-only subagent running
/vion-code-review branch with this change doc as the spec. Update this doc's Implementation
state row and, on any divergence, its Drift checkpoints, in the same PR. Stop at every STOP line
and wait for the operator. End with the REPORT block the doc names.
```

The REPORT closes the session's last message, in a fenced block:

```
REPORT ▸ T-0NN · sdd-closeout
Status:      done | blocked | partial
PR:          <url, or none>
Deviations:  <numbered: what differs from the task and why — or "none">
Questions:   <numbered: what needs the operator — or "none">
Friction:    <journal candidates, one line each — or "none">
Next:        <the next task id, or the STOP awaiting the operator>
```

The doc is the durable record; the REPORT is for the operator's eyes. The operator merges. A session
that finds its task band-sized stops and says so: the task becomes its own change doc in lane 3,
never a silent absorption. Two sessions at once need two worktrees and disjoint rows in this file.

### Implementation state

| Task | Status | Session | PR |
|---|---|---|---|
| `T-001` | done | `sdk: sdd big picture` | #191 |
| `T-002` | in PR | `sdk: sdd closeout T-002` | <PR-URL> |
| `T-003` … `T-019` | to come | — | — |

### Ledger dispositions

_Filled by `T-006`; ruled on by the operator; consumed by `T-007`, `T-008`, `T-009`._

### Skill retirement — where every rule went (`T-002`)

`.claude/skills/spec-pass/SKILL.md` is deleted. Every rule it carried is below with its new owner, or
named as dropped with the reason. The groups follow the skill's own structure; the *Owner* column
names the file and section that states the rule now, and *lane 3 § N* is
`spec-process.md` § Lanes → *Lane 3*, step N.

**Phase A — extraction.**

| Rule | Owner |
|---|---|
| The brief is a pointer; scope and test scope as folders and projects the session enumerates itself; a Drift checkpoint where the brief differs; an empty anchor kind is a checkpoint, not a row | lane 3 § 1 |
| A brief's counts are hypotheses — a fresh-context `Explore` subagent checks the brief before dispatch | lane 3 § 1 |
| A hedge in a brief (*"appears to … — verify"*) is a STOP | lane 3 § 1 |
| The launcher recipe, the model rubric, the per-process permission mode, `-AmendFile` round-trips | lane 3 § 2 |
| Scaffold with `spec-change.ps1 new`; the change doc is implementer-owned | § Change docs; § Lanes → lane 2 |
| Anchor inventory first; `AttributeTargets` read against the targets its readers walk | lane 3 § 3 |
| The statement, consumer, edge-value and state-interaction sweeps, with the symmetric-mechanism prompt and the fail-closed walk | lane 3 § 3 |
| The six-column table; Behavior altitude; Evidence and the probe rule; the five `Rec` values; `⚠` plus failure sketch; a harm names its consumer; recency is not a reason; the code is evidence of behavior, not of intent | lane 3 § 3 |
| Map every existing test in scope; the unmapped list | lane 3 § 3 |
| The self-check against the inventory and the PublicApi manifest; the reverse question per anchor, and of the doc's own reviewer's questions | lane 3 § 3; `vion-code-review.md` § 6 `P2` |
| The extraction STOP's report shape | lane 3 § 3 |
| The operator gate; the second opinion that opens the readers' repositories; the test for a `propose` row | lane 3 § 4; `vion-code-review.md` § 6 `P4` |

**Phase B — implementation.**

| Rule | Owner |
|---|---|
| Minting is a consolidation; the consolidation map; a park is never folded into a criterion | § IDs & EARS |
| `GAP` binds on the declaring line; an unmarked id is a declaration; a criterion the suite cannot reach stays `GAP`; "no observable" means drop the row, not mark it | § IDs & EARS |
| A citation is for the criterion's text; an instruction to cite is not evidence; a claim naming a test is read from that test's call sites | § IDs & EARS; `testing-conventions.md` §17; `vion-code-review.md` § 2 |
| Page text and delta line are one text; the archive commit is the last touch; id holes renumbered before publication, named after it | § Change docs |
| The mutation is written with the test; no mutation, no criterion; over-determined criteria say so; a behaviour true by accident of structure gets its line first | lane 3 § 5 |
| A surviving mutation indicts the fixture; a `[DataRow]` merge re-derives it; a test pinning an ordering, a bound or an edge runs its own mutation before it is cited | `testing-conventions.md` §11 |
| A window no seam constructs is reported as a surviving mutation, with the observable that is carried | lane 3 § 5 |
| Fix rows red-first; the four disciplines a fix owes — every validator, the shape not the symbol, every `switch` arm, every property of the rule | lane 3 § 5; `vion-code-review.md` § 6 `P3` |
| A fix's siblings are swept before the REPORT; a behaviour the classification adds was never swept | lane 3 § 5; `P3` |
| The fixture carries the observable; a generated seam can exist on one half of a contract only | `testing-conventions.md` §11 |
| The size guard; a deviation checkpointed with the criterion it rests on and the test that carries it | lane 3 § 5 |
| The page states rules, never rosters | lane 3 § 5 |
| The suite comes to §9–17; `test-style-lint` before the REPORT; the exemption list, and a file you author inside an exempt project | lane 3 § 5; `testing-conventions.md` §12 |
| The rename round invalidates every test name the doc carries; resolve both token shapes with a script and paste the counts | lane 3 § 5 |
| A scripted fixture edit asserts its match count | lane 3 § 5 |
| A test deleted because another gate covers it names the gate and its failure mode | `testing-conventions.md` §9 |
| A fixture inserted above an attribute block steals the doc comment above the anchor | lane 3 § 5 (`doc-comment-lint`) |
| A DevHost change is demonstrated; a Tier 2 row is a paste, made through the UI's own controls | `devhost-conventions.md` § 1; lane 3 § 5; `vion-code-review.md` § 6 `P4` |
| Sweep discipline: matched spans only, the file's own line endings, no whole-file tidy-up, `sweep-residue-lint` and then re-read; binary-looking files; vendored files; the mirror diff covers the files the change itself created | lane 3 § 5 → *Sweep discipline* |
| Gates pasted verbatim, every line, every time; the CI-shape run; zero `DALE` warnings; five runs for a new real-clock interaction; Stryker optional | lane 3 § 6; `testing-conventions.md` § 8 and §16 |
| Distill, then `spec-change.ps1 archive` | § Change docs; lane 3 § 5 |
| The REPORT's seven-item self-check preamble, and the REPORT's contents | lane 3 § 6 |
| Every count is pasted with the command that produced it | lane 3 § 6; `vion-code-review.md` § 6 `P1` |

**After the REPORT.**

| Rule | Owner |
|---|---|
| Two fresh-context Opus checks, run concurrently; both read the citations, the self-check preamble, the `OUTCOME` lines, the rename markers and the relay-notes section | lane 3 § 7 |
| The checks run after the session's own sibling sweep, never instead of it; a REPORT's numbers are read against the head they were taken at | lane 3 § 7 |
| A finding is a hypothesis until the tree confirms it; the mechanism is verified at the call site; an amendment's count carries its command; a two-clause item owes two proofs | lane 3 § 7; `vion-code-review.md` § 4 |
| One numbered amendment per round, worked by a **fresh** session; the coordinator's targeted reads close it | lane 3 § 7 |
| The relay, both directions: a REPORT that arrives as a message is saved from the message and said so; outbound, the amend file is the artifact, bypass mode at both ends, delivery verified in the transcript, the two watch signals | lane 3 § 7 |
| The scorecard | lane 3 § 8 |
| The critic and review prompt shapes (kit `critic-v10.md` / `review-v10.md`) | lane 3 → *Appendix — the two check prompts* (question 4) |

**Dropped, with the reason.**

| Rule | Why it is dropped |
|---|---|
| The branch name `spec-pass/<code-lower>-a<N>`, and the attempt number in the brief | Migration-only: numbered attempts of a fourteen-area sequence. Lane 3 has no attempt counter. |
| "Attempts are disposable — **fix the skill**, delete the branch, rerun" | Only the skill half is dropped: there is no skill to fix, and a lane-3 round's process correction now lands in `spec-process.md`. The general half — a discarded round is rerun, never hand-patched — is kept in lane 3's opening. |
| "RFCs to absorb" in the brief; the RFC deletion plus reference-sweep step; and its ledger clause (a line absorbing a deleted RFC's item names the deleted origin and points at the absorption heading, never a `§` anchor into the file that is gone) | `docs/rfcs/` is gone — the `TKIT` pass absorbed the last two. The **sweep discipline** inside that step is kept in full; the RFC-specific sweep is not, and the ledger clause has no RFC left to absorb. The general reading it rests on — a `§` pointing at a deleted file is residue no regex sees — survives in *Sweep discipline*. |
| The pass order (fourteen areas, in sequence) and "un-passed areas live under the old rules until their pass" | The migration is complete: every roster area has a `trace: enforced` page. |
| "In an area whose pass hasn't run yet, a feature-sized change's distill creates a partial page", and the same clause in `CLAUDE.md`'s working agreement 10 | Same reason: no un-passed area is left for the clause to fire in. |
| "Learning between passes lands as skill diffs, never as longer briefs" | There is no skill to diff. `/vion-codify` plus the retro is the replacement loop (`T-015`). |
| Stryker's "two of fourteen areas" count | The rule is kept; the migration-era count is not a standing fact. |
| The Tier B wording "both passed so far (`CLI`, `IO`)" | Stale — all four Tier B areas passed. Replaced by what the tier means now. |

### Retro-1's questions

1. **Compaction versus handover.** The pass-7 coordinator compacted four times; the implementer
   sessions never did. Did output quality move with a compaction (journal lines by time, amendment
   counts), and does the fresh-session-per-round rule (v7) explain the fall from six amendments
   (GATE) to one?
2. **The brief check's yield.** 13–18 corrections per brief for 4–8 minutes: a standing lane-3 rule,
   or a lane-2 rule too?
3. **Which checks pay.** Critic misses 2–4, review blockers 1–3, the second opinion's reader reads
   flipping 3–5 rows per pass: which of the three belongs in lane 2's single review?
4. **Where corrections concentrated.** The 90 review lines by cause (typed numbers, citation-text
   mismatches, carried counts, sweep residue, orphaned doc comments): which became gates, which
   remain prose, which recur despite a gate.
5. **Stalls.** A held relay (INTRO, 10 h), an API loss (CTRL, 2 h 45 min), a hung turn (CLI, 6 h): do
   the watch rules matter once there is no coordinator?
6. **Cost.** Sessions × model per pass and per round, from `T-011`'s data.
7. **The skill's cadence.** Fourteen versions in six days, each from the previous pass's friction:
   can codify plus retro keep that loop closed without a per-pass skill bump?

---

## Drift checkpoints

- **`T-002`: `CLAUDE.md` had no spec-pass row to drop.** The task line says "drop the spec-pass row;
  the spec-process row names lanes, not passes". The read-before-write table carries only the
  spec-process row, which named area passes; that row now names the lanes, and nothing was removed
  from the table.
- **`T-002`: the `spec-pass` grep is clean except in this doc.** The done-criterion excludes
  `docs/changes/archive/`, `docs/retro/` and the journal. This change doc is the one remaining match
  — it names the skill it retires in `T-002`'s task line, in `D9`, and in the record above — and it
  moves under `archive/` when `T-019` closes the effort, at which point the criterion reads clean as
  written.
- **`T-002` corrected two stale clauses beside its own edits.** `spec-process.md`'s Tier B sentence
  claimed two of four Tier B areas had passed, and `CLAUDE.md`'s working agreement 10 branched on
  un-passed areas. Both are dead facts post-migration; both are listed in the *Dropped* table above.
- **`T-002`'s doc rewrite left two files disagreeing with it, and both were corrected here.**
  `scripts/test-style-lint.ps1`'s header comment and `docs/specs/_findings.md`'s header both framed
  their subject as something an *area pass* does "until their pass" — true when the doc said so, false
  the moment it stopped. Comment and prose only; no gate behaviour moved (`T-003` and `T-004` own the
  gate changes).
- **`T-002` placed two rule groups outside the files the task line named.** The Tier 2 paste rule
  went to `devhost-conventions.md` § 1, which already owns the scripted-DOM-write rule beside it, and
  the four standing checks became `vion-code-review.md` § 6, numbered `P1`–`P4` so a finding can cite
  one the way it cites a `D`-number rather than being appended to the lead's mined taxonomy, whose
  provenance is a dated mining round.

---

## Spec delta (to distill)

> No criterion changes: this doc's deltas are process documents, each a task above. The archive
> gate has nothing to compare; the doc archives when the Implementation state reads done for every
> task, by the session that lands `T-019`.

---

## Tasks

> The nineteen tasks are enumerated with their lanes, models and STOP lines under *The phases and
> their tasks*; the completion record is the Implementation state table plus each PR.

- `T-001` the handover (this PR) · `T-002` skill retirement and `spec-process.md` steady state ·
  `T-003` two gate fixes · `T-004` pragma-reason lint · `T-005` BOM policy · `T-006` ledger buckets ·
  `T-007` the fix-now batch · `T-008` the public-API ratchet's scope · `T-009` Jira · `T-010` the HTTP
  decisions · `T-011` transcript harvest and kit disposal · `T-012` `/check` · `T-013` the lanes made
  operable · `T-014` the PR gate · `T-015` collect and retro · `T-016` conventions import · `T-017`
  the architecture side · `T-018` retro-1 · `T-019` the release
