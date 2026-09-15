# Retro-2 — sharpened review checks

**Date:** 2026-09-15 · **Window:** 2026-09-13 → 2026-09-15, 106 entries (review 72, gate 15, brief 13, decision 6), archived in [`journal-2026-09-13-to-2026-09-15.md`](journal-2026-09-13-to-2026-09-15.md) · **Extra path:** [`journal-2026-09-10-to-2026-09-11.md`](journal-2026-09-10-to-2026-09-11.md), mode *whole*, 54 entries · **Previous records:** [`2026-09-10-sdd-migration-retro.md`](2026-09-10-sdd-migration-retro.md), [`2026-08-12-review-mining-round.md`](2026-08-12-review-mining-round.md)

One fresh-context reader over the window, the extra path and both records; the counts below were
recounted against `origin/main` at `8231636c` with the commands under § Commands. Cluster membership,
and the hygiene lists marked as judgement, are the reader's classification: a command confirms each
pointer is an entry, not which cluster it belongs to. The reader read the checkout of an unmerged branch
whose journal carried four more entries; they are not in this window and fall into the next one when
that branch merges.

## The clusters

`AR:n` is a line of the archive above; `RJ:n` a line of the extra path. Each entry has one primary
cluster; with the lines outside every cluster listed below, the 106 and the 54 partition exactly.

| Cluster | Count (window + RJ) | Window (`AR`) | RJ |
| --- | ---: | --- | --- |
| C — a claim not checked on the thing it describes | 10 + 15 | 23, 37, 47, 69, 91, 133, 143, 145, 163, 175 | 3, 8, 12, 13, 16, 25, 26, 27, 28, 30, 42, 45, 47, 49, 52 |
| A — a test that cannot fail, a red run unread, a clause no test reaches | 16 + 5 | 21, 25, 49, 55, 59, 63, 75, 101, 111, 113, 135, 141, 151, 161, 165, 213 | 7, 9, 11, 43, 54 |
| B — a change leaving sites it affected: text it falsified, reach unchecked | 15 + 6 | 11, 13, 53, 95, 129, 139, 159, 169, 173, 189, 197, 201, 205, 207, 209 | 10, 14, 17, 39, 55, 56 |
| D — a brief, amendment or RFC that was wrong | 13 + 6 | 3, 5, 17, 31, 41, 65, 83, 85, 97, 117, 119, 123, 211 | 4, 22, 29, 37, 38, 40 |
| E — machine and tool traps | 10 + 2 | 33, 35, 51, 57, 71, 87, 89, 99, 107, 125 | 24, 41 |
| N — a gate reporting wrongly (false green, false red, SKIP) | 7 + 3 | 7, 61, 77, 79, 81, 181, 191 | 34, 35, 36 |
| K — a precedent copied where its property does not hold, or not followed where it does | 6 + 3 | 15, 29, 67, 115, 137, 187 | 48, 50, 53 |
| M — the CI change scope missing one more input kind per review round | 7 + 0 | 177, 183, 185, 193, 195, 199, 203 | — |
| F — mutation-run mechanics giving no result or destroying work | 3 + 2 | 19, 43, 157 | 6, 51 |
| I — a decision taken instead of raised | 2 + 3 | 45, 127 | 31, 32, 46 |
| H — dispatch and session orchestration | 1 + 2 | 153 | 20, 21 |
| Q — test or harness isolation | 2 + 0 | 103, 105 | — |
| O — one rule with more than one owner | 2 + 0 | 9, 179 | — |

Outside every cluster: decisions `AR` 93, 121, 131, 149, 167, 171; singletons `AR` 27, 39, 73, 109, 147,
155 and `RJ` 15, 18, 19, 23, 44; positive signals `RJ` 5, 33. `(second ask)`: `AR` 77 (after 61), `AR` 87 (after 57, 71), `RJ` 30.

**Recurred despite earlier landings.** Retro-1's P5 did not stop cluster A. Rules stamped inside the
window recurred after their stamp: a red read off an escaping exception (`AR` 151, 165 after 21), a
`git checkout` restore (`AR` 157 after 19, 43; `RJ` 51), a precedent's property (`AR` 67 after 15, 29),
the CI scope (`AR` 183–203 after 177), journal-lint SKIPped locally (`AR` 61, 77 after 7).

## What landed

- **P5 — the red's source** · rung 2 · [`docs/review-checks.md`](../review-checks.md): a mutation's red
  read off the summary line or from an escaping exception is not a red.
- **P3 — text a change makes false** · rung 2 · [`docs/review-checks.md`](../review-checks.md): the sweep
  greps the words a behaviour is described in, across the tree.
- **Nested worktrees** · forwarded · the architecture repo's journal, branch `chore/retro-feedback-journal`:
  this round's own session placed its worktree under `.claude/worktrees/`, a second ask. A
  `WorktreeCreate`/`WorktreeRemove` hook in the dispatch plugin was proposed (rung 3); the decision is
  that repo's.

## Proposed and not landed

- **A mutation-run script** (A, rung 3) — replaced by the P5 clause: the script was the costliest landing
  and fixes the mechanics of a run, not the omission of one.
