# Process journal

Where the process creaked, one line per event. Not a work log: what shipped is in git.

## Format

```
YYYY-MM-DD · <where> · <topic or —> · <what happened, one line> [ (second ask)] [ (self)] [ → codified: <path>]
```

- `where` is one of five: `review` a correction to produced work · `gate` tooling fought or false-passed · `brief` upstream was wrong · `decision` a settled point, with its reason · `manual` a human grumble.
- `topic` names what it is about — a component, a skill, a document slug, an issue key — or `—`.
- `(second ask)`: the same thing was asked for twice. `(self)`: the agent found it, not a human.
- `→ codified: <path>`: the file a rule or fix for this entry landed in.
- An entry says what was produced, what was wrong, and what was asked instead. No reasoning, no fix, no quote. At most 400 characters.
- A physical line that does not start with a date continues the entry above. Blank lines between entries are allowed.
- Written by the agent in the commit that carries the fix, for every `where` but `manual`. A fixed review finding counts, and its entry names the finding and the file that should have prevented it.
- Newest last, below the retro marker. Entries above the marker have been read by a retro.

## Entries

<!-- retro-1 marker -->

2026-09-13 · brief · process-unification · The brief put the live window at 59 entries; the file held 54 below the retro-1 marker, by the old journal-lint and by a count of dated lines alike, so the archive carries 54.

2026-09-13 · brief · process-unification · The brief repointed every citation of the review command's § 7 at docs/review-checks.md, but § 7 was the pull request body's record of a review round, which the checks file does not hold; those citations now point at the PR template's Verification line.

2026-09-13 · gate · check.ps1 · /check derived its gate list from ./scripts/*.ps1 steps only, so a spec-gates.yml job using a shared-workflows action would have been invisible to it; shared actions are now derived too and a CI-only one prints SKIP. (self) → codified: scripts/check.ps1

2026-09-13 · review · CLAUDE.md · The review found the rotated window unnamed for the first retro, codify triggered from a third place in the trigger table, and a heading with no blank line above it; the plugin harness skill should have caught the second. (self)
