# Retro-1 — the SDD migration's window

**Date:** 2026-09-10 · **Window:** 2026-08-12 → 2026-09-10, 359 journal entries (`process-journal.md:83–445`,
archived in [`journal-2026-08-12-to-2026-09-10.md`](journal-2026-08-12-to-2026-09-10.md)) · **Method:** five
dated reader subagents over disjoint slices, then one merge reader over the five reports and never the
journal · **Counts:** every number below recounted with its own command at § 3 of
[`/vion-retro`](../../.claude/commands/vion-retro.md), not read off a report.

The first run of `/vion-retro`, over the window the fourteen area passes, the unification pass, the ledger
review and the close-out effort left behind. Retro-0 built the substrate; this round is the first that had
data to read.

## What was read

The journal below the retro-0 marker, in five slices: 83–161 (79 entries), 162–226 (65), 227–289 (63),
290–357 (68), 358–445 (84). The slicing is not a convenience — the merge pass found two of the window's
largest shapes invisible to every individual reader. Second inputs: the seventeen archived change docs
under [`../changes/archive/`](../changes/archive/) for their scorecards and drift checkpoints, the
in-flight [`2026-09-07-sdd-closeout.md`](../changes/2026-09-07-sdd-closeout.md), retro-0's note, and
`T-011`'s transcript harvest [`2026-09-sdd-pass-data.md`](2026-09-sdd-pass-data.md).

**Fourteen of the seventeen archived docs carry a scorecard**, not seventeen: the two unification docs and
the founding process doc have none. Pass 0's cost exists nowhere but the journal, and its *Drift
checkpoints* section is still the unfilled placeholder — a pass that skipped its own record.

## The clusters

Counts are distinct journal lines after merging across slices; the families overlap at their edges, and
about fifteen duplicate pairs journal one event twice, so they are within a few percent of each other
rather than exact.

| Cluster | Count | Slices | Visible per slice? |
| --- | ---: | --- | --- |
| A check that cannot fail | 72 | all five | yes — but #1 in only one |
| An unverified premise in a directive artifact | 59 | all five | yes — #1 in three |
| A produced claim asserted rather than observed | 57 | all five | yes |
| A sweep or fix reaching fewer sites than the shape | 34 | all five | **no — top-3 in one slice only** |
| A machine or tool trap repaid in briefs | 31 | all five | **no — top-3 in none** |
| A gate passing over what it does not read | 19 | four | no |
| A pointer with no durable referent | 19 | four | no |