- **Tests for CI's change scope** (M, rung 3) — dropped: the fourth review round had already inverted the
  rule to a list of inert kinds with everything else building, and nothing recurred after it.
- **One `dotnet test` per test project in `/check -Test`** (E's low-memory kills, rung 3) — dropped.
- **P6 — the precedent carries its property** (K, rung 2) — dropped after a second explanation. The
  existing precedent rule in `spec-process.md` binds only a lane-3 table's `Why`; the window's copies
  were code, tests, a workflow job and a skill.

## Hygiene

- Every entry is on the header's grammar: `where` among the five, four fields, none over 400
  characters, none above the marker, dates in order.
- Entries stating the fix: 30 (`AR` 3, 5, 7, 19, 35, 49, 55, 57, 67, 87, 89, 95, 99, 101, 103, 105, 107,
  109, 135, 143, 145, 157, 161, 163, 165, 175, 177, 179, 191, 203). Entries carrying reasoning: 11
  (`AR` 15, 21, 29, 45, 49, 61, 77, 79, 177, 181, 203). Both are the reader's judgement, not recounted.
- Entries quoting: 4 (`AR` 45, 73, 75, 209).
- Review entries naming neither the file that should have prevented them nor a stamp: 19 (`AR` 11, 13,
  23, 39, 61, 67, 95, 101, 103, 105, 109, 175, 179, 197, 201, 205, 207, 209, 213) — judgement: a
  phrase grep finds 23, and the other four name a file in words it misses.
- Brief entries the agent found carrying no `(self)`: `AR` 3, 5, 17, 31, 117, 119 (the reader's reading).
- `AR` 87 carries `(self)` beside `(second ask)`.

## Pruned

Nothing. No check has gone three rounds unnamed. D4 is named in neither retro-1's window nor this one
(`grep -cE '\bD4\b'` → 0 in both archives and in the extra path).

## Left, and why

- **C** — its rungs 1 and 2 are in place (D2, P4, the claim rule in `comment-conventions.md`, the
  probe-shape rule in `spec-process.md`); review caught each recurrence under them, and no gate reads a
  claim about behaviour.
- **D** — briefs are written by the architecture repo's commands; the implementer caught every wrong
  brief before code.
- **N** — reached rung 3 inside the window (`check.ps1`, `publish.yml`); nothing recurred after the last
  stamp.
- **E's byte traps** (`AR` 33, 51, 99; `RJ` 24) — three different tools, one caught by the build. A gate
  is feasible: 0 invalid-UTF-8 files and 0 tab bytes in `.cs` across the 1,183 tracked `.cs .md .ps1 .yml .json .props .targets` files at `8231636c`.
- **F** folded into the P5 clause; **I**, **H**, **Q**, **O** too small to act on.

## The proposal's shape

The first proposal relayed the reader's report whole and then a table with one line of reason per row;
the human found the report overwhelming and the table missing context. The second — per landing, what
changes in the repo, what it costs, the alternatives, and a pick — was the one decided on, and was asked
for as the level of the next round. Journaled in the architecture repo, where the retro skill lives.

## Commands

PowerShell from the repository root. `$AR` and `$RJ` are the archive and the extra path.

```powershell
$AR = 'docs/retro/journal-2026-09-13-to-2026-09-15.md'; $RJ = 'docs/retro/journal-2026-09-10-to-2026-09-11.md'
$entries = { param($f) Select-String -Path $f -Pattern '^\d{4}-\d{2}-\d{2} · ' }
(& $entries $AR).Count                                                      # 106
& $entries $AR | Group-Object { ($_.Line -split ' · ')[1] } | Select-Object Name, Count   # review 72, gate 15, brief 13, decision 6
(& $entries $RJ).Count                                                      # 54
(& $entries $AR | Where-Object { $_.Line.Length -gt 400 }).Count            # 0
(& $entries $AR | Where-Object Line -match '"[^"`]{6,}"').LineNumber        # quotes: 45, 73, 75, 209
Select-String -Path $AR, $RJ -Pattern '\(second ask\)' | ForEach-Object LineNumber      # AR 77, 87; RJ 8, 13 (quoted), 30
Select-String -Path $AR, $RJ, 'docs/retro/journal-2026-08-12-to-2026-09-10.md' -Pattern '\bD4\b'   # none
# every cited pointer is an entry, and with the outside lists the pointers cover every entry once:
# compare (& $entries $AR).LineNumber against the union of the table's AR column and the outside lists
@(git ls-tree -r --name-only 8231636c | Where-Object { $_ -match '\.(cs|md|ps1|yml|json|props|targets)$' }).Count   # 1183
```

## For the next round

1. Is P5's red-source clause named by a journal line, and does cluster A shrink?
2. Is P3's text clause named on stale-text findings, or do they still cite `comment-conventions.md` alone?
3. Still no D4 — prune it.
4. Did a file a test reads get skipped by `read_by_build`?
5. Did low-memory kills or edit-tool byte traps recur?
6. Did the nested-worktree hook land in the dispatch plugin, and did any session nest a worktree after it?
7. Did cluster K — a copied precedent — grow without P6?
