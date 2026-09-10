# Process metrics

One observational row per retro round, appended by the round itself. This is the quantitative companion
to [`process-journal.md`](process-journal.md): the journal carries what was **felt** (one-line narratives,
written at the moment); this table carries what can be **counted afterwards from durable artifacts** —
git, `gh`, the journal itself. Never from transcripts (they age out) and never live during work.

**Nothing here is a target. The moment a number becomes one, it stops measuring.** (Anti-Goodhart clause,
inherited from the architecture repo, dale, dashboard and cloud-api.) The retro's felt impressions get
checked against this table — deciding what to change remains the retro's job.

No harvest script yet, deliberately — hand-fill a few rounds first; script the columns once they stop
changing.

**Why a schema before there is data.** A column here is a standing instruction to the journal: `second
asks` and `escapes` exist as columns *because* they made
[`process-journal.md`](process-journal.md) define the `(second ask)` and `(escape)` markers, and
without the markers neither number could ever be produced. A column with no defined input is not a
measurement, it is a wish — `gate catches` was dropped for exactly that reason and comes back when a
round can name the command that fills it.

## Columns and counting rules

- **round** — the retro note (dated file in `docs/retro/`).
- **window** — first..last day covered.
- **merged PRs** — `gh pr list --state merged` filtered to the window by `mergedAt`.
- **review lines** — new `review` journal entries in the window; **/PR** = review lines ÷ merged PRs.
  No pre-loop baseline exists: retro-0 mined 288 follow-up turns across 26 sessions, but those are
  turns, not corrections, and the two are not comparable. The first row after retro-0 sets the baseline.
  **The definition moved at retro-1**, from *the user corrected produced work* to *produced work was
  corrected, whoever found it*, so retro-1's own row undercounts against every row after it: two
  slices of its window ran with classification delegated, and their corrections were filed `agent`.
  Read the ratio forward from retro-2, not across that boundary.
- **second asks** — `review` lines carrying an `(nth ask)` marker: things that had to be said more
  than once. Count the lines and report the **highest `n`** beside the count, because the promotion
  argument is carried by the worst case and not by the total — retro-1 found `(third ask)`,
  `(fourth ask)` and `(fifth ask)` already written by hand and invisible to a column that read only
  `(second ask)`. Count the marker **in its position** — last on the line except for the D-number and
  any `→ codified:` stamp after it — and not merely mentioned in an entry's prose, or a line
  *about* the markers is counted as carrying one:

  ```bash
  grep -cE '^[0-9-]+ · review · .*\([a-z]+ ask\)( \([DP][0-9, DP]*\))?( → codified:.*)?$' <file>
  ```

  Anchoring the marker itself at end of line is the wrong repair and undercounts by more than half,
  because the header puts the D-number after it. Retro-1 made both mistakes in one round: its first
  tally matched `(second ask)` alone and missed the escalations, and its correction anchored too
  hard.
- **escapes** — `review` lines carrying the `(escape)` marker: corrections on work a review round had
  already passed, whichever round it was, including a later round of the same task's review. The
  loop-quality number: the analyzer/gate > check > prose ordering is working when this falls. Retro-1
  redefined it after reading 359 entries with ≥20 qualifying lines and zero markers; a `0` before
  that round means unrecorded, not none.
- **D-hits** — which taxonomy checks the journal lines actually name (`D1`…`D10`), as a tally. Retro-0's
  open question is precisely this: the taxonomy is mined from what the lead *said*, not from what the
  review command *catches*. A check that never fires in three rounds is a candidate for deletion; one
  that fires constantly is a candidate for promotion to a gate or an analyzer.
- **brief lines** — `brief` journal entries: how often a brief arrived wrong, ambiguous or incomplete.
  Its own column because this repo is briefed from the architecture repo's `/fix` and `/implement`
  lanes, and because retro-0 found those briefs had been skipping their own definition-of-done review
  step here for want of a `/vion-code-review` command.
- **consumer lines** — `consumer` journal entries: friction in the SDK-user feedback loop. This repo's
  distinguishing signal — most work here is triggered by a `DF-nn` entry from a consuming library, and a
  defect that reaches a consumer costs a release cycle, not a rebuild.
- **releases** — tags cut in the window. Each one obliges an example/template/library reference bump
  ([`releasing.md`](releasing.md)); a release without its bump is a `release` journal line.
- **journal other · acted** — non-review journal lines added, and total lines the round marked acted-on.
- **notes** — anything a number can't say.

| round | window | merged PRs | review lines (/PR) | second asks | escapes | D-hits | brief lines | consumer lines | releases | journal other · acted | notes |
| ----- | ------ | ---------- | ------------------ | ----------- | ------- | ------ | ----------- | -------------- | -------- | --------------------- | ----- |
| [retro-0](retro/2026-08-12-review-mining-round.md) | 2026-06-12..2026-08-12 | 70 | n/a — 288 follow-up turns / 26 sessions (not comparable) | n/a | n/a | n/a | n/a | n/a | 31 (18 stable, 13 preview) | 4 · 0 | Baseline round: mined transcripts because no journal existed. Every "n/a" is a column the journal will fill from here — none is a zero. One release every other day is this repo's tempo; the example-bump obligation rides on all 31. |
| [retro-1](retro/2026-09-10-sdd-migration-retro.md) | 2026-08-12..2026-09-10 | 85 | 128 (1.5) | 8 · highest `n` = 5 | 0 — unrecorded, see notes | D10 36 · D2 20 · D9 16 · D1 2 · D3 2 · D5 2 · D6 1 · **D4/D7/D8 0** · P1 2 · P3 2 · P2/P4 0 | 45 | 1 | 11 (11 stable, 0 preview) | 231 · 6 | The SDD migration end to end: 14 area passes, the unification pass, the ledger review, the close-out. 70 of 359 entries carry any taxonomy tag, and 60 of those sit in the two slices where a review command was the instrument. `escapes` is 0 because the marker had never been written, not because nothing escaped — ≥20 lines qualify; the definition was widened this round. No `→ codified:` stamp exists anywhere in the file. `acted` is the six landings, not the lines they were drawn from. |