The last four are the merge pass's yield. *A machine trap repaid in briefs* is the clearest case: ranked
fifth, eighth, fourth, fifth and fifth by the five readers, and larger merged than four of the five slices'
own third-place clusters. Two of its members are verbatim recurrences — the cleanup foot-gun at `:129` and
again at `:140` (*"second session to pay a round for it. The journal line did not prevent the
recurrence"*), and the stale global `dale` at `:304` and `:309` (*"the same trap, the same way"*).

## What recurred despite retro-0

Retro-0 landed the substrate rather than rules, so the question is whether the taxonomy held. It did not
travel, and the numbers say why.

**`D-hits`, the column retro-0's open question asks for** — trailing tags over 359 entries, all three
notations the file uses:

| `D10` | `D2` | `D9` | `D1` | `D3` | `D5` | `D6` | `D4` | `D7` | `D8` |
| ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| 36 | 20 | 16 | 2 | 2 | 2 | 1 | **0** | **0** | **0** |

`P1` 2, `P3` 2, `P2` and `P4` zero — and `P1`–`P4` existed for only the last 84 entries. Seventy lines
of 359 carry any tag at all.

Three readings, each load-bearing:

- **`D10` is 36 of the 79 D-stamps and carries three causes** — the unchecked claim, the incomplete sweep,
  and the wrong premise. Slice D's own retro-mapping puts five of its `D10`-stamped lines under `P3`, which
  had owned that shape since the day it was minted.
- **`D4`, `D7` and `D8` never fired.** `process-metrics.md` makes deletion a three-round judgment, so this
  is round one of three, not a verdict. `D4` is the analyzer-obligation check, and its silence is the
  reason nothing in this round reaches the ladder's top rung: the window was process, spec and harness
  work, with almost no consumer-facing authoring for an analyzer to see.
- **Tagging tracks the instrument, not the discipline.** Sixty of the seventy tagged lines sit in
  the two slices where a review command was the reviewing instrument; the three middle slices, where the
  passes ran coordinator critics instead, carry ten tagged lines across 196 entries. The longest unbroken
  run of untagged entries is **102**, over a quarter of the window in one stretch. All four scorecards in
  one slice record the classification gate as *"0 — delegated"*. The taxonomy is calibrated to a review
  mode half the window did not use.

## The two silent markers

**`(escape)`: zero in 359 entries, and the thing it counts happens.** At least twenty lines fit, named
independently by all five readers. Three structural reasons, one per era: the markers were defined in
`process-metrics.md` and never in the journal header that produces them
(`journal-2026-08-12-to-2026-09-10.md:22`); the passes ran critics
rather than `/vion-code-review`, so the marker's wording named an instrument that was rarely used; and the
header never decided whether a second review round catching the first counts, which is now the ordinary
case. Retired would have been the wrong call — the loop the number watches only started existing on
2026-09-09. Redefined instead, landing 4.

**The redefinition is not yet tested, and this round could not test it.** Its own review found four real
defects, and none of them is an escape: round 1 was the first round over this branch, so nothing had
already passed one. Two lines were written with an `(escape)` marker and corrected before commit for
exactly that reason. An escape needs a second round to exist, which is why the marker's first honest
reading is retro-2's, and why a `0` here still means unrecorded rather than none.

**Round 2 disagreed, and closing the disagreement is the more useful outcome than either answer.** It
argued that the author's own application of `P5` to the stale-tool code, journalled before round 1 ran,
was itself a round that missed two of three call sites — making round 1's catch an escape. The wording
allowed that reading, so the header now says a round is **a reader who is not the author**: a self-check,
however rigorous, is not one, or the marker swallows nearly every correction and measures nothing. The
line stays unmarked by decision, not by oversight, and retro-2 inherits a definition that no longer
turns on who is reading it.

**`→ codified:`: zero in 359 entries, and for all but the last day the stamp did not exist.** A true
negative for the stamp, a false negative for codification: `:301` is quoted verbatim inside `P1`; `:187`
proposed the doc-comment check and `doc-comment-lint` shipped in-window; five gates in one scorecard's list
were minted from in-window lines. None was stamped. **Not retro-stamped here, deliberately** — the stamp
exists so a later round can see *rule written, recurred anyway*, and a rotated window is invisible to the
next round, so stamping the archive buys nothing. That is a real seam between the two loops: `/vion-codify`
stamps on the branch, `/vion-retro` rotates the evidence away. Retro-2's first question is whether a stamp
has appeared at all.

## The seven questions

Asked by the change doc on 2026-09-07, against transcripts `T-011` has since harvested and deleted.
Answered from durable artifacts only; where the artifact is gone, that is said rather than reconstructed.

**1. Compaction versus handover.** *The premise is wrong in both halves.* `T-011` measured two **pass**
sessions compacting, not none — `SCEN` and `CTRL`, one each — and those are precisely the two passes where
amendments first fell to one. So compaction did not move output quality in the direction the question
assumes. Nor does the fresh-session rule explain the fall from six: amendments run **2, 3, 6, 2, 1, 1** and
then one for eight consecutive passes, so the fall to one happened at `SCEN` (pass 5), while the amendment
became *a fresh-session brief* at `LIFE` (pass 7), two passes later. The fresh-session fix-up itself is
older still — `GATE` (pass 3) invented it mid-pass, *"after two rounds in which the same defect classes
recurred"*, and the recurrence stopped there. What sits at the inflection is pass 4, `INTRO`, which added
the classification relay on top of it; six
amendments became two there and one at the next pass. The fresh-session brief held the gain; it did not
produce it.

**2. The brief check's yield.** Eleven to twenty-four wrong claims and eight to thirteen omissions per
brief, seven to eleven and a half minutes, one Opus subagent. A standing lane-3 rule and **not** a lane-2
one — see *What landed* for why, and landing 2 for what replaced it.

**3. Which checks pay.** Of the three the question names, two pay and one does not. Completeness-critic
misses run **one to ten** per pass across the fourteen — one to six over the eight the next sentence
scopes to — and every one is a real gap; round-1 review blockers one to four. The
**round-2 targeted read found zero in all eight passes that record it** — the second round paid for
nothing new, eight times running. The second opinion is worth keeping for a different reason than its
count: it reads the *other* repositories, and one of its passes changed the class of four rows. For lane
2's single review the answer is the round it already runs, `/vion-code-review branch`, which fires on
essentially every close-out task.

**4. Where corrections concentrated.** The seven clusters above. Which became gates, *inside the window
and without waiting for this retro*: `bom-lint`, `doc-comment-lint`, `sweep-residue-lint`,
`journal-lint`, `pragma-reason-lint` and `test-style-lint`. Which remain prose: the two largest, until
landings 1 and 2. **Which recur despite a gate** is the sharpest of the four — `spec-trace` is green
while a test cites a criterion whose text it does not prove, because the gate checks that an id exists
and never that the citation fits. Three journal
lines and one pass's round-1 blocker are that exact defect.

**5. Stalls.** Three, roughly nineteen hours, three unrelated mechanisms: a cross-session message held
because the *sender* sat in default mode, a watch reporting "no STOP yet" over a session idle after an API
connection loss, and a turn that hung for six hours and twenty minutes and surfaced only as "API Error".
Do the watch rules matter with no coordinator? **More, not less.** Two of the three were visible only to a
watcher, and the third writes no transcript record at all, so wall-clock is the only tell. But all three
mechanisms live in the `vion-dispatch` plugin in the architecture repo, so the rung is that repo's to pick.

**6. Cost.** From `T-011`'s harvest, forty-five dispatched sessions: fourteen passes (13,740 messages,
5,170 tool uses, 70.6 h), twelve fix-ups (5,752 · 2,198 · 9.3 h), seven coordinator sessions plus two
aborted (8,560 · 2,881), nine close-out workers (4,835 · 1,755 · 55.5 h), one infra session. Per area pass:
two Opus sessions at high effort plus three to five Opus check subagents, two and a half to three and a
half hours wall — `CLI`'s nine hours is six hours and twenty minutes of hang. **The harvest carries no
model column**, and the transcripts it was taken from are deleted, so "sessions × model" cannot be
reconstructed beyond each change doc's own *"2 Opus sessions at high effort"* line. That is the one place
this round's inputs fall short of the question.

