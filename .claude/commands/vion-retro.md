---
description: Read the process journal since the last retro marker, cluster corrections by count, propose where each recurrence lands on the enforcement ladder, then land it, record the round, and rotate the window
argument-hint: '[YYYY-MM-DD — start date, overriding the marker]'
---

Turns recurrences in [`docs/process-journal.md`](../../docs/process-journal.md) into rules at the
weakest rung that has not already failed. It **stops once**, at the landing set — that is the
operator's decision and nobody else's. It does not stop to confirm the read, the record or the
rotation: those are process steps, and the operator rules on the diff at the PR
([`../../CLAUDE.md`](../../CLAUDE.md) working agreement 2). The landings are ordinary work on a task
branch and follow the working agreement like any other change — committed as they go, gated, reviewed
in-session, opened as a PR.

## 1. Scope

The window is everything below the retro marker in the journal — the HTML comment reading
`retro-N marker: everything below this line is unread by a retro`. `$1` overrides it with a start
date. Find it and count before reading anything else:

```bash
grep -n "retro-.* marker" docs/process-journal.md            # gives the marker's line, M
awk -v m=M 'NR>m && /^[0-9]{4}-[0-9]{2}-[0-9]{2} · /' docs/process-journal.md | wc -l
awk -v m=M 'NR>m && /^[0-9]{4}-[0-9]{2}-[0-9]{2} · /' docs/process-journal.md \
  | sed 's/^[0-9-]* · \([a-z]*\) · .*/\1/' | sort | uniq -c | sort -rn
```

Substitute the number the first command printed for `M`. An unbound `awk` variable is `0`, so
leaving it as written counts the whole file and reports a window larger than the one you are about
to read — invisible today, because every entry sits below the marker, which is how it would ship
wrong.

Say the total and the per-`where` breakdown out loud before § 2. Two guards, in opposite directions:

- **Under ~20 entries the window is thin** and mostly yields one-offs. Warn, and ask whether to
  proceed — the interval is the operator's; the warning is the guard.
- **Over ~120 entries a single reader cannot hold the window**, and § 2 slices. Retro-0 left over 350
  entries below its marker, so this is the case the first real round meets, not a hypothetical.

Retro-0 did **not** rotate: it placed the marker and left the window in the live file. Every round
after it rotates (§ 9), so the marker and the archive move together from here.

On a branch other than `origin/main`'s tip, say which.

## 2. Read with a fresh context

Dispatch **reader subagents** (Agent tool, `general-purpose`, passing `model` explicitly as the model
this session runs on). **Never read the window inline**: this session or its predecessors wrote many
of the entries, and recency bias sets the themes otherwise.

The Agent tool returns as soon as each reader is launched and notifies on completion, so the slices
run concurrently whether or not you want them to — dispatch all of them in one message and wait. Do
not poll a reader's output file: it is the raw transcript, and reading it undoes the fresh context
the slicing bought. Retro-1 ran five slices this way in under five minutes of wall time, the
slowest reader setting the pace.

Every reader gets the journal header (everything above `## Entries`, which defines the `where`
vocabulary and the `(second ask)` / `(escape)` markers), its slice of the window **with line
numbers**, and the previous round's note under [`docs/retro/`](../../docs/retro/). Where the window
covers change-doc work, the archived docs under `docs/changes/archive/` carry scorecards and *Drift
checkpoints* that say what a pass cost; name the ones in the window as a second input.

- **One slice** when the window is under ~120 entries: one reader, the whole window.
- **Dated slices of ~100 entries** above that, one reader each, in parallel — then **one merge
  reader** that receives only the slice reports and never the journal. The merge pass is not
  optional: a theme drawn four times across four slices is a singleton to each reader and the
  window's largest cluster to nobody, and re-clustering is the only step that can see it.

Each reader returns:

- themes clustered **by count**, each with its line numbers and one verbatim quote;
- what **recurred despite the previous round's landings**, read from that note (retro-0 landed the
  substrate itself — the journal, the metrics table, `/vion-code-review`'s D-numbers — so for
  retro-1 this item is "did the taxonomy hold", not "did a rule hold");
- **which `D`- and `P`-numbers the lines actually name**, as a tally. Retro-0's standing open
  question is exactly this, and it is the one input `process-metrics.md`'s `D-hits` column has;
- lines carrying `(second ask)` and lines carrying `(escape)`, listed separately — they are the two
  markers the metrics table cannot produce any other way;
- singletons with a structural cause;
- positive signals;
- journal hygiene — format drift against the header, pre-judged fixes, duplicates, entries above the
  marker.

