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

None **on the criteria this doc's own decisions touch** — the pages it changes are process pages:
`spec-process.md`, `testing-conventions.md`, `CLAUDE.md`, the commands under `.claude/commands/`, the
gate scripts, and the architecture repo's two lane commands and the SDK's substrate page.
`_findings.md` shrinks as the fix-now batch lands and the Jira-filed entries are struck with their
keys.

**Corrected by `T-007`:** the fix-now batch necessarily moves criteria, because a lane-1 fix carries
its page edit. It minted `AC-CTRL-014.6`, `AC-CTRL-014.7` and `AC-CLI-019.3` and reworded
`AC-ANLZ-018.3`. Lane 1 owes no delta line — the page edit riding a fix *is* the distill
([`../spec-process.md`](../spec-process.md) § Lanes) — so this doc's *Spec delta* stays empty by
design; what was wrong was the flat claim above, not the absence of a delta.

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
   **Corrected by `T-006` and `T-008`:** five entries are six, "four undeclared SDK namespaces" is
   seventeen, and the set named here is 388 unmarked public types across **nine** packages, two of
   which (`Vion.Dale.DevHost.Xunit`, `Vion.Dale.Plugin`) this question does not name — see *Drift
   checkpoints*. The ruling is unchanged and is recorded as architecture decision **0145** for the
   whole release roster; `T-008` implemented the Modbus half and left the rest in the ledger with its
   measured size, because the whole is seven package-sized passes.
3. *(c — propose-and-wait, `T-005`)* BOM policy for C#: 199 of 941 `.cs` files carry one and
   `bom-lint` skips `.cs`. Recommendation: normalise once and gate. OUTCOME: **normalise** once
   and gate `.cs` (operator, 2026-09-07); `T-005` has no STOP.
   **Corrected by `T-005`:** the pair was 195 of 946 at the commit this doc cites and was never
   true as written — see *Drift checkpoints*, which also records that the ledger entry the task
   line asks to close does not exist.
4. *(b — decide-and-document, `T-002`)* Where lane 3's check prompts live as tracked files.
   OUTCOME: the operator cannot decide yet (2026-09-07). `T-002` lands the two shapes as an
   appendix of the Lanes section — the cheapest place to move them from — and names the
   command-file option in its PR; decided there or later, never blocking `T-002`.
   **Unchanged after `T-002` (operator, 2026-09-07): still open.** The shapes stay in the Lanes
   appendix; the alternative named in PR #192's body — two tracked commands,
   `.claude/commands/spec-critic.md` and `.claude/commands/spec-review.md`, taking the `<…>` parts
   as arguments — is the operator's to decide at `T-014` at the latest, which touches the review
   command anyway.
5. *(b — decide-and-document, `T-004`)* Whether the pragma-reason lint fails or warns on first
   landing. OUTCOME: **fails** from the first landing (the operator leans yes, 2026-09-07).
6. *(b — decide-and-document, `T-016`)* Whether `/vion-commit` is imported or only its subject rule.
   OUTCOME: **neither as-is** — the rule without the stops; generalised as `D11`
   (operator, 2026-09-07).
7. *(c — propose-and-wait, `T-019`)* The release version and its moment. Recommendation: `0.12.0`
   after retro-1. OUTCOME: agreed (operator, 2026-09-07); the tag itself stays the operator's.
8. *(b — decided under `D4`, raised by `T-002`)* `_findings.md`'s header now says any lane adds to
   it — is the widened scope wanted? OUTCOME: **yes** (operator, 2026-09-07). Engineering findings
   from any lane go to the ledger, no Jira unless scheduled; `T-006` buckets the ledger's entries as
   planned (77 at the time of the ruling; 76 once `T-003` deleted the one it fixed, 75 after
   `T-004`).

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
  *(Landed as 195 files, and there was no ledger entry — see *Drift checkpoints*.)*
- `T-006` *(Opus, medium)* — **ledger buckets.** A script over `_findings.md` lists every entry
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
  *(Landed as decision 0145 plus the Modbus half — two of the six entries closed. The other four
  are 347 unmarked public types across six packages and stay in the ledger with their counts and
  the decision cited; see *Drift checkpoints*.)*
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
  delete the machine-local residue listed above, after `T-002` has copied whatever it keeps; the
  dispatch mechanics live in the `vion-dispatch` plugin's README now, so the deletion loses nothing.

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
| `T-002` | done | `sdk: sdd closeout T-002` | #192 |
| `T-003` | done | `sdk: sdd closeout T-003` | #193 |
| `T-004` | done | `sdk: sdd closeout T-004` | #194 |
| `T-005` | done | `sdk: sdd closeout T-005` | #195 |
| `T-006` | done — buckets ruled by the operator | `sdk: sdd closeout T-006` | #196 |
| `T-007` | done | `sdk: sdd closeout T-007` | #197 |
| `T-008` | done — **partial by measurement**: decision 0145 recorded and the Modbus half landed; the four larger entries stay in the ledger with their counts | `sdk: sdd closeout T-008` | #198 |
| `T-009` | done | `sdk: sdd closeout T-009` | #199 |
| `T-010` | in PR — 28n fixed and `AC-HTTP-008.2` rewritten; row 54 re-read and unchanged | `sdk: sdd closeout T-010` | #200 |
| `T-011` … `T-019` | to come | — | — |

### Ledger dispositions

_Filled by `T-006`; ruled on by the operator; consumed by `T-007`, `T-008`, `T-009`._

**69 entries at `4641b6f`; 75 when this table was recounted at `6d19f75`** — reviewer's question 8's
arithmetic holds (77 at the ruling, 76 after `T-003`, 75 after `T-004`, untouched by `T-005`; **72 after `T-007` deleted the three rows
it fixed, then 73 when its own gate found one on its first CI run**; **71 after `T-008` deleted the two
the Modbus half resolved, then 72 when arming the analyzer found one and 75 when its review round found
three more**; **69 after `T-009` struck the six `Jira` rows' entries** — the checker reports
`69 entry(ies) … 80 row(s), 11 resolved`). All five rows added since
the ruling are marked as having entered after it: the operator has not bucketed them, `T-007` and
`T-008` have. Every entry is below exactly
once, keyed on its bold lead rather than its position, because a triage outlives the deletions it
causes — and a **renamed** lead orphans its row exactly as a deleted one does, which is how `T-008`'s
two recounted leads announced themselves. Both rows carry the new lead.
`pwsh -File scripts/ledger-buckets.ps1` checks the table against `_findings.md` and prints the
tallies; `-List` re-seeds it. A row whose entry is gone reads as *resolved*, not as an error — that
is `T-007`'s, `T-008`'s and `T-009`'s progress through this table.

**What each bucket means, and what it does not.** `fix-now` is the task line's *small, area-local, no
decision*, read against `spec-process.md` § Lanes' three questions — does it change specified
behaviour · **does it cross an area** · is a design point open — plus two tests `T-007`'s own recipe
imposes: it must be **provable red-first**, and it must **add no published surface** (a new public
member is a ratchet move, not a repair). `decision` is `T-008`'s public-API ratchet family and
nothing else. `Jira` is `D7`'s routing. `leave` is everything else, and it does **not** mean "needs no
ruling": most `leave` rows are open questions that stay in the ledger until a retro schedules one,
which is the ledger header's own loop. Each reason names which test took the entry out of `fix-now`.