**7. The skill's cadence.** Fourteen versions in six days, each from the previous pass's friction, and the
skill is now retired. Can codify plus retro keep that loop closed? **Not proven, and the honest answer is
"no evidence yet."** Two of the window's journal entries *are* skill-version changelogs — v13 and v14
written as friction lines — which one reader read as `→ codified:` stamps in the wrong shape. The
successor for the same-branch case is `/vion-codify`, and it has never run: zero stamps exist across 359
entries. A per-pass bump had a cadence no six-weekly retro can match, so the two loops are only equivalent
if the branch-local one actually fires. Retro-2's first question.

## What landed

| # | Cluster | Rung | File |
| --- | --- | --- | --- |
| 1 | A check that cannot fail | 3 — a named check | `.claude/commands/vion-code-review.md` § 6, new `P5` |
| 2 | An unverified premise in a brief | 4 — prose | [`../spec-process.md`](../spec-process.md) § Starting a lane-1 or lane-2 session |
| 3 | `D10` carrying three causes | 4 — prose | `.claude/commands/vion-code-review.md` § 5 |
| 4 | Markers that cannot count what happens | 4 — prose | [`../process-journal.md`](../process-journal.md) header, [`../process-metrics.md`](../process-metrics.md) |
| 5 | A pointer with no durable referent | 2 — a gate | `scripts/self-reference-lint.ps1`, `spec-gates.yml` |
| 6 | A machine trap repaid in briefs | 2 — a fix in the tool | `Vion.Dale.Cli`, `AC-CLI-003.10` |

Landing 2 is not the shape first proposed. The evidence said the lane-3 pre-dispatch brief check is
prescribed at Opus, costs seven to eleven and a half minutes rather than the four its own paragraph claimed, was refuted
by the pass it checked on two occasions, and mostly catches counts *another subagent had guessed into the
brief*. So the rule went to the writer instead: a brief carries intent, a number is in it only when the
session would act differently for a different value, and then it carries the command that produced it.
Lane 2 gained nothing — six of eight close-out task lines carried a wrong claim and the worker caught every
one unaided, which is the system working.

