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
  edge case. **A round means a reader who is not the author.** The author's own check of their own
  work is not a round, however rigorously it applied a named check, or the marker swallows nearly
  every correction and stops measuring the loop. Retro-1 read 359 entries, found at least twenty that
  fit and not one marker: the original wording named `/vion-code-review` alone, which was rarely the
  instrument, so the number the table exists to watch has never once been produced.

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
2026-09-10 · review · T-018 · Round 2 found the sentence round 1 had already forced a recount of still carrying an unrecounted number: "thirty-seven consecutive entries carry none" was the tail of one slice under the old parens-only notation, and under the corrected one the longest untagged run is 102 — the round's own strongest evidence understated by nearly three times, inside the clause that had just been fixed. Recounting a sentence is not recounting the sentence's every number. (D10, P1)
2026-09-10 · review · T-018 · The repair for a miscount was itself a miscount in the opposite direction: told that the metrics column had counted `(second ask)` only, I added "anchor the count at the end of the line" — but the journal's own header puts the D-number after the marker, so the anchored command returns 3 where the truth is 8. Both the original and its fix were written without running the command over the file. The column now carries the command itself rather than a description of it. (D10, P1)
2026-09-10 · review · T-018 · The gate's user-facing row in `spec-process.md` still described the 60-character bound and the `docs/rfcs/` exclusion that the same commit had deleted from the script — `check.ps1`'s meta-gate proves a row exists and never that it is true. A rung-3 landing's own documentation went stale inside the commit that landed it. (D2)
2026-09-10 · review · T-018 · Round 2 read the redefined `(escape)` marker as covering an author's own application of a named check, which would have made round 1's catch an escape. The wording allowed it. Rather than argue the instance, the header now says a round is a reader who is not the author — a marker whose count depends on who is reading its definition cannot be the number that should fall. (D9)
2026-09-10 · agent · T-018 amendment · `/vion-retro` § 9's archive-offset formula was wrong by the height of the heading the same section requires: it gives `L − (a−1)` on the assumption the entries start at line 1, and this round's archive would have been `L − 71` against a true `L − 66`. I had measured the offset instead of composing it, so the archive was right and the formula still shipped wrong — a rule can be broken by the session that follows it correctly, and only the next reader pays. Fixed to measure `f` out of the finished file, and to say that the frame commit is part of the offset. (D10)
2026-09-10 · agent · T-018 amendment · `/vion-retro` § 9's worked example dated its own evidence wrong: it describes the citation staleness `T-015` had already repaired by content in the same PR that wrote the sentence, so a reader measuring it today finds zero stale citations and concludes the command is lying about its own motivating case. Rewritten to say what the audit found and that #210 repaired it, which keeps the reason without asking the reader to reproduce a residue that no longer exists. (D2)
2026-09-10 · agent · T-018 amendment · `/vion-retro` § 2 told the round to run its reader subagents in the foreground; the Agent tool in this harness returns at launch and notifies on completion, so the instruction cannot be followed as written. Not a wrong instruction so much as one written against a different harness — replaced with what actually works, including the warning not to poll a reader's output file, which is the raw transcript and undoes the fresh context the slicing bought.
2026-09-10 · review · T-018 amendment · The operator ruled the thirteen journal citations should be rewritten into the archive rather than left to the heading's offset, and made the ruling conditional on fixing the offset formula first — a rewrite computed from a broken formula reproduces the exact defect `T-015` spent a round repairing. Both done, and each of the nine distinct targets re-read by content afterwards rather than trusted from the subtraction. (D10)
2026-09-10 · plugin · T-018 · An amendment answering my `partial` REPORT's two questions was filed at `amend-sdd-closeout-T-018-1.md` and I never read it — the operator answered question 1 in chat, I treated that as the whole answer, and continued. Question 2's ruling (rewrite the thirteen citations, after fixing the offset formula it depends on) went unapplied until the coordinator relayed it, by which point my own rotation had broken those citations: `:154` pointed past the end of a 110-line file and `:87` resolved to a header line reading "loses the evidence." A `partial` REPORT is a STOP, and the answer to a STOP arrives as an amendment on disk, not only as chat — a session that resumes on the chat answer alone resumes on half of it. (D10)
2026-09-10 · plugin · T-018 · Resuming, this checkout carried another session's uncommitted edits to five files — the citation rewrite already begun, and the three `/vion-retro` repairs. Verified every one by content before taking them over rather than reverting or re-doing them. This is `:422`'s hazard recurring three days later, the shared checkout between a coordinator and its worker, and the second time in this effort that work was found in a tree its author did not own.