A reader proposes no fixes and reads no convention doc or command. It reports; the deciding is § 4's.

## 3. Verify the read

Spot-check the load-bearing claims against the file before acting: the line numbers, the top three
cluster counts, every hygiene finding, the `(second ask)` and `(escape)` lists, and that no entry
sits above the marker. Run `pwsh -File scripts/journal-lint.ps1` — hygiene has a gate here, and a
finding the gate disagrees with is a finding to re-read.

A retro that lands changes on a misread costs more than one that lands nothing. This is
[`vion-code-review.md`](vion-code-review.md) § 6's **P1** applied to the round's own numbers: every
count you carry into § 5's table is recounted here, one command each, not read off a report.

## 4. Decide

Take the largest still-open clusters and every recurrence despite the previous round's landings. For
each, pick **the weakest layer that has not already failed**. This repo's enforcement ladder, from
strongest to weakest ([`CLAUDE.md`](../../CLAUDE.md) § How this file stays true):

1. **A DALE analyzer diagnostic** — for an authoring mistake a compiler can see. The cheapest rung
   in the repo despite being the strongest, because it fires in the *consumer's* build and not only
   in ours, and no rung mesh or the sibling repos have reaches that far. It costs what
   [`docs/sdk-surface-conventions.md`](../../docs/sdk-surface-conventions.md) §§ 4–5 say an analyzer
   costs — an id allocated in `DaleDiagnostics.cs`, tests of the composed behaviour rather than the
   rule alone, and proof that it fires in a real build, where Metalama replaces the compiler task and
   generated contract interfaces are invisible — so it is a landing with a real bill, not a free
   promotion.
2. **A CI gate or a script** — for a mechanical correction that no diagnostic can see because it is
   about this repository rather than about a consumer's code: a doc shape, a count, a file set. It
   goes into `.github/workflows/spec-gates.yml`, and it carries a `scripts/<name>.tests.ps1`
   self-test because `scripts/run-script-tests.ps1` fails on a gate script that is neither tested
   nor exempt — so the self-test is a gate obligation here, not a nicety.
3. **A named check in [`vion-code-review.md`](vion-code-review.md) § 6** — a new **`P`-number**, in
   that section's format, for a rule that already exists in prose in whichever file owns it and is
   still being drawn. Prose has failed; the round must catch it. **Never a new `D`-number**: § 5 is
   mined from what the lead actually said in a dated window and does not grow by invention.
4. **Prose in the file that owns the rule** — for a rule that does not exist yet. Name the file; if
   none fits, say so and propose one. Written as the rule, its reason, and an example from real code.

Two shortcuts that skip rungs, both earned by evidence rather than by count:

- A cluster whose lines carry a **`→ codified:` stamp and still recurred** is the strongest signal in
  the window: the rule exists and did not hold. Rung 3 at the least, rung 1 or 2 where the mistake is
  mechanical.
- A cluster whose lines carry **`(escape)`** is rung 3 having already failed — the review ran and
  missed it. Go to rung 1 or 2, or say why the check can be repaired rather than replaced.

Or consciously leave it, with the reason written down so the next round sees a choice and not an
oversight. **Bound the set to three to six landings**; more means the previous round was skipped, and
a round that lands ten rules has written nine nobody will read.

## 5. Propose and stop

Present one table — **#**, **Cluster**, **Count**, **Finding**, **Rung**, **Lands in** — followed by
the leaf for each and its reason. Then **stop**. The operator approves, moves a landing to another
rung, or drops it, by number. This is the round's only stop, and it is a decision, not a checkpoint.

## 6. Land

Apply the approved landings. Where a landing is a rule, **write the evidence into the rule** — "drawn
four times across the migration despite the rule" — because a rule with its reason survives the next
rewrite and a bare rule gets simplified away. Where it is an analyzer or a gate, it carries its tests
and its self-test like any other change here. Follow the conventions as if this were feature work,
because it is: `/check`, `/cleanup`, the in-session review round, the PR body's shape.

## 7. Record

Write `docs/retro/YYYY-MM-DD-<slug>.md` — a **descriptive slug**, following retro-0's
`2026-08-12-review-mining-round.md`, not a `retro-N` filename; the round's number lives in the note's
title and in the marker. It carries: window, method and counts on one line under the title; what was
read; the clusters with counts and line pointers; what recurred; what landed, one line each with its
rung and file; what was consciously left and why; and the questions the next round should be able to
answer. Terse — the journal holds the incidents, the note holds the structure and the decisions.

