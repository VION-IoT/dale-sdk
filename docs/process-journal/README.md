# Process journal

Where the process creaked, one line per event. Not a work log: what shipped is in git.

retro at: 40

## Format

```
YYYY-MM-DD · <where> · <topic or —> · <what happened, one line> [ (second ask)] [ (self)]
```

- `where` is one of five: `review` a human's correction to produced work · `gate` tooling fought or false-passed · `brief` upstream was wrong · `decision` a settled point, with its reason · `manual` a human grumble.
- `topic` names what it is about — a component, a skill, a document slug, an issue key — or `—`.
- `(second ask)`: the same thing was asked for twice. `(self)`: the agent found it, not a human.
- An entry says what was produced, what was wrong, and what was asked instead. No reasoning, no fix, no quote. At most 400 characters.
- A physical line that starts with a date-shaped token (digits, dash, digits, dash, digits) is an entry line whether or not the date is well-formed; only a line starting with anything else continues the entry above. Blank lines and one-line HTML comments between entries are allowed.
- Written by the agent in the commit that carries the fix, for every `where` but `manual`. A finding an agent found and fixed is not an entry; a mechanical correction is fixed in that same commit.

## Layout

- This folder holds this `README.md` and one fragment per branch, `YYYY-MM-DD-<branch>.md`: entries only, no heading, no subfolders.
- `<branch>` is the branch name lowercased in the invariant culture, every character outside `[a-z0-9]` replaced by `-`, runs of `-` collapsed, cut to 66 characters, then leading and trailing `-` removed. A name that comes out empty has no fragment: stop and say so.
- Dates never decrease within a fragment, and no entry is dated earlier than its file name's date.
- The live window is every fragment, ordered by file name, then line: `(export LC_ALL=C; grep -Hn '' docs/process-journal/2*.md)`, the locale set before the glob expands. No fragment: the glob does not match, grep exits 2, and the window is empty.
- `retro at:` is the live window's entry count past which a retro is due. A `decision` entry reading `retro deferred to <date>: <reason>` silences the whole due check, the count and a record's age alike, until the date passes; the newest such entry holds. No `retro at:` line: no count.
- `README.md` is required and never removed; rotated windows live in `docs/retro/`. A branch's own lines go in its fragment, never into `docs/retro/`, whatever their vintage: an entry past the character limit is split into consecutive entries with the same date, `where` and topic, each marked `(part n/m)`.

## The branch's fragment

1. The default branch is `git symbolic-ref --short refs/remotes/origin/HEAD` without `origin/`. No branch, or the default branch, checked out: stop, and say to cut the branch first where `CLAUDE.md` says.
2. Candidates: `git diff --name-only --diff-filter=AM $(git merge-base origin/<default> HEAD) -- docs/process-journal/` plus `git status --porcelain --untracked-files=all -- docs/process-journal/`.
3. The fragment is the candidate named `YYYY-MM-DD-<branch>.md` for the current branch, exactly. None: `<today>-<branch>.md`, appended to when it exists at the merge base, created otherwise.
4. The entry goes at the fragment's end.
