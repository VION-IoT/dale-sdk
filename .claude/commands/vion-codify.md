---
description: Turn this branch's corrections into rules in the files that own them while the reasoning is fresh — judge each journal line, apply in one pass, stamp the line, and let the operator rule on the PR
---

Writes a rule that is already clear now, with the example from the code at hand, before the
specifics are gone. It writes **prose only**, into the file that owns the rule: a rule just
discovered has not failed yet, and the stronger rungs of this repo's enforcement ladder — a DALE
diagnostic, a CI gate, a named `/vion-code-review` check — are priced for a *recurrence*, which is
[`/vion-retro`](vion-retro.md)'s judgment to make over a window, not this command's over one branch.
The one exception is § 2's mechanical case.

This command **does not stop for approval on its own steps**. It judges, applies and stamps in one
pass, and the operator rules on the diff at the PR
([`../../CLAUDE.md`](../../CLAUDE.md) working agreement 2). It stops only where a *decision* is
owed — § 2's contradiction case.

## 1. Collect

- Every line this branch added to [`docs/process-journal.md`](../../docs/process-journal.md):

  ```bash
  git diff $(git merge-base origin/main HEAD) -- docs/process-journal.md
  ```

  which covers the committed and the uncommitted lines alike. Take every `where`, not just
  `review`, and skip any line already carrying a `→ codified:` stamp.
- The corrections in this conversation that have no line yet — **write the line first**, in the
  journal's own format, then treat it like the others. Nothing gets codified without a journal
  line, because the line is the evidence and the rule is only the conclusion drawn from it.

Say the count before judging, and say how many of them are `review` lines. Working agreement 7
already obliges a `review` line in the commit that carries its fix, so a branch that corrected
something and journalled nothing is a gap to close here, not a clean sheet.

## 2. Judge each line

For each, **read the file that owns the rule** — do not decide from memory what it says — and pick
one of:

- **Codify** — the rule is clear, general, and absent from the file. Draft it in the file's own
  voice: the rule, its reason, and an example from the code just written or corrected. Match the
  section it belongs in; propose a new section only if none fits. Owners are
  [`CLAUDE.md`](../../CLAUDE.md)'s read-before-you-write table: the surface, testing, comment,
  devhost, simulator, identifier-stability and spec-process docs each own their subject, and
  `CLAUDE.md` itself owns only what no doc does.
- **Already covered** — the file states it. Quote the existing rule and name where, including its
  `D`- or `P`-number when the owner is [`vion-code-review.md`](vion-code-review.md) § 5 or § 6. The
  correction is then evidence that a rule did not hold, not a gap in the file. Do **not** restate or
  sharpen the existing rule to make it "stick" — a rule redrawn is a `(second ask)` for the retro to
  price, and rewriting it here destroys that signal.
- **Fix, not rule** — the correction is mechanical and the fix is cheaper than the sentence. A gate
  script that saw the wrong file set, a stale count in a SKILL.md, a lint whose vocabulary drifted
  from the header it mirrors: fix it, and say in the row that the fix is the landing. Precedent is
  the journal's own first entry under the marker, where `cleanup-code.ps1`'s untracked-file blindness
  was fixed rather than documented. This is the enforcement ladder read forwards: prose about a
  tooling bug is the worst rung available, not the safest.
- **Wait** — one occurrence of something that may be taste, situational, or contested. Say why. It
  stays an unstamped journal line and becomes the retro's input.

Where two lines point at one rule, one entry. **Where a line contradicts an existing rule, stop on
that one** and say which two sentences disagree — the operator decides which holds, and a command
that picks quietly has changed a convention without anyone ruling on it.

## 3. Apply, stamp, and report

In one pass, without pausing between rows:

- Write each **codify** rule into its owning file, in that file's existing structure and voice.
- Make each **fix, not rule** change.
- Append ` → codified: <path>` to each journal line the landing came from, naming the file the rule
  was written into (or the file the fix changed). Where the stamp goes and what it means are the
  journal header's, under *Format* — read it there. It decides which of your rows earn one, and
  **already covered** and **wait** rows do not.
- Run `pwsh -File scripts/journal-lint.ps1` before you finish. Stamps are appended to existing
  lines, and an append that lands on the wrong line is exactly what that gate catches.

Then hand back one table — **#**, **Line (date · where · topic)**, **Verdict**, **Owner file**,
**What landed** — one row per journal line, in the journal's order, with the reason on every
non-codify verdict. That table goes into the PR body beside the review round, and it is what the
operator rules on.