Carry retro-0's open question forward until a round can answer it: **which of `D1`–`D10` actually
fire**, from the lines that name them.

## 8. The metrics row

Append **one row** to [`docs/process-metrics.md`](../../docs/process-metrics.md), filled from durable
artifacts only — git, `gh`, and the journal — per that file's own counting rules. Do **not** invent a
column: the table's schema is a standing instruction to the journal, and a column with no defined
input is a wish. If the round wants a number the table cannot express, say so in the note and name
what would have to start being recorded.

`n/a` means the input did not exist, never zero. Retro-0's row is `n/a` across most columns for that
reason, and the first row after it sets the baseline the ratio columns are read against.

## 9. Rotate

Repair any journal damage § 2 found first. Then move the window's entries **verbatim** — including
the blank lines between them — into `docs/retro/journal-<from>-to-<to>.md`, where `<from>` and
`<to>` are the window's first and last entry dates. That file opens with one heading naming the
window, the note it belongs to, **and the line-number offset** (see below), then the entries unedited.

Replace the marker in the live journal with the next round's, keeping it an **HTML comment**:

```
<!-- retro-N marker: everything below this line is unread by a retro. Retro-N: docs/retro/<note>.md · window archived in docs/retro/journal-<from>-to-<to>.md. Move the marker when a round reads it. -->
```

It must stay a comment. `scripts/journal-lint.ps1` skips HTML comments and fails every other
non-entry line under `## Entries`, so a marker written as a bold line reddens the gate on the retro's
own PR.

**Rotation makes line-number citations into the journal stale, and silently.** Archived change docs
cite entries as `docs/process-journal.md:154` and as ``journal `:172` `` — thirteen such citations
existed on 2026-09-10, in three archived pass docs and the in-flight closeout doc, and the phrasing
varies, so this is a grep the round *reads*, not a count it trusts:

```bash
grep -rnoE "(process-journal\.md\`?:[0-9]+|journal[^.]{0,20}\`:[0-9]+(/\`:[0-9]+)?)" --include=*.md docs/
```

Read the hits; do not count them. The pattern over-matches: a sentence *about* citations is not a
citation, and two of them are in `docs/` today, in the closeout doc's checkpoint and in the journal
entry that records it. It can also only match the phrasings someone has already used — the thirteen
took four different shapes, one of them a pair of numbers in a single span — so a fourteenth written
another way is invisible to it. Widening the pattern is not the answer; reading the hits is.

After a rotation those numbers do not break — they re-point, at whatever entry now occupies that line
of the live file, which is worse. Three obligations, the first two cheap:

- The archive's heading states the offset, **measured from the file it just wrote, never composed**.
  This file opens with a heading, so the entries do not start at line 1 and the offset is not the
  window's own start: with the window's first entry at line `a` of `process-journal.md` and at line
  `f` of the archive, an entry cited at line L is at line `L − (a − f)`. Read `f` out of the finished
  archive — `awk '/^[0-9]{4}-[0-9]{2}-[0-9]{2} · /{print NR; exit}'` — and state the frame too: **`a`
  is only meaningful with the commit it was read at**, and a round that edits the journal header
  before rotating (this one landed a marker rewrite) has already moved `a` under itself. Retro-1
  wrote `L − 71` from the arithmetic this bullet used to give, against a true offset of `L − 66`, and
  built its first archive from a stale range for the same reason.
- The round reports the grep's hits to the operator with the landing set, and says how many point
  into the rotated window. **Rewriting them is the operator's call, not the command's** — retro-1 was
  told to rewrite, and the ruling turned on this bullet being right first.
- **Resolve a citation by its content, never by arithmetic** — including a citation this round has
  just renumbered. The scheme is unreliable even without a rotation: `T-015` audited thirteen on
  2026-09-10 and found twelve already wrong, by amounts no single offset explains and three of them
  by more than fifty, because the drift was written in one citation at a time as the header grew
  under them. It repaired all thirteen by content in #210, so a reader measuring staleness after that
  commit finds none — the defect is in the scheme, not in a residue you can still see. A citation is
  checked by reading the entry it lands on and asking whether that is the entry the sentence means.
  Where the answer is no, say so; a renumber applied to a pointer that was already wrong just makes
  the wrongness look deliberate.

Rotation keeps the live journal short so the append point stays near the end. Hand back:

```
Retro N recorded: docs/retro/<file>
Landed: <n> — <one line each>
Left:   <n> — <one line each>
Rotated: <count> entries → docs/retro/journal-<from>-to-<to>.md (<k> line citations point into it)
```
