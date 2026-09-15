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

<!-- retro-2 marker -->

2026-09-15 · review · retro-2 record · The branch review found the record claiming the extra path partitioned while seven of its entries sat in no cluster, and its counts carrying no command, one file tally unscoped; CLAUDE.md's count-with-its-command rule and review-checks.md P1 should have prevented it. (self)

2026-09-15 · review · review-checks.md P3 · The branch review found the new P3 clause on docs, skills and CLAUDE.md lines pointing at comment-conventions.md, which covers inline comments only; the harness skill's one-owner rule should have prevented it. (self)
