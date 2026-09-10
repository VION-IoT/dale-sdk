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
| 33 | 20 | 16 | 2 | 2 | 2 | 1 | **0** | **0** | **0** |

`P1` 2, `P3` 2, `P2` and `P4` zero — and `P1`–`P4` existed for only the last 84 entries. Sixty-seven lines
of 359 carry any tag at all.

Three readings, each load-bearing:

- **`D10` is a third of all tagging and carries three causes** — the unchecked claim, the incomplete sweep,
  and the wrong premise. Slice D's own retro-mapping puts five of its `D10`-stamped lines under `P3`, which
  had owned that shape since the day it was minted.
- **`D4`, `D7` and `D8` never fired.** `process-metrics.md` makes deletion a three-round judgment, so this
  is round one of three, not a verdict. `D4` is the analyzer-obligation check, and its silence is the
  reason nothing in this round reaches the ladder's top rung: the window was process, spec and harness
  work, with almost no consumer-facing authoring for an analyzer to see.
- **Tagging tracks the instrument, not the discipline.** Fifty-three of the sixty-seven tagged lines sit in
  the two slices where a review command was the reviewing instrument; the three middle slices, where the
  passes ran coordinator critics instead, carry seven tags across 196 entries, and thirty-seven consecutive
  entries carry none. All four scorecards in one slice record the classification gate as *"0 — delegated"*.
  The taxonomy is calibrated to a review mode half the window did not use.

## The two silent markers

**`(escape)`: zero in 359 entries, and the thing it counts happens.** At least twenty lines fit, named
independently by all five readers. Three structural reasons, one per era: the markers were defined in
`process-metrics.md` and never in the journal header that produces them (`:88`); the passes ran critics
rather than `/vion-code-review`, so the marker's wording named an instrument that was rarely used; and the
header never decided whether a second review round catching the first counts, which is now the ordinary
case. Retired would have been the wrong call — the loop the number watches only started existing on
2026-09-09. Redefined instead, landing 4.

**`→ codified:`: zero in 359 entries, and for all but the last day the stamp did not exist.** A true
negative for the stamp, a false negative for codification: `:301` is quoted verbatim inside `P1`; `:187`
proposed the doc-comment check and `doc-comment-lint` shipped in-window; five gates in one scorecard's list
were minted from in-window lines. None was stamped. **Not retro-stamped here, deliberately** — the stamp
exists so a later round can see *rule written, recurred anyway*, and a rotated window is invisible to the
next round, so stamping the archive buys nothing. That is a real seam between the two loops: `/vion-codify`
stamps on the branch, `/vion-retro` rotates the evidence away. Retro-2's first question is whether a stamp
has appeared at all.

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
prescribed at Opus, costs seven to ten minutes rather than the four its own paragraph claimed, was refuted
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
  for a retro: `bom-lint`, `doc-comment-lint`, `sweep-residue-lint`, `journal-lint` and `test-style-lint`
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

Thirteen citations of journal lines exist in `docs/`, in three archived pass docs and the close-out doc.
All thirteen resolve correctly by content today — `T-015` repaired them in #210 — and **all thirteen point
into the window this round rotated.** They are reported rather than rewritten: that call is the operator's.
The archive's heading carries the offset, and the offset is stated as a line range rather than computed,
because `/vion-retro` § 9's formula omits the heading it also requires.

## What retro-2 should be able to answer

1. **Has a `→ codified:` stamp ever been written?** Zero exist. `/vion-codify` has never run, and until it
   does, the round's strongest signal — a rule that exists and did not hold — has no input at all.
2. **Does `(escape)` fire now that it names the case that happens?** A zero next round means the redefinition
   failed, not that nothing escaped.
3. **Which of `D1`–`D10` fire**, carried forward from retro-0. This round answers it for one window: three
   numbers carry 69 of 73 stamps and three never fire. Two more rounds decide whether `D4`, `D7` and `D8`
   are dead or merely out of season.
4. **Did `P5` catch anything?** It is the round's only rung-3 landing and it is aimed at the window's
   largest cluster.
5. **Did the writer-side brief rule shrink briefs?** The intent was fewer numbers, better sourced. A brief
   that grew a grep appendix is the rule read backwards.
6. **Is the tagging instrument or discipline?** If the next window runs entirely under `/vion-code-review`
   and tagging is still under twenty percent, the four characters are not worth what the header claims.
