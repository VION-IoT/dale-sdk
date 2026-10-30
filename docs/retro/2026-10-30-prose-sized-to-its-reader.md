# Retro-3 — prose sized to its reader

**Date:** 2026-10-30 · **Window:** 2026-09-15 → 2026-10-29, 76 entries in 18 fragments (review 42, gate 19, brief 13, decision 2), archived in [`journal-2026-09-15-to-2026-10-29.md`](journal-2026-09-15-to-2026-10-29.md) · **Extra paths:** none · **Bodies:** 33 pull requests merged after 2026-09-15T17:23:04+02:00 (#242–#276) · **Recounts:** every count reproduced.

One fresh-context reader over the window, the bodies and the three previous records. Cluster membership
is the reader's classification. A count includes a journal entry and a body that record the same event
once each; distinct events are given where they differ.

## The clusters

`AR:n` is a line of the archive above.

| Cluster | Count | Pointers | Outcome |
| --- | ---: | --- | --- |
| A brief, amendment or issue premise wrong | 15 | `AR` 50, 72, 82, 110, 112, 114, 120, 144, 148; #246, #250, #256, #257, #262, #276 | left; the brief rule kept |
| A test that cannot fail, a clause no test reaches | 12 | `AR` 5, 9, 41, 43, 45, 70, 87, 95; #247, #260, #267, #272 | CHK-15 kept |
| A claim not checked against what it describes | 11 | `AR` 17, 21, 35, 46, 54, 58, 62, 66, 80, 88, 97 | CHK-10 kept |
| A change leaving text it made false | 8 | `AR` 3, 25, 64, 89, 99, 101, 108; #255 | CHK-13 kept |
| A brief asking for a log-level test (3 events) | 6 | `AR` 74, 84, 91; #248, #251, #271 | forwarded |
| A change doc's design corrected by an operator amendment | 5 | `AR` 27, 29, 37; #242, #258 | left |
| git refusing a freshly minted App token | 5 | `AR` 132, 136, 138; #268, #269 | left; fixed by #269 |
| `modbus-smoke -LocalSource` described wrongly or failing | 5 | `AR` 86, 142, 146; #253, #274 | left; fixed by #274 |
| Prose too long or carrying history (3 events, 2 by the operator) | 5 | `AR` 105, 106, 126; #264, #273 | landed CHK-16 |
| Application Control blocking build or test artefacts | 4 | `AR` 48, 52, 56, 76 | left; codified the same day |
| Machine and tool traps | 4 | `AR` 39, 60, 116, 118 | left; four tools |
| The analyzer-wiring output guard racing builds | 3 | `AR` 7, 11, 13 | left; codified the same day |
| A test name with a filler word, a hand-typed REPORT time, the pull-request branch-prefix precondition | 2 each | `AR` 15, 33, 130, 134; #261, #270 | left |

## Check citations in the bodies

`grep -lw <id> bodies/*.md`, the first round with per-check body counts.

| Check | Citing bodies |
| --- | --- |
| CHK-01, CHK-03, CHK-05, CHK-08, CHK-11, CHK-12 | none |
| CHK-02 | #258, #259, #260, #261, #272, #273, #276 |
| CHK-04 | #271 |
| CHK-06 | #261 |
| CHK-07 | #260 |
| CHK-09 | #272, #273 |
| CHK-10 | #273 |
| CHK-13 | #257, #258, #260, #264 |
| CHK-14 | #267, #270 |
| CHK-15 | #260, #261, #267, #276 |

## What landed

- **CHK-16 — prose sized to its reader** · rung 2 · [`docs/review-checks.md`](../review-checks.md). The
  comment rules in `comment-conventions.md` were already prose; exception and assertion messages had no
  owner. A widened CHK-02 was rejected: one check carrying several causes is what the D10 split undid.
- **The log-level brief clash** · forwarded · the REPORT's Friction, for the architecture repo, whose
  commands write the briefs. It recurred after its `→ codified:` stamp; every implementer recorded the
  level as a `GAP` criterion with an observed run.

## Pruned

- **CHK-12, the reverse question** — nothing supported it: no body, no journal line, zero as `P2` in
  the 2026-09-10 record, no count in 2026-09-15's. Its slot holds the 15-check budget for CHK-16.
- **Kept, though their clusters recurred:** CHK-15's red-source clause, CHK-13's text clause, CHK-10's
  split from D10, CHK-02, and `spec-process.md`'s brief-count rule. Each recurrence that cites one was
  found by it, by the agent, before merge.
- No alias, and no check firing constantly.

## Left, and why

- **Wrong brief premises**, the largest summed cluster (34 over two rounds): every one was caught by the
  implementer before code. The briefs come from the architecture repo, whose `brief-reader` agent was
  added on 2026-09-17; five entries postdate it.
- **Token, `-LocalSource`**: fixed in tooling inside the window, with no recurrence after.
- **Singletons**, one each: `AR` 19, 23, 31, 68, 78, 93, 103, 122, 124, 128, 140. `AR` 140, the
  devhost-smoke `SKILL.md` over its 6 kB budget, is the next retro's to cut, or a fix-sized branch's.

## For the next round

1. Is CHK-16 cited, and does the operator still shorten comments or messages?
2. Did a brief ask for a log-level test after the forward?
3. CHK-01, CHK-03, CHK-05, CHK-08 and CHK-11 had no citing body: a second zero round counts toward their prune.
4. Did the architecture repo's `brief-reader` run on the briefs whose premises were wrong?