| Area | Entry | Bucket | Why |
|---|---|---|---|
| `INTRO` | `dale list` cannot render a nested block's short name. | leave | The CLR-form identity is deliberate (`AC-INTRO-004.1`); what the table should print instead is a `dale list` output decision, not a repair. |
| `INTRO` | Nothing warns when a library's `<PackageId>` and `<AssemblyName>` diverge after the identity change. | leave | A new `dale build` / `dale pack` warning is specified CLI behaviour that does not exist yet — feature-sized, and its reader is the platform's registration. |
| `INTRO` | `dale list` runs the introspection without the development-only exclusion | leave | Filter or mark is a question about what `dale list` is for; the entry defers it to the CLI being specced. |
| `INTRO` | A blank or a colliding endpoint `Identifier =` draws no compile-time diagnostic. | leave | A whole-type analysis across two attribute families that would fire on eight deliberate fixtures; it failed the size guard in two passes. |
| `CTRL` | A clock-mode switch rebuilds the next generation by writing the process environment. | leave | Both alternatives are worse or bigger: a runner-held static is the same global renamed, a mode parameter is a surface change. |
| `CTRL` | The duration converter's read half answers 500 where every other bad body answers 400. | fix-now | One `Read` body, one escaping exception class, `CTRL`-local and directly testable — but the entry's `:107-123` is stale (the file is 40 lines, `Read` is `:18-33`), the converter is registered twice (`WebHostService.cs:87` MVC, `:104` SignalR, where there is no 400 to answer with), and the 400 rule is prose today, so the page edit **mints** a criterion. |
| `CTRL` | A topology's validation errors are served by splitting a joined message. | leave | The fix is a structured exception on the topology types, which are `SCEN`'s — a second area. |
| `CTRL` | A client that connects before the first generation's actors exist is never primed. | leave | A readiness gate on the hub is new behaviour, not a repair. |
| `CTRL` | The scenario and topology routes refuse without a reason token. | leave | Extending the token family is a wire decision the Explorer's client would key on. |
| `CTRL` | Three shipped packages are outside the public-API snapshot. | decision | `T-008` — the DevHost packages, named in reviewer's question 2; `T-008` measured them at 115 unmarked types across three packages and left them here. The lead was renamed when `Vion.Dale.DevHost.Xunit` turned out to be a third. |
| `LIFE` | Two published message types nothing in this repository sends or receives. | leave | Both ends are live in the private runtime; nothing here can change them or test one. |
| `LIFE` | A contract handler's reference is minted whether or not the actor exists. | leave | A registry lookup at link time is a spawn-ordering contract shared with the runtime. |
| `LIFE` | A `[Persistent]` property more than one level inside a block is silently not persisted. | leave | Recursing needs a cycle guard, a key grammar for arbitrary depth and a decision about collections — its own change doc. |
| `LIFE` | A block's actor name is ambiguous when its name or identifier contains the separator. | leave | Every reader prefix-matches, the runtime included, so there is no observable to prove red. |
| `LIFE` | The two dependency-injection registrations are independent, and one in-repo host uses only one. | leave | Whether an actor system without the SDK's registrations is a supported composition is a decision. |
| `LIFE` | A bound service the configuration gives no identifier is dropped at six sites for the instance's life. | leave | Failing the configuration instead needs the cloud's allocation rule; `AC-LIFE-003.4` states today's behaviour. |
| `LIFE` | A block whose configuration failed still starts, publishes and acknowledges. | leave | Three refusal shapes, each with a reader — one of them stalls the gateway's whole boot. |
| `LIFE` | The development host restores nothing, so the start hook's persisted-value promise is one it cannot keep. | leave | Whether the host sends an empty restore for sequence parity is the host's decision. |
| `LIFE` | `Vion.Dale.ProtoActor`, and three namespaces of `Vion.Dale.Sdk`, are outside the public-API snapshot. | decision | `T-008` — `Vion.Dale.ProtoActor` and the undeclared SDK namespaces; decided by decision 0145 and left here with its measured size (12 types across two packages, plus 47 of the SDK's 217). |
| `BIND` | Two published code-generation attributes have no reader in any repository. | leave | A public-surface removal that also rewrites shipped example files, entangled with the `Examples/` packaging row below. |
| `BIND` | The two discoveries of this area do not share a rule, and the contract side is the one out of step. | leave | Converging on the runtime's degrading scan reshapes a stated refusal across `BIND` and `PLUG`. |
| `BIND` | The contract-message envelope takes any payload while the inter-block one takes a struct. | leave | Adding the constraint narrows a published generic, and nothing behind the laxity goes wrong today. |
| `BIND` | A contract mapped to a handler class the host never spawned sends into nothing. | leave | The contract-side half of `LIFE`'s row 26 — one fix, and it is the runtime's. |
| `BIND` | A second registration request registers again, while the client aborts the duplicate. | leave | Unreachable: the runtime sends the request exactly once, at boot. |
| `BIND` | `RegisterServiceProvider` is a member-less published record with no reader anywhere. | leave | Deleting it is a public-surface removal, the same class as the code-generation attributes. |
| `BIND` | A cast integer reaching the multiplicity token conversion fails a whole pack run. | leave | Two shapes (a guarded walk or a `DALE` rule), and what the introspection walk owes a member it cannot read is `INTRO`'s to state. |
| `BIND` | A contract-type token has no uniqueness guard. | leave | `DALE043`-sized whole-compilation analysis, owing `CompilationEnd` and a referenced-assembly boundary decision. |
| `BIND` | A block's interface endpoints come from public properties only, and the walk cannot be widened without moving every consumer's artifact. | leave | Widening mints endpoints into every consumer's uploaded document; `AC-BIND-001.3` refuses the declaration instead. |
| `BIND` | The TestKit maps a contract an inclusion gate would have excluded. | leave | Reading a gate means running the binder's evaluator — `BIND`'s surface, parked twice already. |
| `BIND` | Seventeen namespaces of `Vion.Dale.Sdk` are outside the public-API ratchet, not four. | decision | `T-008` — the undeclared SDK namespaces; decided by decision 0145 and left here at 217 unmarked author-declared types, of which 97 under `Examples.*` the decision rules out of scope. The lead was renamed to carry the count. |
| `BIND` | Eight example contract files and one example logic block ship inside the SDK assembly. | leave | A packaging decision — whether they ship at all — which question 2's ruling on the ratchet does not settle. |
| `BIND` | `RegistrationSecret` belongs to no roster area. | leave | A corpus-roster ownership question with no behaviour behind it. |
| `BIND` | A handler that survives a reconfiguration with no contract mappings keeps a stale map. | Jira | `D7`'s runtime candidate (VION-16, `dale-sdk` label); no SDK change can cure it — the message that would is one the runtime does not send. **`T-009`: filed as VION-196 (VION-16, `dale-sdk`); the ledger entry is struck.** |
| `BIND` | `Vion.Dale.Sdk.Reflection.AssemblyExtensions.GetConcreteType` now has no caller. | leave | A surface removal in a namespace still outside the ratchet: `T-008` armed the Modbus packages, not `Vion.Dale.Sdk`, so `Reflection` waits on the `BIND` row above with the other sixteen. |
| `ANLZ` | A preset attribute is judged by every rule but `DALE019`. | leave | Widening the match to the base chain re-aims all 36 analyzers at once, at every consumer with preset attributes. |
| `ANLZ` | A relation-bearing component declared on a base block in a referenced assembly draws no warning. | leave | `AC-ANLZ-002.3`'s stated boundary, not a defect in the rule. |
| `ANLZ` | `[StructField]` on a parameter other than a wire struct's constructor parameter is judged by nothing. | leave | Narrowing further is source-breaking on a published attribute — the shape `DALE047` was minted to avoid. |
| `ANLZ` | A `MinInterval` at the tick-representation boundary configures a negative interval, unreported. | fix-now | One comparison, in the analyzer and the runtime, that must move together; a token drawing no diagnostic and making the emission gate unconditionally true is a red-first test. |
| `ANLZ` | `DALE046` judges a struct type only on its first occurrence in a wire graph. | leave | No shape makes the outcome differ, so there is nothing to prove red. |
| `ANLZ` | A package packed without the analyzer assembly loses all forty-six diagnostics in silence. | fix-now | The entry names the gate and the assertion: one check in `verify-packed-assembly-versions.ps1`, which was minted for this failure class. **`T-007`: the lead is refuted — such a package fails every consumer's build with `CS0006`, it is not silent; the check landed anyway and the entry is resolved. See *Drift checkpoints*.** |
| `ANLZ` | A `PackagePath` ending in a separator packs two different artifacts by runner. | leave | **Entered the ledger after the ruling**, found by `T-007`'s own gate on its first real CI run — so it is bucketed here rather than ruled on. Dropping the trailing separator changes a released package's layout, which is `releasing.md`'s subject and not fix-sized; nothing is broken today, since NuGet resolves both forms. |
| `ANLZ` | The generator's `Contract`-substring predicate runs on every class in every compilation. | leave | No functional observable; measuring the cache cost needs a build-time benchmark. |
| `ANLZ` | An inclusion gate on a property typed as a generated contract interface draws a false error. | Jira | `D7`'s VION-62 candidate: `DALE043` is an error, so it fails a consumer's build rather than nagging in it. **`T-009`: filed as VION-194; the ledger entry is struck. The escalation line's "five gating suites" is three, and no fielded gate is bitten today — all 29 carry an explicit `[LogicBlockInterfaceBinding]`, which makes them gateable before the broken lookup runs. See *Drift checkpoints*.** |
| `ANLZ` | `AnalyzerReleases.Shipped.md` / `Unshipped.md` do not exist and `RS2008` is suppressed. | leave | Adopting release tracking is an open decision the surface conventions deliberately do not take. |
| `ANLZ` | `LogicClassGeneratorERR` has no registry id. | leave | A question about the diagnostic surface; its four sites all fire on the SDK's own build going wrong. |
| `ANLZ` | `DALE013` fires on a documented `[PublicApi]` in a project that generates no documentation file. | leave | Entered after the operator's ruling: found by `T-008`'s review round. No shipped package is affected (all thirteen set the property); the cheap fix — setting it on the armed test projects — changes what those whole projects warn about. |
| `ANLZ` | An analyzer `ProjectReference` flipped to `ReferenceOutputAssembly="true"` is caught by nothing. | leave | Entered after the operator's ruling: found by `T-008`'s review round, and **pre-existing** — the HTTP and `TKIT` passes added the same reference without a guard either. The honest fix is a required-absent rule in the packed-artifact gate, which is that gate's own change. |
| `ANLZ` | The manifest generator's type scan does not see a `delegate`. | leave | Entered after the operator's ruling: found by `T-008` arming `DALE014` over a package that ships a public delegate. Nothing is published as one today, and closing it is a generator change plus that script's first test harness — bigger than the marks that found it. |
| `MODB` | The default outcome for an unrecognised exception is `TransportError`. | leave | A reclassification flips every fielded block from its wire arm to its quiet one; the entry names a narrower question to answer first. |
| `MODB` | The proxy seam takes two types for one protocol field. | leave | Four signatures on a published interface plus the TestKit fake — the surface review two rows below. |
| `MODB` | A value width below two bytes divides by zero. | leave | Hardening a published type the consumer injects in production is a surface decision, not an area-local guard. |
| `MODB` | One surface, two instant types. | leave | Changing either is source-breaking on a published property type the consumer re-declares verbatim. |
| `MODB` | Forty-three public types are outside the API manifest. | decision | `T-008` — the 41 unmarked Modbus types the assembly walk found where the entry counted 43. **Resolved by `T-008`** (PR below): 16 published, 25 plumbing, entry deleted. |
| `MODB` | `Vion.Dale.Sdk.Modbus.Rtu` ships no `AddDaleModbusRtuSdk` extension, so a development host hand-constructs its `IConfigureServices`. | leave | Entered after the operator's ruling: found by `T-008`'s review round asking why RTU's `IConfigureServices` is published where the two I/O packages' identical class is plumbing. Adding the extension is a new published member — a ratchet move, not a repair. |
| `MODB` | Two consumer-facing exceptions live in an implementation namespace. | leave | A namespace change on a public type is source-breaking. Both now have a manifest row, so the move would be visible in the diff — which was the condition this row was waiting on; the move itself is still a consumer-breaking rename. |
| `MODB` | A factory-created Modbus client or server is never reclaimed. | leave | A DI-lifetime change on a published registration with twelve fielded creation sites; `AC-MODB-018.3` states the lifetime as it is. |
| `MODB` | The reuse-address knob has no same-version-redeploy repro. | leave | OS- and timing-dependent, which is why the regression was never written and no portable test can prove one red. |
| `MODB` | Whether a newer FluentModbus makes the reuse-address provider unnecessary is unasked. | leave | A dependency question, answered by a version bump nobody has asked for. |
| `MODB` | Three server features were deferred at design time and no consumer has asked since. | leave | Feature bands, each layering onto today's surface; no consumer has asked. |
| `CLI` | The two upload conflicts are told apart by the endpoint's message text. | Jira | `D7`'s VION-62 candidate; the distinguishable field is the platform API's, and six fielded invocations ride the substring. **`T-009`: filed as VION-195; the ledger entry is struck. All six invocations re-resolved.** |
| `CLI` | One thirty-second ceiling covers every cloud request, the package upload included. | leave | Small, but with no observable a red-first test can stand on (`AC-CLI-017.6` is `GAP` for the same reason) — the fix-now lane's entry price. |
| `CLI` | `dale dev` announces an address it never checked. | leave | Answering it needs `CTRL`'s readiness handshake, and changing the default is a surface change across five commands. |
| `CLI` | The bundled template has no gate that runs `dale new`. | leave | The fixture, the cleanup and the failure modes are a change doc's worth of work. |
| `CLI` | The `login` help's `--environment` default is whatever the developer's own store says. | fix-now | The entry says it: small, area-local, and one test pinning the help line under an empty store root. |
| `TKIT` | A persistent value declared for a property the block does not persist is accepted silently. | leave | Catching it needs the binder's own view of what persists — `BIND`'s surface, parked beside the inclusion-gate row. |
| `TKIT` | ~~Four~~ Three shipped SDK packages carry no analyzer reference. | decision | `T-008`'s sixth: `DALE014` cannot ask the three Modbus packages for a mark until they reference the generator, so the ratchet's Modbus half **is** this row. **Resolved by `T-008`** (PR below): three analyzer references, three wiring probes, and the `PublicApiConfig.cs` Core never had; entry deleted. |
| `TKIT` | Two downstream test projects cannot be proven against a same-PR kit change. | leave | It reaches the template's shipped content and the first-party library lane, which is `releasing.md`'s to decide. |
| `TKIT` | The five kit test projects do not agree on their test-platform reference. | leave | A consistency question the entry states for the operator; nothing is red, so it waits on a ruling rather than a fix. |
| `TKIT` | The SDK ships no test context for a service-provider handler. | leave | 350–450 lines of new published surface across three or four types — its own change doc. |
| `IO` | A block cannot ask whether a face it holds is mapped. | Jira | `D7`: VION-130 exists — link only, no new item; the consumer's reflection probe names the same gap. **`T-009`: VION-130 carries the pointer as a comment; the ledger entry is struck against that key.** |
| `IO` | A state payload of the wrong schema decodes as a value nothing sent. | leave | The accessor is a public-surface addition on a `BIND`-owned `[PublicApi]` struct, and the refusal needs a rule for a payload carrying no `schema` at all. |
| `IO` | A command that the far side refused is invisible to the block. | leave | Subscribing the response topic is new wire behaviour: a message type, an arm, and a decision about what a block observes. |
| `IO` | The core SDK has the same unmarked public type the IO pass fixed in its own packages. | decision | `T-008` — the SDK's undeclared root namespace, which `DALE014` matches as a prefix, so this row is not separable from the `BIND` row above and stays with it. |
| `IO` | `Vion.Contracts`' generated payload verifiers are unusable as published. | Jira | `D7`'s `vion-contracts` candidate (VION-16, `dale-sdk` label); the defect is in another repo's generated code. **`T-009`: filed as VION-197 (VION-16, `dale-sdk`), area `contracts`; the ledger entry is struck. Reproduced, and the count is eleven wrappers rather than ten. See *Drift checkpoints*.** |
| `IO` | `hal-sim` writes the two payload identity strings transposed. | Jira | `D7`'s `hal-sim` candidate (VION-16, `dale-sdk` label); nothing here reads the transposed fields. **`T-009`: filed as VION-198 (VION-16, `dale-sdk`), area `hal-sim`; the ledger entry is struck. Reproduced.** |
| `HTTP` | A per-request timeout does not bound the response body. | leave | Threading the token changes the exception class a callback receives — behaviour reshaped, not corrected. |
| `HTTP` | A callback lost before the block's first message stays lost. | leave | Neither cure belongs to this package; `IActorDispatcher`'s two members cannot answer whether the block has an actor. |
| `HTTP` | Two timeout bounds deliver two exception classes. | fix-now | `D6` already ruled *fix*, and it is **`T-010`'s, not `T-007`'s batch**: the predicate widens by one line, the message needs the executor to read `HttpClient.Timeout`. **`T-010`: fixed and the ledger entry deleted; `AC-HTTP-008.2` rewritten. Both halves of this reason are wrong — the one-line widening fixes neither case, and the executor does read `HttpClient.Timeout`. See *Drift checkpoints*.** |
| `HTTP` | The package ships no HTTP test kit. | leave | A sixth kit is its own change doc; raised by the first consumer, not found here. |
| `HTTP` | The package surfaces no link or connection diagnostics. | leave | A feature band that would need the package to own the primary handler. |

**Five `fix-now`, six `decision`, six `Jira`, fifty-eight `leave`** — fifty-nine once `T-007` added its own. Three of the five `fix-now` rows
— `ANLZ`'s packed-analyzer assertion, `CLI`'s `login` help default, `CTRL`'s duration converter — pass
every test above. **Two do not**, and both are here for a stated reason rather than a clean fit; they
are the rows to rule on first:

- **`ANLZ`'s `MinInterval` row fails "does it cross an area".** It is tagged `EMIT` + `ANLZ`, and the
  fix moves `EmissionAttributeHelper.cs:267` and `DurationParser.cs:120-121` together — the analyzer
  mirrors the runtime exactly, so a one-sided fix would reject a token the runtime accepts. Two
  areas is lane 2 as `spec-process.md` writes it. Bucketed `fix-now` because it is **one rule stated
  twice, not two decisions**, and because the defect is live: `MinInterval = "922337203685477.6"`
  draws no diagnostic and makes the emission gate's elapsed test unconditionally true. If the area
  question is read strictly, this row is `leave`.
- **`HTTP`'s row 28n fails all three**, and is bucketed `fix-now` only because **`D6` already ruled
  it a fix** — the bucket records a decision taken, not a judgment made here. It is also **`T-010`'s,
  not `T-007`'s batch**, and the entry's own reason for parking it ("it changes an exception class a
  callback receives today") is *behaviour reshaped rather than corrected*, which is lane 2's wording.
  If the bucket is meant to read strictly as "`T-007` fixes this", this row is the one to move.

Two `leave` rows are the nearest misses in the other direction, and both fail exactly one test:

- **`IO`'s wrong-schema decode** — the fix is named down to the line count, but it adds a member to a
  `[PublicApi]` struct (**adds published surface**) and needs a rule for a payload carrying no
  `schema` at all (**a design point is open**).
- **`CLI`'s upload ceiling** — small and area-local, refused only because **no test can stand on it**
  (`AC-CLI-017.6` is `GAP` for the same reason), which is why the pass left it here.

Four counts this recount moved are in *Drift checkpoints*: `T-008`'s five entries are six and its
Modbus half is larger than the ledger says, `D7`'s five candidates are six rows, all six of `D7`'s
`#nn` numbers misroute by a uniform **+3**, and reviewer's question 2's "four undeclared SDK
namespaces" is seventeen. `T-009` goes by `D7`'s descriptions and by each entry's *Escalated to the
operator as a Jira candidate* line — the ledger carries exactly five, and nothing else does.

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
| The launcher recipe, the model rubric, the per-process permission mode, `-AmendFile` round-trips | lane 3 § 2, which points at `architecture/plugins/vion-dispatch/README.md` |
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
- **`T-003`: the narrowed hole check fails on two holes whose withdrawal was recorded only in prose,
  and closing them meant writing into two archived docs.** `AC-GATE-007.1` (withdrawn in the GATE
  pass's Phase B, merged into `AC-GATE-008.3`) and `AC-INTRO-002.2` (merged into `AC-INTRO-002.1`
  before publication) are both recorded by their own passes, dated, with the reasoning — in prose,
  under *Drift checkpoints* and *Amendment 2*, never as a `REMOVED` delta line. Under the old
  any-mention rule that prose was indistinguishable from the `ADDED` line that minted the id; under
  the new rule neither counts. Each archived doc gained one `REMOVED` line in a subsection that says
  who added it and when, which is a touch after the archive commit (`spec-process.md` § Change docs
  says that commit is the last one). Carrying the two lines in this in-flight doc would satisfy the
  gate — the check reads in-flight docs too, below — but would date a 2026-09-02 withdrawal to
  2026-09-07 and file it under the doc that changed no criterion. The record belongs to the pass that
  withdrew the criterion.
- **`T-003` widened the hole check past the task line, from archived change docs to every change
  doc.** The task line says the check accepts a `REMOVED <id>` line "under `docs/changes/archive/`".
  Read literally that reddens every future leaf retirement for the life of its own change: the doc
  doing the withdrawing is in-flight from the page edit that opens the hole until the archive commit
  that closes it, which for a multi-PR doc is every PR it has. Demonstrated on a minimal tree before
  the widening (a page with `.1` and `.3`, an in-flight doc carrying the `REMOVED .2` line: FAIL) and
  covered by self-test case 12e. The narrowing the task asks for is *prose and `ADDED` lines no longer
  count*, and that is intact.
- **`T-003`: the un-hidden tests carried three settled-style violations, and the renames touched two
  more archived docs.** `ScenarioSteppingShould.FailSettleNamingTheStillChangingTarget`,
  `ExpectStepShould.NameTheTargetTheBoundAndTheActualValueOnFailure` and
  `InclusionGateShould.BindExactlyTheIncludedMembers` have each carried an article since their area's
  pass with no gate able to see them. They are now `FailSettleNamingStillChangingTarget`,
  `NameTargetBoundAndActualValueOnFailure` and `BindExactlyIncludedMembers`, and the SCEN and GATE
  pass docs' tables that cite them by name — three rows — were swept with them; a rename invalidates
  every test name a doc carries, wherever the doc lives.
- **`T-003` swept the defect shape, not the symbol the task line names, and the regex became a
  scanner.** The task line names one shape: a `]` inside a string
  (`[DataRow("Mode in ['Eco', 'Fast']", …)]`). The defect is *the attribute block never reaches its
  signature*, and it has two more instances the first fix left standing — a `]` closing a nested
  bracket (`[DataRow(1, new[] { … })]`) and an attribute wrapped onto an indented line — found by the
  review subagent, which enumerated the citations independently of the gate. Twenty-one cited methods
  in all, three of them non-conforming. One regex covering all three needs an alternation that
  backtracks exponentially on an attribute that never closes, which fails CI as a hang rather than a
  failure, so the block is now read by a linear scan that states the C# rules it knows: a string
  literal in every form, a char literal, and bracket depth. Verified against the shape it replaces —
  the scan finds every one of the 2525 methods `origin/main` found and adds 21; no method's body
  *start* moves, and eighteen body *ends* shrink, each by the hidden method the old regex had
  swallowed into its predecessor's span. That span is what the Triple-A marker check reads, so a
  predecessor missing its own marker used to pass on its hidden neighbour's.
- **`T-003` deleted the ledger entry for the defect it fixed, and corrected the gate's row in
  `spec-process.md`.** `_findings.md`'s header obliges the fixing PR to delete the entry; the entry
  (`ANLZ` pass, "A `[DataRow]` containing a `]` hides a test") had also gone stale twice over — a
  `file:line` anchor that had moved, and "one test in `Vion.Dale.Sdk.Generators.Test`" against the 21
  the sweep found. `spec-process.md` § Gates owns what each gate fails on and still stated the
  any-mention rule this change deleted; the two are now one text. 76 entries.
- **`T-003`'s second review round closed three holes the first fix opened or left.** The scan read
  `@"…"` and `$@"…"` as verbatim but not `@$"…"`, C#'s third spelling, and fell back to escape
  handling that walks past the closing quote of a path ending in a backslash — the one shape where
  the scan was *worse* than the regex, and in the under-reporting direction (no site in the tree
  today; self-test cases 10 and 10b, one spelling per file because two hidden methods in one file let
  either carry the case). Widening the hole check to every change doc had let a `proposed` doc — which
  `spec-process.md` § Change docs says `spec-trace` ignores, and which by definition has not opened
  the hole it claims to close — and a `REMOVED` line quoted inside a fence explain a hole (cases 12f,
  12g). And `test-style-lint` had no anti-vacuous floor: it reported OK over the 21 methods it could
  not see, and would have reported OK over zero, including where no `*.Test` directory exists at all,
  because `Get-ChildItem -Path @()` enumerates the process working directory rather than nothing
  (cases 11, 11b). `spec-trace`'s equivalent empty-roots path is left alone: it already fails, on the
  orphans that follow from it, and only its message misleads.

- **`T-004`: twenty-three bare sites were four.** The task line and the ledger entry it closes both
  say twenty-three of thirty suppressions carry no reason. Measured on the tree this landed on: 32
  `#pragma warning disable` lines naming a `DALE` id, across 14 files, of which 5 have neither a
  comment after the directive nor a `//` line above it — and 4 under the rule as landed, the fifth
  being `GatingBlocks.cs:364`, whose reason is the doc comment of the class below and names
  `DALE044`. The same grep at `2902b93`, the ANLZ pass that wrote the entry, returns the same 32
  lines, so the count was never true of a different tree: it was measured under a rule the entry does
  not state. The gate is the same gate either way; what shrank is the edit beside it, from five other
  areas' fixtures to one file.
- **`T-004` reads the whole comment run above the directive, not the one line the task names.** 21
  of the 32 reasons are wrapped over two or more lines, and three of them end on `// is suppressed.`
  — the tail of a reason, made of nothing but the directive's own words
  (`DeclarativeTimerBinderShould.cs:229`, `:249`, `:269`). Reading one line reddens those three and
  admits any tail that happens to carry a noun; the run is the unit the author wrote. The narrowing
  the task asks for survives intact: the run has to *reach* the directive, so a blank line or any
  code between ends it (self-test case 5), which is the defect the four bare sites had. A `/* */`
  block above counts too — `comment-conventions.md` § *Form* names that form first for anything
  longer than a line, and a gate failing from its first landing may not fail a blessed form.
- **`T-004`'s gate also fails a comment built from nothing but the directive's own words**, which
  the task line does not ask for but the operator's constraint does. The check strikes every
  `DALE####` token, XML doc tags, words of three letters or fewer and a closed list of the
  directive's vocabulary, then asks that **one** word remain. One, not a prose-quality floor: the
  first version asked for three, which rejected `// Bug in the tool; see VION-133.` — the one
  citation form `comment-conventions.md` blesses — while still admitting any non-reason padded with
  adverbs, and reported it under a message about restating the diagnostic that was not true of it.
  What a lint can judge is whether the comment says anything the directive does not, and that is now
  the whole of the rule and the whole of the message.
- **`T-004`: a `///` run above counts only where it names an id being disabled.**
  `GatingBlocks.cs:364` carries its reason in the doc comment of the class below the directive, which
  names `DALE044` and says why the combination is declared anyway. A doc comment documents its
  member, so accepting one unconditionally would let every documented member pass on prose that never
  mentions the suppression (self-test case 6b); naming the id is the author connecting the two on
  purpose. The alternative — a `//` line duplicating the doc comment two lines above it — is the
  noise `comment-conventions.md` § *Terse* refuses.
- **`T-004` armed the DALE analyzer in `Vion.Dale.Sdk.TestKit.Test`, because its five suppressions
  suppressed nothing.** The project referenced `Vion.Dale.Sdk` but never the generator assembly as an
  analyzer, so by `AC-ANLZ-018.2` no DALE diagnostic was ever judged there and all five directives
  were decorative — which makes any reason written beside one a claim about a mechanism the project
  does not have. Measured before deciding: with the analyzer armed and every directive in place, zero
  DALE diagnostics; with the analyzer armed and the five directives removed, exactly five, one per
  directive, each an **error** — `DALE034` at `CustomThresholdEmissionPolicyShould.cs:68`, `DALE035`
  three times and `DALE036` once in `EmissionPolicyShould.cs`. So the one-line `ProjectReference`
  costs no new suppression and turns five inert claims load-bearing. Writing the reasons without it
  was the alternative, and it means writing four comments whose subject is a directive that does
  nothing.
- **`T-004`'s reasons went onto the directive line, and the two group comments stayed.** The first
  attempt deleted both group comments and gave each directive a three-line reason, which stated the
  shared half three times in identical words — `comment-conventions.md` § *Terse* cuts whole points
  that don't need making, and the grouping ("each of the three below") is the one thing a per-site
  comment cannot say. What each directive owed was the half its siblings do not share: the literal it
  exempts. Four trailing comments, and the file is otherwise untouched.
- **`T-004`'s first self-test read exit codes only, and three of ten mutations survived it.** The
  gate reports two defects — nothing written, and something written that says nothing — and both exit
  1, so an exit-code case cannot tell them apart: collapsing the two branches survived, and so did
  dropping the four-letter rule and the substance floor itself, because the one case aiming at the
  floor was in fact landing on the empty-reason branch. Cases now assert the message, and case 8
  asserts the site *count*, because every non-site in it would pass as a site — the exit code cannot
  see an over-count. Sixteen single-rule mutants, each killed by a named case; the run and its table
  are in the PR body, which is where `testing-conventions.md` § 16 puts a mutation record.
- **`T-004` swept the three suppression channels beside the one it gates.** `AC-ANLZ-020.1` names
  four — `#pragma warning disable`, `[SuppressMessage]`, `NoWarn` and an `.editorconfig` severity
  entry — and this gate reads only the first. The other three are empty of DALE today: every `NoWarn`
  in the tree names `1591`, `RS2008` or `CS8669`; no `.editorconfig` carries a `dotnet_diagnostic.DALE`
  entry; the three `[SuppressMessage]` attributes name `MSTEST0049`. So nothing is being routed
  around the gate now, and a future `[SuppressMessage("Dale", …)]` would be — recorded here rather
  than widened into, because a gate reading attribute arguments is a different scanner.
- **`T-004` deleted the ledger entry for the defect it fixes, 76 → 75.** `_findings.md`'s header
  obliges the fixing PR to delete the entry; the entry (`ANLZ` pass row 167, "Nothing requires a
  `#pragma warning disable DALE*` to say why") is the one whose count this doc's first checkpoint
  corrects. Nothing links it. Reviewer's question 8's parenthetical, which carried the ledger's size
  forward from `T-003`, is updated with it.
- **`T-005`: the BOM count was wrong when this doc was written, not stale — 195 of 946, not 199 of
  941.** Reviewer's question 3 and `T-005`'s task line both carry the same pair. Counted at
  `66dc32c`, the exact main the *Where the migration stands* section cites, the tree already held
  946 tracked `.cs` of which 195 carried a mark; the count is unchanged at `814b4e9`, so neither
  number drifted between the drafting and the work. The nearest true figure in the record is the
  `ANLZ` pass's **200 of 884** (2026-09-04); between that count and this one `.cs` grew by 62 and
  marked files fell by 5. 195 is the number this PR normalises and the number the gate now reports.
- **`T-005`: there was no ledger entry to close.** The task line ends "the ledger entry closed", and
  `_findings.md` has no entry about byte-order marks — 75 before this PR and 75 after. The question
  was never routed to the ledger: the `ANLZ` pass left it under *Seen and left* as "a retro
  question" (`docs/changes/archive/2026-09-04-anlz-pass.md:224-227`), the `MODB` pass carried it
  forward unchanged in its own *Seen and left*, and the journal recorded it on 2026-09-04. Those are
  where the answer lands, and nothing was deleted. The entry `T-006` will bucket does not exist.
- **`T-005` strengthened the probe past its task line, because the line as written cannot fail.**
  "Probe three files through `cleanup-code.ps1` to confirm cleanupcode leaves a BOM-less file alone"
  is satisfied by stripping three marks and seeing them stay stripped — but the tree is style-clean,
  so cleanupcode had no edit to make in those files and may never have written them at all. A probe
  that passes whether or not the tool ran is the same shape as the vacuous gate `T-003` and `T-004`
  each closed. So a fourth step was added: a real formatting violation was introduced into one of the
  three, cleanupcode repaired it — proving it rewrote the file — and wrote it back with no mark, byte
  identical to the stripped original. That run is the evidence in the PR body; the three-file form
  alone is not. The three were also chosen for shape, not convenience: one per target framework
  (`net10.0`, `netstandard2.1`, `netstandard2.0`) and each with non-ASCII content, since a BOM-less
  file with non-ASCII bytes is the only shape where a tool's encoding *detection* can go wrong. 68
  of the 195 are of that shape.
- **`T-005`'s widened gate carries two anti-vacuous floors, where the carry-over asked for one.** A
  floor on the total (`$checked -eq 0`) is `pragma-reason-lint`'s shape and catches a scan that
  reaches nothing, but it cannot defend *this* change: dropping `.cs` from the kind list leaves 167
  files of the other kinds still checked, so the total stays healthy and the gate reports OK while
  the 946 files it was widened for go unread. But a floor only catches a count reaching *zero*, and
  a scan narrows partially: a pathspec still returning C# but no longer `.json` holds both floors up
  while four kinds go uncovered. So the report prints the file, kind and `.cs` tallies
  (`1113 file(s) across 11 of the 12 BOM-free kinds (946 .cs)`), and the cases pin all three. The
  mutation table is in the PR body, where `testing-conventions.md` § 16 puts one.
- **`T-005` gated `.cs` and stopped there; four kinds stay mixed, not two.** Measured over every
  tracked file, what still carries a mark is `.csproj` 10 of 75, `.sln` 10 of 10, `.DotSettings` 3
  and `.scriban` 1. Question 3 decided "gate `.cs`", and an IDE rewrites the first three on its own
  terms — gating them would fail an author for Visual Studio or ReSharper, not for an edit. The
  fourth is not an IDE artefact and the reason differs: `LogicClassTemplate.scriban` is hand-written
  text the generator renders into every generated logic class, and its mark is inert only because
  `LogicClassGenerator.cs:667` reads it through a `StreamReader`, which strips one by default. That
  is a property of the reader, not a policy. Named here rather than widened into, so the choice is
  the operator's and not this task's.
- **`T-005` measured `.ps1` and left it out.** The gate's own criterion is "a kind this repo never
  writes with a mark", and `.ps1` qualifies today at 0 of 26 — it is also the language every gate in
  the suite is written in, so the `utf-8-sig` helper the docstring names would hit it as readily as a
  `.cs`. Adding it would normalise nothing and lock in the current state. It is outside question 3's
  "gate `.cs`", so it is recorded as a free candidate rather than taken.
- **`T-005` corrected two lists beside its own edit in `spec-process.md`.** The gate table's kind
  list omitted `.yaml`, which `bom-lint` has always checked, and the lane-3 *Gates, all of them*
  paste list omitted `scripts/pragma-reason-lint.ps1` — `T-004` added its row to the gate table but
  not to that list, so a REPORT following it verbatim would paste eight of the nine gates
  `spec-gates.yml` runs. Both are one-line corrections to text this PR was already editing.
- **`T-005`'s review round found five mutants the first suite did not kill, three of them in the
  lines this task added.** Twelve cases and eleven killed mutants were not enough, and the pattern
  was the same each time: a number that only ever has one value in the fixture is indistinguishable
  from a constant. Both cases reading the `.cs` tally expected `2`, so printing the literal `2`
  survived; no case read the FAIL header at all, so its tallies were unwatched; no case produced
  more than one problem, so printing `1` survived; case 4 pinned only the short side of the
  three-byte length test, so demanding a fourth byte survived — and a file that *is* a mark and
  nothing else is what `utf-8-sig` writes for an empty file. The fifth was structural: every case
  ran against a temp fixture, which is not a git repo, so all twelve exercised the directory walk
  and none exercised `git ls-files` — the only branch `spec-gates.yml` ever takes. Cases 2b, 4b, 10b
  and 13a–c close them; the suite is 19 cases and 17 killed mutants, no survivors. **The lesson
  `T-004` drew — assert the message, not the exit code — is necessary and not sufficient: a tally is
  pinned only by two cases that disagree about it, and a branch is covered only by a fixture shaped
  like the one production runs on.**
- **`T-005`'s new count assertion failed in CI and had found a real defect: the walk is OS-dependent
  without `-Force`.** Case 1 passed at the desk and reported `6 file(s) across 5 of the 12` on the
  Linux runner. The missing one is `.github/workflows/ci.yml`: on Unix a dot-prefixed entry is
  hidden, and `Get-ChildItem -Recurse` omits hidden entries unless told otherwise, so the walk
  scanned `.github` on Windows and skipped it on Linux — a kind this gate covers, silently unscanned
  on the platform CI runs. Twelve exit-code cases had not seen it, and neither had the message-only
  assertions that preceded this round; the tally is what made it visible. `-Force` is added, and the
  case is made to fail on Windows too by setting the Hidden attribute on the fixture's `.github`,
  because a guard that holds only on the runner passes at every desk. The gate's production path is
  `git ls-files` and was never affected — the defect was in the fallback the self-tests themselves
  run on, which is the part of a gate nothing else exercises.
- **`T-006`: `T-008`'s five ledger entries are six, and its Modbus half is larger than the sixth
  says.** Reviewer's question 2 counts five and `T-008`'s task line closes five; the ratchet family
  in `_findings.md` is six. The sixth is `TKIT`'s "~~Four~~ Three shipped SDK packages carry no
  analyzer reference" — none of `Vion.Dale.Sdk.Modbus.Core`, `.Rtu`, `.Tcp` references
  `Vion.Dale.Sdk.Generators` (only their two TestKits do), so `DALE014` never asks them for a mark,
  and "the 43 Modbus types all join the manifest" cannot be done without arming the analyzer there.
  One row, not two pieces of work — which is why it is `decision` and not `fix-now`.
  **But that entry's premise is wrong where it matters most**: it says all three "each declare
  `[assembly: PublicApiNamespace]`", and `Vion.Dale.Sdk.Modbus.Core` declares **none** — its only
  assembly attribute is `[DaleSharedAssembly]` (`AssemblyAttributes.cs:9`), and it has no
  `PublicApiConfig.cs`. `DALE014` fires only inside a declared namespace
  (`PublicApiDocumentationAnalyzer.cs:103`), so arming the analyzer in Core asks about **nothing**.
  Counted: Core 40 public types / 20 marked / 7 namespaces / **0 declared**; Rtu 12 / 1 / 1 / 1;
  Tcp 23 / 11 / 7 / 5 — 43 unmarked, as the ledger says, but only ~23 of them in a namespace the
  ratchet can see. So `T-008` also has to decide **which of Core's seven namespaces are published**,
  which is a scope question the ruling "include them all" does not answer for a package that
  declares nothing. (The warnings are warnings, not errors: nothing in the tree sets
  `TreatWarningsAsErrors`.)
- **`T-006`: the `Jira` bucket is six rows, where `D7` names five candidates.** The sixth is `IO`'s
  "A block cannot ask whether a face it holds is mapped" — `D7`'s `#70`, which it routes to VION-130
  as a **link** rather than a new item, and which `T-009`'s task line acts on in the same breath as
  the five ("link `#70` to VION-130"). Five filings plus one link; no entry is filed twice.
- **`T-006`: `D7`'s `#nn` numbers are not positions in `_findings.md`, and all six are the true
  position plus exactly three.** Counted at `66dc32c`, the exact main this doc cites (77 entries):
  the runtime's stale handler map is #33 not `#36`, the `DALE043` false error #43 not `#46`, the
  `dale upload` 409s #57 not `#60`, VION-130's face-mapped question #67 not `#70`, `Vion.Contracts`'
  verifiers #71 not `#74`, `hal-sim`'s transposed strings #72 not `#75`. Read as positions, every
  one lands on the wrong entry — `#46` on the `[DataRow]` row `T-003` has since deleted, `#70` on the
  core SDK's unmarked type, which is `T-008`'s. The offset is uniform, so the numbering is
  mechanically recoverable, but it is the coordinator's machine-local review's and `T-009`'s own task
  line already expects that file to be gone. What identifies an entry is `D7`'s parenthetical
  description, and each of the five matches exactly one entry carrying an *"Escalated to the operator
  as a Jira candidate"* line — the ledger holds exactly five such lines, and the routing on each
  agrees with `D7`'s. `T-009` goes by those, never by the number.
- **`T-006`: reviewer's question 2's "four undeclared SDK namespaces" is seventeen.**
  `PublicApiConfig.cs:6-8` declares 3 of `Vion.Dale.Sdk`'s 20 namespaces (`Core`, `Emission`,
  `Utils`); the other 17 each hold at least one public type. The largest are `Core`'s neighbours in
  everything but the ratchet: `Messages` (28), `Mqtt` (24), `Abstractions` (16),
  `Configuration.Interfaces` (15), `Configuration.Services` (12). Those two figures check the method
  — `LIFE`'s ledger row independently says 28 message types and 7 diagnostics types, and a metadata
  walk of the built assembly gives 28 and 7. Three of the seventeen are `Examples.*`, whose contents
  are the packaging question `BIND`'s example-files row raises rather than the ratchet's. No total is
  quoted here on purpose: a source-declaration count and an assembly walk disagree by tens
  (nested types, and what a `record` declaration emits), and `T-008` should take its number from the
  manifest generator rather than from either. The ruling does not move — "include them all" — but
  `T-008`'s size does, and the three ledger entries that name namespaces (`LIFE`, `BIND`, `IO`)
  between them name eight of the seventeen.
- **`T-006`: `T-010`'s row 54 has no ledger line either, and its page edit is already made.** The
  task line reads "54: the decline stated on the page and the ledger line closed". `_findings.md`
  has never carried a `DaleSharedAssembly` entry — the HTTP pass's own record says only 28n left one
  ([`archive/2026-09-06-http-pass.md:757`](archive/2026-09-06-http-pass.md)) — and the decline is
  already on the page as `AC-HTTP-013.3` (`docs/specs/http.md:347`, its rationale at `:363`), landed
  by that pass. So `T-010` is row 28n plus a re-read of a criterion that already says what the task
  asks to be said, not two halves. This is the same shape as `T-005`'s missing entry: the second
  task line in three to name a ledger entry that does not exist, both times for a question a pass
  had already answered somewhere else.
- **`T-006`: the one `fix-now` row whose citation was checked turned out to be stale, and it was the
  only one checked.** `CTRL`'s duration-converter entry cites
  `Iso8601TimeSpanConverter.cs:107-123`; the file is 40 lines and has been since `56bc40b`, with
  `Read` at `:18-33`. The mechanism survives — `XmlConvert.ToTimeSpan` catch → `TimeSpan.Parse`,
  whose `FormatException` escapes — so the finding holds and the bucket stands; the entry is
  corrected in place, with the second registration site (`WebHostService.cs:104`, SignalR, where
  there is no 400) added, because a fix aimed at one of two registrations is a fix that half works.
  What this says about the round: a bucket is a claim about a fix's shape, and a shape read off a
  citation nobody resolved is a hypothesis. The other 74 entries' citations are **unverified here** —
  the ledger was read for what each entry *says*, not re-probed — so `T-007` and `T-008` re-resolve
  before they act, and `docs/spec-process.md` § Lanes' "a finding is a hypothesis until the tree
  confirms it" is the rule that says so.
- **`T-007`: the `CTRL` entry names one escaping exception class and the tree has two.** The entry
  says `Iso8601TimeSpanConverter.Read` lets `TimeSpan.Parse`'s `FormatException` escape. Measured on
  the tree this landed on, `"nope"` and `"PT"` escape as `FormatException` and
  `"10675200.00:00:00"` — a duration past `TimeSpan`'s range — escapes as `OverflowException`, which
  the entry does not name and which a `catch (FormatException)` would have left standing. The fix is
  `TimeSpan.TryParse` plus one `throw new JsonException(…)`, so both go the same way, and the third
  `[DataRow]` is there for the class the entry missed rather than for a second spelling of the one it
  names.
- **`T-007`: a non-string token was never the 500 the `CTRL` entry describes, and the fix is narrower
  for it.** `reader.GetString()` on a number token throws `InvalidOperationException`, which looks
  like the same escape — but `System.Text.Json` converts it itself: probed before deciding, `5`
  deserialized as a `TimeSpan` yields `JsonException: The JSON value could not be converted to
  System.TimeSpan`, path appended, with no converter change at all. So the malformed-body family has
  exactly one hole and it is the parse fallback; widening the fix to the token check would have been
  a guard against a defect the framework does not have.
- **`T-007`: `AC-CTRL-014.6` is minted for the converter, not for a route, because the entry's
  "unreachable today" is still true.** Re-resolved: the two `[FromBody]` actions in the whole web
  surface take `SetValueInput<JsonElement>` and `SetValueInput<object>`
  (`DevHostController.cs:52`, `:68`), so no request body binds a typed duration and no end-to-end
  `400` can be asserted. The criterion therefore states the *class* the decode raises over both
  wires, and the `400` is in the prose beneath it, where § Refusal shapes already owns it. Both
  registration sites (`WebHostService.cs:87` MVC, `:104` SignalR) share the one converter instance
  type, so the fix reaches the site the `T-006` note called the observable and the site it called
  answerless, in one edit.
- **`T-007`: the `CLI` entry proposes two fixes and only one of them is needed.** The entry asks for
  the help to describe the resolution rule *and* for "the snapshot regeneration to run against an
  explicitly empty store root". Once the option carries no `DefaultValueFactory` the help does not
  read the store at all, so the second half guards nothing: `publish.yml`'s snapshot step is
  untouched, and the committed snapshot line is regenerated from the built tool and committed here
  rather than left to the bot. The entry's aside about `Environment.SpecialFolder.UserProfile`
  ignoring `USERPROFILE` on Windows is confirmed and now moot for this option — `TokenStore.UseRoot`
  is the seam the test uses, and the test's three rows differ only in what the store holds.
- **`T-007`: one mutant of the `CLI` fix survives, and it is the browser-bound boundary again.**
  Replacing `CommandContext.ResolveLocal(flag).Environment` in the action with `flag ?? "production"`
  — dropping the stored-second step — is killed by no test in the 395 the suite runs. The observable
  is past `AuthService.AcquireInteractiveAsync`, which opens a browser and binds a loopback listener,
  and this area's suite reaches neither ([`cli.md`](../specs/cli.md) § Test discipline); it is the
  same boundary `AC-CLI-018.4` and `.7` are `GAP` for. What the fix does about it is structural
  rather than test-shaped: the rule now has one implementation, `ResolveLocal`, whose three branches
  are proven at `CommandContextShould` against `AC-CLI-013.3`, and login's duplicate of it is gone.
- **`T-007`: the packed-artifact gate's new real-tree case held only on the CI runner, which is the
  carry-over's own failure shape.** The case checks that each required-content rule's package id
  names a project in the repository — the one mutation no fixture catches, because a mis-typed id
  matches no package and every fixture still reports OK. Written with `Test-Path`, it caught
  `Vion.Dale.SDK` on Linux and passed at the desk, because a Windows file system answers `Test-Path`
  for either spelling. The case now compares directory and file names with `-ceq`, and the mutation
  is killed on Windows. Two `-ceq` comparisons stand behind it, so the single-rule mutant that
  relaxes one is still caught by the other, so the mutation table in the PR body carries the double
  mutant instead.
- **`T-007`: the same gate's entry match was case-insensitive, and the convention path is not.**
  PowerShell's `-contains` ignores case, so a package carrying `Analyzers/Dotnet/Cs/…` — which NuGet
  does not load as an analyzer — satisfied the required-content rule. The comparison is
  `-cnotcontains` and a fixture spelling the path in the wrong case is case 8. Found by the mutation
  table, not by the fixtures: the first `-inotcontains` mutant survived because it was equivalent to
  what the code already did.
- **`T-007`: the packed-artifact gate's self-test now runs the script the way CI runs it, and still
  runs only where CI runs it.** Its four cases called `Invoke-Verify` in process, so neither the
  parameter binding, the exit code nor the printed report was under test — the shape `T-005` closed
  for `bom-lint` by discovering that twelve cases had never touched the production branch. All cases
  are now a `pwsh -File … -PackagesDir` child process and every one asserts report text beside its
  exit code. Where they run is unchanged and is the honest limit: `-SelfTest` is called from
  `publish.yml`'s `verify-packages` job, not from `run-script-tests.ps1`
  (`scripts/run-script-tests.ps1`'s `$exempt` says so with that reason), and `publish.yml` ignores
  `docs/**` and `examples/**` — so a docs-only PR runs none of it. This PR touches `scripts/` and
  `.cs`, so it runs here.
- **`T-007`: the gate's floor and its tallies are proven by two script variants, because a fixture
  cannot vary module state.** The rule table is a module-level hashtable, so no directory of
  packages can empty it or add to it. Two cases write a copy of the script with the table rewritten:
  emptied — which must fail on the floor rather than report clean — and given a second rule, which
  must print `2 rule(s)`. Without the second, the rule tally has one value across the whole suite and
  a mutant printing the literal `1` survives; it did, until that case was added.
- **`T-007`: `AC-ANLZ-018.3` said what the tree did, and now says what it does.** The criterion
  stated the defect — "a package carrying no analyzers and no warning" — with the fix routed to the
  ledger in its own `GAP` reason. It now states the pack condition's outcome *and* the release run
  failing on it, and the `GAP` stands for the reason `AC-ANLZ-018.1`'s does: the observable is a
  packed artifact, and this gate's fixtures are packages rather than compilations, so no test
  artifact `spec-trace` scans can carry either id. The ledger entry's "forty-six diagnostics" is the
  count in the tree today (46 distinct `DALE###` ids in `Vion.Dale.Sdk.Generators`); the `ANLZ` pass
  archive's 44 was true at its own date and is not corrected here.
- **`T-007`'s review round found the fix incomplete in the one dimension the entry named.**
  `XmlConvert.ToTimeSpan` raises `OverflowException`, not `FormatException`, for a *well-formed* ISO
  duration past `TimeSpan`'s range — so `"P100000000D"` escaped the first fix untouched, while
  `"10675200.00:00:00"`, the same span in the .NET spelling, was refused. One input class, two answers,
  and the checkpoint above claiming "both go the same way" was measured on the .NET form alone. The
  converter now catches both classes off the ISO attempt, and the fourth `[DataRow]` is the ISO
  spelling. Measured red at the first fix: `Actual exception type:<System.OverflowException>`.
- **`T-007`: the criterion as first minted was falsified by its own commit's test.** `AC-CTRL-014.6`
  read "WHEN a duration … is neither an ISO-8601 duration nor the .NET form THE SYSTEM SHALL refuse
  the payload as malformed" — while the converter returns `TimeSpan.Zero` for `""` and for JSON
  `null`, and the same commit's test asserted exactly that. The read half is two rules, not one, and
  they hold under different failures: it is now `AC-CTRL-014.6` (both forms, and an absent duration as
  zero) and `AC-CTRL-014.7` (the refusal). The tolerance test had also been citing `AC-CTRL-014.5`,
  which is the **emit** half — a citation for behaviour the criterion does not state, and the defect
  `spec-process.md` § IDs & EARS names. Both tests now cite the criterion whose text they assert.
- **`T-007` dropped `AC-CTRL-014.6`'s "on either wire" clause, because nothing reaches a wire.** The
  test builds its own `JsonSerializerOptions`; it proves the converter class and says nothing about
  `WebHostService.cs:87` or `:104`. A criterion the suite cannot reach is `GAP` however many tests
  name it, so the choice was to GAP the wire half or to state the decode. Both criteria now state the
  decode, and where it is registered is prose beneath them.
- **`T-007`: the `ANLZ` entry's premise was false, and it is the citation this round did not
  re-resolve.** The entry says a package packed without its analyzer "restores, compiles clean and
  judges nothing; the only signal is previously-red code turning green". It does not.
  `Vion.Dale.Sdk.csproj:97` packs `build/Vion.Dale.Sdk.targets` **unconditionally**, and that file
  adds the analyzer **unconditionally** (`:11`), while the analyzer is packed under
  `Condition="Exists(…)"` (`:92`) — so the package ships a targets file pointing at a file it does not
  carry, and every consumer's build fails with `CS0006`, 0 warnings. Probed on a minimal project, and
  the import path confirmed in a consumer's generated `nuget.g.targets`. The gate is still worth
  having — it names the package in the release run instead of in the first consumer's build — but the
  failure mode was wrong in five places (the script's docstring, its rule-table comment, **its
  report**, `analyzers.md`'s prose and `AC-ANLZ-018.3` itself) and is corrected in all of them. The
  contradicting file is one directory from the `csproj:92` the entry cites; re-resolving a citation
  means reading what the cited line does, not only that it is still there.
- **`T-007` rewrote `AC-ANLZ-018.3`, so the archived `ANLZ` pass's delta line moved with it.**
  `spec-process.md` § Change docs says a criterion's text on the page and on its delta line are one
  text, and that a fix landing after the archive commit carries both. The delta line and the
  consolidation map's row 152 in `docs/changes/archive/2026-09-04-anlz-pass.md` are updated, with a
  blockquote above the line saying who touched it and why — the shape `T-003` established for the same
  situation. Verified in isolation: `spec-change.ps1 archive` against a slug-renamed copy reports 6
  unapplied delta lines before and 5 after, the five being pre-existing bold-marker artifacts that
  `origin/main` carries too.
- **`T-007`'s gate had one single-line mutant that left the self-test green**, and it is the shape the
  fixtures could not see: `$absent += $result.Missing` → `$absent = $result.Missing`. Every one of the
  eight package fixtures held **one** package, so "the total" and "the last package's" were the same
  number for all three cross-package accumulators. On a release artifact set they are not —
  `Vion.Dale.Sdk` never sorts last there, its `.AnalogIo`, `.Modbus.*` and `.TestKit` siblings follow
  it — so the mutant reported the last package's empty finding list and exited 0 on exactly the
  release the gate exists to stop. Two two-package fixtures close it, and a third accumulator
  (`$matchedPackages++` → `= 1`) needed the two-rule variant pointed at a directory where **both**
  packages are under a rule. **The second review round then found the same shape on the accumulator
  that matters most** — see below; the count that stands is twenty mutants, no survivors.
- **`T-007` tried a second production floor and backed it out.** "Every rule must match a package"
  catches a rule id that names nothing — but it has no honest predicate: a directory holding one
  unrelated package is a legitimate run, and the floor reddened four fixtures that are exactly that.
  What it aimed at is a claim about *this repository*, so it is checked against this repository in the
  self-test instead: each rule's id must name a project directory and a `.csproj` (both compared
  `-ceq`), and where that project declares a `<PackageId>` it must equal the rule key — because a rule
  keyed on a project whose package id differs matches nothing at run time and reports `0 package(s)
  matched` in a green job.
- **`T-007`: a `[DataRow]` value collided with the production default, and only the mutation showed
  it.** `LoginCommandShould`'s rows were `null`, `"test"`, `"production"`, with an `if` on the
  parameter — which `testing-conventions.md` § 13 rejects, since an `if` on a row parameter means the
  rows are different scenarios. Split into a parameterised test and an empty-store test, the deleted-
  Arrange mutation reddened two rows of three: `"production"` survived, because `DaleConfig.Environment`
  *defaults* to `"production"` and `LoadConfig()` on a missing file returns a fresh one. A row whose
  value equals the default cannot tell a loaded fixture from no fixture. The rows are now `"test"` and
  `"staging"`, and the empty store is its own test.
- **`T-007` left `verify-packed-assembly-versions.ps1` out of `spec-process.md` § Gates.** That table
  says what each gate fails on, and this gate gained a failure this round — but every row in it is a
  gate `spec-gates.yml` runs or a documented on-demand spec tool, and this one runs from `publish.yml`
  over packed artifacts. Adding it would widen the table's subject from the SDD gate suite to the
  release pipeline. The new failure is stated where its criterion lives, in `analyzers.md`, which names
  the script.
- **`T-007`'s second review round found this round's own lesson unapplied to the accumulator that
  matters most.** The first round's fix pinned `$absent`, `$checked`, `$requiredCount` and
  `$matchedPackages` with two-package fixtures — and left `$mismatches`, the one carrying the 0.11.1
  stale-assembly defect the whole script was minted for. Both new pair fixtures built every package at
  the honest version, so no fixture anywhere had a mismatch in a non-last package. Measured:
  `$mismatches += …` → `= …` leaves the self-test reporting `Self-test passed.` A third pair fixture,
  stale in the first-sorted package, kills it. The journal line this round wrote — *a cross-item
  accumulator is unpinned until a fixture holds two items and the interesting one is not the last* —
  was true and applied to four of five.
- **`T-007`: two `unchecked` branches had no case at all, and one mutant found each.** The gate's
  docstring promises a foreign assembly is "listed as unchecked rather than trusted silently", and the
  same list carries a file named like an assembly that is not one. Neither branch had a fixture, so
  both list-appends were overwritable. Two cases now, each with **two** entries, because one entry
  cannot tell a list from a variable holding the last thing put in it. Twenty mutants, no survivors.
- **`T-007`: "absent" and "present" were the wrong words in both new criteria, and each was falsified
  by the other's test.** `AC-CTRL-014.7` said "WHEN a duration **is present** and is neither form … SHALL
  refuse" — and an empty JSON string is present, is neither form, and reads as zero, which
  `AC-CTRL-014.6`'s own row asserts. The pair is now written on *text*: `.6` decodes one **carrying no
  text** as zero, `.7` refuses one **carrying text** that is neither form. Same defect as the first
  round's blocker, moved one criterion over by the split that fixed it.
- **`T-007`: `AC-CTRL-014.6` was untrue of the shape its own doc comment points at.** The converter's
  summary says the framework applies it to `TimeSpan?` through the nullable wrapper — and that wrapper
  answers a JSON `null` itself, so a `TimeSpan?` decodes `null` as `null`, never reaching the decoder.
  Probed: `TimeSpan? null -> <null>`, `TimeSpan? "" -> 00:00:00`; the non-nullable `TimeSpan` reads
  both as zero. The criteria are the non-nullable decode and the prose says so, rather than claiming a
  rule the serializer overrides.
- **`T-007`: `AC-CTRL-014.7`'s "naming that text" does not hold for a non-string payload, and that is
  now stated rather than claimed.** A number or object body is refused as `JsonException` — by the
  serializer, before the decode is reached, with a message that does not name what it was offered.
  Binding the clause to *text* leaves that payload outside both criteria, which is right: nothing in
  this repository decides it. The prose records the behaviour instead of minting a criterion for
  someone else's rule.
- **`T-007`: the two new criteria were textually indistinguishable from `AC-CTRL-009.3` on the same
  page.** That criterion already says the control surface's codec accepts "a duration in either its
  ISO-8601 or its .NET spelling"; `AC-CTRL-014.6` said the same words about a different decoder. Two
  criteria whose sentences cannot tell each other apart is how the first round's third blocker
  happened — a test citing the wrong one of two nearby ids. Both new criteria now say **JSON**.
- **`T-007`: the false `ANLZ` premise survived in a third place in the archived doc.** The first
  round's correction reached map row 152 and the delta line; the row-152 *sketch* still stated the
  refuted reason as fact, in a bold heading, so the file contradicted itself. The checkpoint above
  claiming "wrong in five places … corrected in all of them" was a wrong count in the same way the
  entry it corrects was. The sketch now carries the correction as a blockquote, and
  `AC-ANLZ-018.3`'s "rather than leave a consumer's build to report the missing file" — which
  overclaims, since `verify-packages` needs `publish` and so runs after both pushes — is reworded to
  what `analyzers.md`'s prose already said honestly.
- **`T-007`: the case built to be platform-neutral was the one CI failed on.** The repository-reading
  case compared each rule's required entry against the `.csproj` through `Split-Path`, and passed at
  the desk. On the Linux runner it reported `FAIL rule 'Vion.Dale.Sdk -> analyzers/dotnet/cs/…' is not
  content Vion.Dale.Sdk.csproj packs` — the gate's first CI run, red, on the case whose whole purpose
  is to hold wherever it runs. Which of `Split-Path`'s two uses diverged is not recorded: the fix is
  not to find out. A required entry is a **zip** path, always forward-slashed, so it is parsed as one
  and the `.csproj` text is normalised to `/` — both sides platform-neutral by construction, nothing
  asking the platform. (`Split-Path` does answer in backslashes on Windows whatever separator it is
  given; measured. That is enough to know a comparison built on it is not neutral by accident.) The
  `FAIL` branch also prints what it looked for, because a bare verdict on a case that only fails
  somewhere else is a second round of guessing. Third platform trap in this task, after `T-005`'s
  hidden-directory walk and this round's own `Test-Path`.
- **`T-007`: the gate's second CI run failed on the real artifact, and the gate was wrong, not the
  artifact.** `verify-packages` reported `Vion.Dale.Sdk 0.0.0-ci.622 -> analyzers/dotnet/cs/…: absent`.
  The package carries it: the entry is `analyzers/dotnet/cs//Vion.Dale.Sdk.Generators.dll`, with a
  **doubled** separator. Measured across the whole downloaded artifact set — every entry produced by a
  `PackagePath` ending in a separator has it (`analyzers/dotnet/cs//` and all 43 `tools/net10.0//`
  entries), while the `lib/` entries, which no `PackagePath` places, are clean. NuGet resolves either
  form, and the published packages install and judge correctly; the strict match was the defect. Entry
  names are now compared with repeated separators collapsed, and the fixture that proves it writes its
  entry name **verbatim** into the zip, because `ZipFile.CreateFromDirectory` normalises separators —
  which is exactly why eleven fixtures agreed with each other and none of them with the tree. The
  carry-over asks for "a fixture shaped like the one production runs on"; a fixture built by a
  different tool than production's is not one, and no amount of mutation testing inside that suite
  could have shown it. The gate now runs clean over the artifact set that failed it
  (`clean (18 assemblies across 18 packages)`, `required content: 1 of 1 present`).
- **`T-007` did not fix the doubled separator, and it is a ledger candidate rather than this task's.**
  `PackagePath="analyzers\dotnet\cs\"` and `PackagePath="tools\net10.0\"` in
  `Vion.Dale.Sdk.csproj` end in a separator, which `dotnet pack` doubles on Linux and not on Windows,
  so the same source produces two different artifacts by runner. Benign today — NuGet resolves both —
  but it means any tool that reads entry names exactly must know, and the two artifacts are not byte
  comparable. Dropping the trailing separator is a packaging change to a released package's layout,
  which is `releasing.md`'s subject and not a fix-sized repair. Recorded, not absorbed.
- **`T-008`: the ratchet's scope is 388 unmarked public types across nine packages, and the task is
  the three Modbus packages of it.** Reviewer's question 2 named "the two DevHost packages,
  `Vion.Dale.ProtoActor`, the four undeclared SDK namespaces and the 43 Modbus types"; `T-006`
  corrected the last two figures and this task measured the rest. Counted off the built assemblies at
  `bf6c939` — the manifest generator's own basis is a source scan of `[PublicApi]`, so the count of
  *unmarked* types has to come from the assemblies, which is where a source count and an assembly walk
  disagree by tens: `Vion.Dale.Sdk` **217** across 17 undeclared namespaces (97 of them `Examples.*`),
  `Vion.Dale.DevHost` **101** across 5, `Vion.Dale.DevHost.Web` **14** across 6, Modbus Core/Rtu/Tcp
  **41** across 15, `Vion.Dale.ProtoActor` **10** across 2, `Vion.Dale.DevHost.Xunit` **3**,
  `Vion.Dale.Plugin` **2** — **43 namespaces** in all, counting those that hold an unmarked type. (The
  basis matters, and the first draft of this line mixed three: it added the SDK's 17 *undeclared*
  namespaces to Modbus's 15 *total* ones and reached 47. Every namespace of the nine assemblies is 50;
  every undeclared one is 39; every one holding an unmarked type is 43, and only that one is the size
  of the work.) Every one of the 388 is a per-type judgment, and
  each one answered `[PublicApi]` also owes a docs-site XML summary, because a `[PublicApi]` type's
  docs are a shipping surface. That is seven package-sized passes (the three Modbus ones being one),
  not one task. The ruling does not move — decision **0145** records it for the whole roster — and
  `T-008` lands the pass the ledger says is one piece of work (`TKIT`'s row **is** the Modbus half),
  leaving **347 types across six packages** in the ledger with their measured numbers and the decision
  cited. Two shipped packages nobody had named are in the same position:
  `Vion.Dale.DevHost.Xunit` and `Vion.Dale.Plugin`, both on `set-version.ps1`'s release roster.
  **The first two counts here were wrong and the review round measured them again.** `Vion.Dale.Sdk`
  was written 221 and `Vion.Dale.DevHost` 98: the walk they came from resolved a nested type's
  namespace through its *immediate* declaring type, which is nil for anything nested two deep, and its
  compiler-generated-name filter matched a doubly-nested `<M>$` while missing the `<G>$` above it. Both
  are now `Assembly.GetExportedTypes()` with `GetCustomAttributesData()`, the pair the analyzer and the
  manifest generator agree with, and the SDK's 217 is stated as **author-declared**: eight further
  exported types are the `<G>$`/`<M>$` pairs the four C# 14 `extension` blocks emit, which no author
  can attribute at all. A number measured by a walk written for the occasion is a hypothesis until a
  second walk agrees with it — which is the same lesson as the citations, one level up.
- **`T-008`: `DALE014` matches a declaration as a prefix, so the `IO` row is not separable from the
  `BIND` row and four of `Modbus.Tcp`'s five declarations are inert.**
  `PublicApiDocumentationAnalyzer.cs:80` skips a type only when `ns != configured && !ns.StartsWith(configured + ".")`.
  So declaring `Vion.Dale.Sdk` — which is what the `IO` entry's "undeclared root namespace" asks for,
  for three types — arms all twenty of that assembly's namespaces and all 217 unmarked types at once;
  and `Vion.Dale.Sdk.Modbus.Tcp`'s four sub-namespace declarations add no rule its root declaration had
  not already made, which is why `DALE014` fired in `…Client.Implementation` and `…Server.Implementation`,
  namespaces nothing declares. Measured, not read: arming the analyzer over `Modbus.Tcp` reported 11
  types living in **five of its seven** namespaces, two of which nothing declares — which is the point:
  the root declaration reached them anyway. The consequence for `Modbus.Core`'s scope
  question — which of its seven namespaces are published — is that the answer is *the root*, one
  declaration, matching what both siblings already do. Recorded in `sdk-surface-conventions.md` § 8,
  which had none of this.
- **`T-008`: three of `sdk-surface-conventions.md` § 8's claims about `Vion.Dale.Sdk.Modbus.Core` were
  false, and the fourth was a miscount.** The page said that project "deliberately does not reference
  `Vion.Dale.Sdk`", that "its `PublicApiConfig.cs` is a local shim defining the attribute", and that it
  is in the manifest "on the strength of its 12 marked types". It references the SDK by
  `ProjectReference` (`Vion.Dale.Sdk.Modbus.Core.csproj:31`), it had **no** `PublicApiConfig.cs` at all,
  its one assembly attribute was `[DaleSharedAssembly]`, and the generator reports **20** marked types.
  The page also said "twelve assemblies declare `[PublicApiNamespace]`; twelve are in the manifest; the
  two sets are not the same twelve" — eleven declared it, and the odd one out was exactly this package.
  All four corrected. This is the `T-007` lesson again: re-resolving a citation means reading what the
  cited thing *does*, and a convention page states facts that rot like any other.
- **`T-008`: arming `DALE014` over new ground found a diagnostic no author could satisfy.**
  `Vion.Dale.Sdk.Modbus.Core.Server.ModbusServerBufferAccessor` is a public `delegate`; the analyzer
  walks every named type, so it asked for a mark — and both marks' `AttributeUsage` listed
  `Class | Interface | Enum | Struct`, so writing one produced `error CS0592`. Measured both ways: the
  build fails with the mark, and reports `DALE014` without it. The marks were widened to accept
  `Delegate` rather than the diagnostic narrowed to stop asking, because narrowing the ratchet inside
  the decision that widens it is the wrong direction; `AC-ANLZ-012.7` states the rule. What is *not*
  fixed is downstream: `generate-api-reference.cjs`'s type pattern does not match `delegate` either, so
  a `[PublicApi]` delegate would carry a mark the manifest silently drops. Nothing declares one, the
  fix is a second pattern plus that script's first test harness, and it is in the ledger under `ANLZ`
  rather than absorbed here.
- **`T-008`: the marks were inherited, so reflection and the build disagreed about who was marked.**
  `Vion.Dale.Sdk.Modbus.Rtu.ModbusRtu` is plumbing deriving from the published `LogicBlockContractBase`.
  `DALE014` reported it — `ISymbol.GetAttributes()` returns declared attributes only — while
  `Type.GetCustomAttribute<PublicApiAttribute>()`, whose `inherit` defaults to `true`, returned the
  base's mark. The package-surface test written to the `Vion.Dale.Sdk.Http.Test` precedent failed on
  it: expected four published types, got six. **Four pre-existing test files across three areas read
  the marks that way** — `AnalogIo`, `DigitalIo`, `Http` and `TestKit`; this checkpoint said six across
  five until the review round counted them, the fifth candidate already passing `inherit: false`. So the
  fix is `Inherited = false` on both attributes rather than an `inherit: false` argument
  at each call site — one change that makes the analyzer, the manifest generator's source scan and
  reflection one reader. Every existing package-surface suite stayed green: no other package has a
  public subclass of a marked type, which is why this had never shown. `AC-ANLZ-012.8`.
- **`T-008`: the Modbus classification is 16 published and 25 plumbing, and the rule it applies is
  *who names the type*.** The `MODB` entry counted 43 unmarked; the assembly walk found **41**, and the
  entry's own reading of the set held: the eleven exception types Core and TCP leave unmarked were the
  accidental half (they are what a block's error callback catches, and **five** of them are named in
  criteria on `modbus.md` already — this checkpoint said seven until the review round counted them, and
  the other six are on the page as a failure *class* rather than by name), the TestKit-substituted seams
  were the deliberate half. Two
  judgments went against a signature-closure reading and are stated on the page rather than assumed:
  `IRequestFactory` appears in the **public constructor** of the TestKit's published
  `SynchronousRequestQueue` and is still plumbing, and `ModbusLinkAccumulator` was already
  `[InternalApi]` while appearing in `IRequestQueue.Initialize`'s parameter list — so the tree itself
  says the rule is not a closure. One mark is a correction rather than a classification:
  `Vion.Dale.Sdk.Modbus.Rtu.DependencyInjection`'s doc comment said "consumers do not call this
  directly" while the SDK's own example calls it by hand
  (`examples/Vion.Examples.ModbusRtu/Vion.Examples.ModbusRtu.DevHost/Program.cs:27`), which is what
  makes it published. The manifest moved 122 → 138 types and no assembly, because all three Modbus
  packages were already in it on their existing marks.
- **`T-008`'s review round found the count wrong in the task about counting, and one page half-corrected.**
  Five of its findings changed the tree. The two numbers are above. The page: § 8 of
  `sdk-surface-conventions.md` was corrected in four places while a bullet six sections down, under
  *Known non-conforming code*, still said "`Vion.Dale.Sdk.Http`, `.Modbus.Core`, `.Modbus.Tcp` and
  `.Modbus.Rtu` reference it not at all" — one claim stale since the HTTP pass and three falsified by
  this PR, with "eight projects" where the grep now gives eighteen. Correcting a page where the
  correction was noticed leaves every other section that made the same claim. The rest: the `LIFE`
  entry's 28 + 7 + 8 did not reach the 47 the same sentence claimed, because the 8 is what that page
  *specifies* and 12 is what the namespace *holds* — both now stated; "seven exceptions are named in
  criteria on `modbus.md`" is five; "six test files across five areas" read the marks through
  reflection's inheriting default when it is four across three, the fifth candidate passing
  `inherit: false` already. Each is corrected where it was written as well as here, because a number
  refuted thirty lines below where it is asserted is still asserted. Every one is a claim that would
  have shipped as prose nobody could check without re-deriving it.
- **`T-008`: the change that minted "a diagnostic no author can satisfy" introduced one.** The
  `[PublicApi]` fixture for `AC-ANLZ-012.8` drew `DALE013` in every build of `Vion.Dale.Sdk.Test` —
  and the type carries a `<summary>`. `DALE013` reads `GetDocumentationCommentXml`, which is empty for
  every type in a project that sets no `GenerateDocumentationFile`, and that test project sets none.
  Measured: 1 occurrence, 0 under `-p:GenerateDocumentationFile=true`. Suppressed at the site with that
  reason, stated under `AC-ANLZ-012.1`, and in the ledger — turning the doc file on for a test project
  of that size changes what the whole project warns about, which is not this task's to decide. Third
  shape in the family, after the delegate and the inherited mark. The ledger entry also carries what
  the re-check turned up: thirteen of the eighteen roster packages set the property, and they are the
  twelve inside the ratchet plus `Vion.Dale.DevHost.Xunit` — so **the five that do not are exactly the
  five the ratchet has yet to reach**, and each meets this the moment it is armed. The first draft of
  that sentence said "every shipped package sets the property", which is false by five.
- **`T-008`: two Modbus marks are judgments and neither was written down until the review asked.**
  `Vion.Dale.Sdk.Modbus.Core.ServiceCollectionExtensions` is `[PublicApi]` with **no consumer source
  naming it** — every caller of `AddDaleModbusCoreSdk` is inside this build, because `AddDaleModbusTcpSdk`
  and RTU's `DependencyInjection` call it for their consumers. It stays published because it is the sole
  registration entry point of a roster package, and marking it plumbing would leave that package with no
  published way to register the converter it publishes; it is the mark most open to being overturned, and
  `modbus.md` now says so. `Vion.Dale.Sdk.Modbus.Rtu.DependencyInjection` is `[PublicApi]` where the
  identical class in `.DigitalIo` and `.AnalogIo` is `[InternalApi]` — real, because RTU ships no
  `AddDaleModbusRtuSdk` and a development host has no plugin loader to discover an `IConfigureServices`,
  so the SDK's own example constructs one by hand. Whether RTU should ship the extension its siblings do
  is a new published member, so it went to the ledger rather than into this PR.
- **`T-006`: the review subagent's own round is why five of these checkpoints read as they do.** It
  refuted the Modbus premise, turned "four of six misroute" into all six at a uniform +3, found the
  duplicate-lead hole in the script, and showed that the self-test's repo-facing case had quietly
  made this a gate on every PR in the repo. Each is recorded above with what was measured, not with
  who found it; this line is the pointer for the retro, which counts review catches.
- **`T-009`: the `DALE043` escalation line overstated who is bitten, twice.** It reads "the first
  consumer carries five gating suites over fielded blocks — so this fails a consumer's build".
  Measured at `logic-block-libraries@7fba0e04`: **three** suites named `*GatingShould`, and **29**
  `[IncludedWhen]` declarations across three blocks. So no fielded gate draws the false error today.
  The defect is real and error-severity; the filing (VION-194) says both, because "fails a consumer's
  build" as an unqualified present tense is a claim an external reader would check and find false.
- **`T-009`: the review round found the escape mechanism named backwards, and it was the correction's
  own claim.** The first version of the checkpoint above said the 29 gates "are all on
  `ChargePoint` / `MeasuringGroup`, which are service-bearing components and satisfy `IsGateable`'s
  **third** branch, never reaching the symbol-only `AllInterfaces` lookup". That is causally
  impossible: the lookup lives **inside** branch 2
  (`IncludedWhenPredicateAnalyzer.cs:211` calls `TypeImplementsLogicInterface`, whose `AllInterfaces`
  test is `:222`), and branch 3 is `:217`, evaluated only after it. What actually saves all 29 is
  branch 2's **first disjunct**: every one of them carries an explicit
  `[LogicBlockInterfaceBinding(typeof(…))]` (verified on all 29, not sampled), and `HasAttribute`
  reads the attribute's presence, never its argument. Branch 3 would also be true — `ChargePoint` and
  `MeasuringGroup` do bear services — which is exactly why the wrong reason looked right.
  **The consequence is not cosmetic:** if the attribute alone is what makes a property gateable, then
  the struck entry's claim that the remedy "names the same unresolved type" is probably false, and
  `[LogicBlockInterfaceBinding(typeof(…))]` is probably a live workaround. Nothing here executes it —
  the pin's proxy cannot — so it stays a hypothesis with a mechanism behind it. VION-194's Origin
  carried it as "may suppress it" when the item was filed and says "is likely a workaround" after the
  correction below; both are hedged, and neither is a tested claim.
- **`T-009`: the ledger's "all ten" verifier wrappers are eleven — by widening the predicate, not by
  correcting a count.** The struck entry said "Every `Verify<Payload>Payload(ByteBuffer)` wrapper —
  all ten **in 3.7.0**". Of that exact shape there are **ten**, in 3.7.0 and at `v10.3.0` alike, so the
  entry was right on its own terms; the eleventh, `VerifyRemoteFunctionInterfaceMessage`, has the
  identical defect but is not a `Verify<Payload>Payload`. Two things were checked and neither is the
  explanation: it is **not** a version difference (reflecting over the **shipped 3.7.0 assembly** for
  public static `Verify*(ByteBuffer)` returns the same eleven as `a23623a`), and it is **not** a
  miscount — an earlier draft of this checkpoint said "miscount" and the second review round refuted
  it. What changed is the question asked. VION-197 states eleven and names the split.
- **`T-009`: VION-132's evidence carries a false bullet, and it is the one the coordinator's review
  marked "not re-read".** The bullet claims `ChangeThresholdRegistry.cs:157` "does skip dynamic
  assemblies". It does not: `:145-158` is a `try`/`catch` around `GetReferencedAssemblies()` whose
  comment says a dynamic assembly "may refuse" the call. `DynamicProxyGenAssembly2` does not refuse
  it — VION-132's own probe reads `Vion.Dale.Sdk.DigitalIo` out of it — so that scan would admit the
  proxy assembly exactly as the contract factory does. The correction is posted on VION-132 rather
  than edited into it, because the item's body is the maintainer's.
- **`T-009`: two of the five were reproduced rather than traced, and it changed what they claim.**
  The operator declined the "traced" evidence class at the gate. `VerifyDiStatePayload` on a valid
  buffer throws `ArgumentException: FlatBuffers: file identifier must be length4`; on an empty one it
  answers `false`; the inner `Verify` with a `null` identifier answers correctly. `hal-sim`'s argument
  order reads back `hardware_block_instance_id='endpoint-A'` where `hal-raspberry`'s reads back
  `'hwblock-1'`. Both Origin lines now say `reproduced` with the observed output. The other three stay
  `traced` and say why in the REPORT: one needs a Metalama-hosted build, one needs the platform
  endpoint, one needs a running gateway.
- **`T-009`: closing VION-133 retires `ShareContract` with no in-repo record.** `D7` chose branch (a),
  but the item on the board had already been rewritten to branch (b) on 2026-09-06 — narrowed to the
  wiring-editor guard and `DevConfigurationBuilder.ShareContract`, neither of which decision 0021
  answers. The closing comment declines both framings, the second on scope (Tier C for the editor;
  dead surface for `ShareContract`), on the operator's ruling at the gate. `ShareContract` has **no**
  ledger entry and no spec mention — `grep` over `_findings.md` and every page finds nothing — so the
  reopen condition on VION-133 is now its only record. **Ledger-correction candidate for the
  operator:** whether `Vion.Dale.DevHost/DevConfigurationBuilder.cs:139` earns a `leave` entry as dead
  surface. Not absorbed here.
- **`T-009`: the task line's five is right, and its numbers needed no correction.** Five
  `Escalated to the operator as a Jira candidate` lines (one wrapping across `:328-329`), five
  routings agreeing with `D7`, plus the VION-130 link row — six `Jira` rows, as `T-006` recorded. The
  first task line in five whose count held.
- **`T-010`: the clause that blocks the ceiling is `cts.IsCancellationRequested`, not `timeout != null`.**
  Row 28n and the task line both say the predicate "fires only when a per-request timeout was given,
  and widening it is one line". Widening it that way fixes nothing. `HttpClient` cancels its *own*
  linked source when its bound elapses and never touches the one the executor passed, so
  `cts.IsCancellationRequested` is false on the ceiling's path whether or not a per-request timeout
  was set — and `cts.IsCancellationRequested` is only ever true when one *was* set, which makes
  `timeout != null` a null-guard for `timeout.Value` and nothing else. Measured, not reasoned: the row
  now named `NameClientBoundUnderLongerPerRequestBound` passed before the fix, with
  `timeout != null` true, asserting the `TaskCanceledException` it received — which it could only do
  with `cts.IsCancellationRequested` false. Two mutations were run, and they redden differently.
  Dropping `timeout != null` literally, as the entry proposes, leaves the predicate false on both
  ceiling paths and reddens the two rows on the *class* alone. Dropping `cts.IsCancellationRequested`
  instead — the charitable reading, and the only one of the two that relabels anything — reddens the
  longer-bound row on the *number*, which reads "Timed out after 60 seconds" for an exchange that ran
  50 ms. Neither is the fix: the first does nothing and the second names the wrong bound.
- **`T-010`: the executor does know the client's timeout, so the bound is named on both paths.** Row
  28n's reason for parking the fix — "saying 'after {n} seconds' on the ceiling's path needs the
  executor to read `HttpClient.Timeout`, which it never does" — reads the failure path, where there
  is a factory and no client. `SendAsync` holds the client as a local, and relabelling in its own
  `catch` names `httpClient.Timeout` with no threading and no second `CreateClient`. So the scope
  guard's out (an unnamed bound) was not needed, and `AC-HTTP-008.2` states the number.
- **`T-010`: row 54 confirmed, as `T-006` recorded.** No `DaleSharedAssembly` entry has ever been in
  `_findings.md` (`grep -c` → 0 at `fe83284` and here), and `AC-HTTP-013.3` (`docs/specs/http.md:347`,
  rationale at `:363` — `:330` and `:346` on `fe83284`, before this PR's own edits moved them) already states the decline the task line asks for. Nothing changed for this
  half. The one page pointer this fix *did* owe was the other half's: the `AC-HTTP-008` prose ended
  "the finding ledger carries the ask to normalise the two", and that sentence went with the entry.
- **`T-010`: the ledger is 68 entries, 12 rows resolved.** `T-009` left 69 and 11; deleting row 28n
  as fixed moves both by one (`fix-now 5, decision 6, Jira 6, leave 63` unchanged — the buckets
  count rows, and 28n's row now reads *resolved*). Row 45's entry cites the same file: its
  `:329` and `:297` are `:344` and `:301` after the fix, corrected in place, and its claim — that the
  relabel asks what failed rather than only whether the source fired — is untouched, the new
  `catch` taking only `OperationCanceledException` and only around the header exchange.
- **`T-010`: the review round found the first cut of the `catch` filter relabelling cancellations that
  were not the ceiling.** `!cts.IsCancellationRequested` alone reads as "the only other cancellation
  left is the client's", and that inference is wrong: a `DelegatingHandler` in the named client's
  pipeline can cancel on a token of its own, and the runtime can add one by name. Measured through the
  real executor rather than argued — a throwaway probe with a stub handler throwing an
  `OperationCanceledException` under an explicit 30 s client bound reached the block as
  `"Timed out after 30 seconds"` for an exchange that took no time, wrapping a transport failure
  `AC-HTTP-006.1` says arrives as the handler threw it. (The permanent row sets no bound, so it runs
  against `HttpClient`'s own 100 s default; the number in the message was the probe's, and nothing in
  the suite produces it.) The filter
  now also requires `exception.InnerException is TimeoutException`, which the client sets for its own
  bound and nothing else, and `DeliverOneExceptionClassPerFailure` gained a `TransportCancellation`
  row that reddens without it. One caveat stated rather than hidden: that inner exception is .NET 5+
  behaviour, so on a host older than the one this SDK's plugins load into the ceiling would go back to
  arriving as a cancellation — a degradation to today's behaviour, never a false claim, which is the
  right way round for a bound the message names.

---

## Spec delta (to distill)

> No criterion changes **owed here**: this doc's own deltas are process documents, each a task
> above, so the archive gate has nothing to compare. Criteria that a task's lane-1 fix moves are
> carried by the page edit in that task's PR and are listed in *Drift checkpoints*, not here —
> `T-007` minted `AC-CTRL-014.6`, `AC-CTRL-014.7` and `AC-CLI-019.3` and reworded `AC-ANLZ-018.3`,
> and `T-010` rewrote `AC-HTTP-008.2`.
> The doc archives when the Implementation state reads done for every task, by the session that
> lands `T-019`.

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
