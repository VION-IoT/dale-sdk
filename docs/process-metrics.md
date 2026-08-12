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
- **second asks** — `review` lines carrying the `(second ask)` marker: things the human had to say
  twice. Each is a standing candidate for absorption into an analyzer, a gate, or a convention rule.
- **escapes** — `review` lines carrying the `(escape)` marker: corrections on work that had already
  passed `/vion-code-review`. The loop-quality number: the analyzer/gate > check > prose ordering is
  working when this falls.
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