## What was consciously left

- **The session's own unchecked claims, 57 lines.** `D10` and `P1` already fire on this; the defect is
  `D10`'s overload, which landing 3 repairs. Re-read next round.
- **The partial sweep, 34 lines.** `P3` was minted three days before this round and has fired twice. A
  check gets a round before it is judged.
- **A gate passing over what it does not read, 19 lines.** The ladder already worked here without waiting
  for a retro: `bom-lint`, `doc-comment-lint`, `sweep-residue-lint`, `journal-lint`, `pragma-reason-lint`
  and `test-style-lint`
  were all minted inside the window from these lines.
- **The round-2 targeted read.** Question 3's answer is that it found **zero** in all eight passes that
  record it, against one to six completeness-critic misses and one to four round-1 blockers each. Removing
  it is the obvious economy and buys nothing today: lane 3 is dormant with the migration closed, and
  deleting a dormant step loses the record of why it existed.
- **Journal entry length.** The header says *"one line each"* and *"Qualitative one-liners only"*; the
  shortest entry in the window is 201 characters and the median is 529. The rule has never once been
  obeyed, including by the entries the maintainer wrote, and the length is what made this round readable.
  The header is what is wrong, not the entries — but correcting it belongs with a decision about what the
  journal is for, which is retro-2's, not a clause smuggled into landing 4.
- **The stall family, roughly nineteen hours over three mechanisms** — a message held because the *sender*
  sat in default mode (`:211`), a watch reporting *"no STOP yet"* over an idle session whose last text was
  an API error (`:241`), and a turn that hung for six hours and twenty minutes surfacing only as
  "API Error" (`:312`). Every mechanism lives in `vion-dispatch` in the architecture repo. It is that
  repo's rung to pick, and it is named here so it is not lost.

## Line citations into the rotated window

Thirteen citations of journal lines existed in `docs/`, in three archived pass docs and the close-out doc,
and all thirteen pointed into the window this round rotated. `T-015` had repaired them once, by content,
in #210. **The operator ruled to rewrite them rather than leave the archive heading's offset to carry the
resolution, and they are rewritten** — to `docs/retro/journal-2026-08-12-to-2026-09-10.md:<n>`, each one
verified by reading the entry it now lands on. A fourteenth, this note's own pointer at the `(escape)`
paragraph, was found by the same sweep and rewritten with them.

The ruling depended on `/vion-retro` § 9's offset formula being right first, and it was not: it assumed
the entries start at line 1 of a file the same section requires to open with a heading. Corrected before
the rewrite, because a rewrite computed from a wrong formula reproduces the defect `T-015` spent a round
repairing — its own first attempt failed exactly that way, by arithmetic.

**Two pointers were deliberately left.** This note's window line above (`process-journal.md:83–445`) and
the close-out doc's marker checkpoint (`:81` at `7662ec3`) are dated statements about where things stood
before the rotation, not pointers a reader follows — both name their frame, which is what makes them
readable afterwards. The archived entries' own internal citations are likewise untouched: the archive is
a verbatim copy, and editing it to fix its pointers would cost the property that makes it evidence.

## What retro-2 should be able to answer

1. **Has a `→ codified:` stamp ever been written?** Zero exist. `/vion-codify` has never run, and until it
   does, the round's strongest signal — a rule that exists and did not hold — has no input at all.
2. **Does `(escape)` fire now that it names the case that happens?** A zero next round means the redefinition
   failed, not that nothing escaped.
3. **Which of `D1`–`D10` fire**, carried forward from retro-0. This round answers it for one window: three
   numbers carry 72 of the 79 D-stamps and three never fire. Two more rounds decide whether `D4`, `D7` and `D8`
   are dead or merely out of season.
4. **Did `P5` catch anything?** It is the round's only rung-3 landing and it is aimed at the window's
   largest cluster.
5. **Did the writer-side brief rule shrink briefs?** The intent was fewer numbers, better sourced. A brief
   that grew a grep appendix is the rule read backwards.
6. **Is the tagging instrument or discipline?** If the next window runs entirely under `/vion-code-review`
   and tagging is still under twenty percent, the four characters are not worth what the header claims.
