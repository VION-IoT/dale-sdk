# Process journal

Friction log for how this repo gets built — review corrections and process creaks, one line each,
written **the moment they happen**. Not a work log: what shipped is in git and the PRs. This file
records where the *process* creaked. Modeled on the architecture repo's `docs/workflow-journal.md` and
the equivalents in dale, dashboard and cloud-api.

Why: corrections and friction are felt in the moment and remembered nowhere. Without this file they
surface only when someone has accumulated enough irritation to raise them all at once — by which point
the specifics are gone and only the annoyance is left. One line, written when it happens, keeps the
specifics.

**Transcripts are not a fallback.** They age out of `~/.claude/projects/` after weeks;
[retro-0](retro/2026-08-12-review-mining-round.md) mined them once and that window is closed — the
oldest session it could still read started 2026-06-12, so everything before the Modbus-TCP-server work
was already gone. Assistant memory is not a fallback either: it is machine-local, so a colleague, a CI
runner or a second workstation never sees it, and it goes stale silently (retro-0 found two of this
repo's memory entries already false).

## Format

```
YYYY-MM-DD · <where> · <topic, PR #, or —> · <what happened, one line>
```

`where` is one of:

- `review` — **produced work was corrected** (**the most important line type**). Whoever found it: the
  user in-session, a `/vion-code-review` round, a fresh-context critic, a coordinator's read. The test
  is whether an artifact that had been produced turned out wrong, not who said so — retro-1 found two
  slices where the operator had delegated classification and said almost nothing, so corrections
  landed under `agent` and the metrics table's headline count read as a quiet fortnight
- `brief` — a brief (from the architecture repo or a prior session) was wrong, ambiguous, or missing
  something; also where a `Friction:` field would have gone if the work had not been done locally
- `gate` — the CI style gate, build, test gate or snapshot bot false-fails, false-passes, or fights the work
- `consumer` — friction in the SDK-user feedback loop (a `DF-nn` entry, an adoption blocker, a
  workaround a consumer had to keep)
- `release` — the release / example-bump / upload lane creaked
- `infra` — CI runner, package feeds, credentials
- `agent` — agent behavior or process friction with **no corrected artifact**: a habit, a wasted
  round, a way of working that creaked. Once something produced turned out wrong, the line is `review`
- `plugin` — the dispatch mechanics themselves creaked: a hook that blocked wrongly, a report not
  filed, a launch refused wrongly, a stale fetch
- `manual` — human grumble

Append at the bottom, newest last — `scripts/journal-lint.ps1` (in `spec-gates.yml`) fails CI on a
line below `## Entries` that is not one entry of this shape, on two entries sharing a line, and on
an entry dated below the one above it. Naming the taxonomy check a correction maps to (`D1`…`D10`, see
[`.claude/commands/vion-code-review.md`](../.claude/commands/vion-code-review.md) § 5) is worth the four
characters — retro-1's open question is which of them actually fire.

Two markers earn their keystrokes because [`process-metrics.md`](process-metrics.md) counts them and
nothing else can produce them:

- **`(second ask)`** on a `review` line the user has now had to make **twice** — and `(third ask)`,
  `(fourth ask)` and so on, counting up, when it is said again. Each one is a standing candidate for
  promotion to an analyzer, a gate, or a convention rule, and the count *is* the promotion argument:
  retro-1 found one correction asked five times, escalating markers already written by hand, and a
  metrics column that could see only the first repeat.
- **`(escape)`** on a `review` line for a correction to work a review round had **already passed**.
  This is the loop-quality signal; it is the number that should fall. It counts wherever a round ran
  and missed: `/vion-code-review`, a fresh-context critic, a coordinator's read — and **a later round
  of the same task's own review catching what the earlier round left** is the ordinary case, not an
  edge case. Retro-1 read 359 entries, found at least twenty that fit and not one marker: the
  original wording named `/vion-code-review` alone, which was rarely the instrument, so the number
  the table exists to watch has never once been produced.

Both go at the end of the line, before the D-number.

A third mark is written **afterwards, by whoever writes the rule**, not by the line's author:

- **`→ codified: <path>`** names the file a rule was written into because of this line. It goes at
  the very end, after both markers and after the D-number, and
  [`/vion-codify`](../.claude/commands/vion-codify.md) is what normally appends it — though any
  session that writes the rule appends it, whichever command or conversation did the writing.

The stamp goes on **only when a sentence now exists that prevents the recurrence.** Correcting or
deleting the text that went wrong leaves the file right without teaching anything, and a line for
such a fix stays unstamped evidence. A line that merely turned out to be already covered by an
existing rule stays unstamped too — stamping it would tell the next retro a gap was closed that
never was, and an unstamped line that recurs is precisely the signal
[`/vion-retro`](../.claude/commands/vion-retro.md) reads hardest.

**Record what happened, not what should change** — the fix is the retro's job, and pre-judging it here
loses the evidence.

**Qualitative one-liners only.** Anything countable afterwards from durable artifacts — PRs merged,
corrections per PR, gate catches, durations — is **not** journalled; the retro counts it into
[`process-metrics.md`](process-metrics.md) from git, `gh` and this file. Journal what was *felt*, when
it was felt.

## Entries

<!-- retro-1 marker: everything below this line is unread by a retro. Retro-1: docs/retro/2026-09-10-sdd-migration-retro.md · window archived in docs/retro/journal-2026-08-12-to-2026-09-10.md. Move the marker when a round reads it. -->

2026-09-10 · agent · T-018 · The rotation was built with the entry range the brief and my own § 1 read had printed (83–445), after landing 4 had added thirteen lines to the journal header in an earlier commit of the same branch — so the archive's first pass captured the header tail and the old marker instead of the first entries. Caught by reading the file, not by any gate: journal-lint sees the live file and never the archive, and a verbatim-copy claim is exactly the kind a `diff` confirms and a reader does not. The recount is cheap and the frame is the part that goes stale — a line number is only meaningful with the commit it was read at, which is why the archive heading names `7662ec3` rather than just an offset. (D10, P1)
2026-09-10 · brief · T-018 · Two of the brief's premises were wrong in the same direction, both re-derivable in one command: the retro-0 marker is at `:81`, not `:67` as the brief cites, and the archived change docs are seventeen carrying fourteen scorecards, not "the sixteen archived change docs' scorecards". Neither re-scoped the task. Both are the shape the counts-are-hypotheses rule exists for, and the second is a finding in its own right — the two unification docs and the founding process doc have no scorecard, so pass 0's cost exists nowhere but the journal. (D10)
2026-09-10 · gate · T-018 · `check.ps1` refused the new gate before it could run wrong: adding a step to `spec-gates.yml` with no entry in the invocation table failed the local suite with the reason, which is `T-012`'s self-test design catching its first real case. The meta-gate in `run-script-tests.ps1` did the same for the missing self-test. Two gates about gates, both earning their keep on one change.
2026-09-10 · agent · T-018 · Four mutation runs against the new lint reported PASS while their `sed` had silently not applied — a quoting failure through three shell layers reads exactly like a surviving mutant, and a mutation that did not apply is worse than none because it certifies the opposite. Fixed by making every mutation print DID NOT APPLY when the replacement is a no-op, and by restoring from a byte copy with a hash check rather than re-deriving the file. (P1)
2026-09-10 · review · T-018 · The stale-tool caution was written where no test could reach it: a local build reports `0.0.0-local`, which the rule deliberately ignores, so the wiring could have been deleted and every test stayed green. Adding the tool version as a fourth `CliComposition` seam is what made the wiring provable, and the mutation that removes the call now reddens. This is `P5`, the check this same round minted, firing on the round's own code before the round ended. (P5, D10)
2026-09-10 · review · T-018 · Round 1 found the round's own counts wrong in four places, which is the one thing a retro cannot be wrong about: the D-hits tally missed a third stamp notation (bare `D10` with no parentheses and no period) and undercounted D10 by three and tagged lines by three; the metrics cell counted `(second ask)` only, on the very row written to demonstrate that the column now counts every `(nth ask)`; the brief-check timing said "seven to ten across four passes" when five passes timed it and the ceiling is 11.5; and the note's slice-concentration figures reproduced from nothing. Every one was a number I had recounted once and then re-read rather than recounted again after the archive moved. (P1, D10)
2026-09-10 · review · T-018 · The new gate's 60-character bound survived being doubled to 130 with all eleven self-test cases green — the constant was asserted by a comment claiming it was pinned, and pinned by nothing. Replaced with whole-cell matching, which needs no constant, kills a false positive the reviewer found on a legitimate definition cell, and is fully mutable-testable. `P5` catching the branch that minted `P5`, sixty lines from where it was written. (P5)
2026-09-10 · review · T-018 · A Drift checkpoint cited `spec-process.md:287` for a line this same branch had already moved to `:299` — the exact defect the branch landed a gate for, committed one file away, two entries after a journal line saying a line number means nothing without the commit it was read at. Rule, gate and journal line all present; the sentence written under them still did it. (D10, P1)
2026-09-10 · review · T-018 · Two of the caution's three call sites were unreddenable: only the explicit `--project` path had a test, so deleting the call from the walk-up or the solution auto-select path left 407 of 407 green. The seam that made one path provable was mistaken for making the behaviour provable. Now a three-row `[DataRow]` over the resolution paths, each proved red by deleting its own call. (P5, P3)
