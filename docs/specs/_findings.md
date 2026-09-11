# Finding ledger

Defects found and deliberately **not** fixed where they were found: too big for the round, or reaching
past the area that found them. The fourteen area passes filled it; any lane adds to it the same way
([`../spec-process.md`](../spec-process.md) § Routing). One line each, newest last. Triaged in bulk at
the retro ([`../retro/`](../retro/)) — an entry that gets scheduled becomes a Jira item and is struck
here with its key; an entry that is fixed is deleted with the PR that fixes it.

**"Struck" means removed, not left behind as a stub.** Both dispositions take the line out of this
file, and the difference is only where the record goes: a scheduled entry's **Jira key** is recorded
in the filing PR and in whatever table triaged it, a fixed one's record is the fix. Two consequences
worth stating, because `T-009` had to decide both from this paragraph alone. A stub keeps the entry
count from ever falling and keeps a triage round's rows from ever reading *resolved*, which is what
makes such a table readable at a glance. And a page that points here for an entry being removed must
be edited in the same PR to name the key instead — the pointer is the task's to fix, not the next
reader's.

**A round that triages this file in bulk builds its own checker.** `sdd-closeout` had one —
`scripts/ledger-buckets.ps1`, keying an entry to its disposition row on the entry's bold lead — and
it was deleted with that round when the doc archived, by its own instruction: a tool whose default
names a closed table checks something nobody can act on. Rebuild it against the open round's table
rather than reviving a default that resolves to the archive.

Not for: a small area-local defect (the round that finds it fixes it), a stated behavior that merely
surprises (the spec page states it), or a missing test (that is a `GAP` marker on the page).

**`ServiceRelationAnalyzer`'s by-name reach is the narrow one `AC-ANLZ-014.4` was widened out of.**
`RelationBearingInterfaces` reads only the property type's own declared base list, so a service-less
component that reaches a relation-bearing contract interface through a base class or an extending
interface draws no `DALE045` at all — the binder binds it, and the cloud edge the warning exists to
predict is missed in silence. Found while fixing VION-194, whose blocker was this same narrow reach
copied from here into `DALE043`. Not fixed there: a different diagnostic, at advisory severity, whose
own `AC-ANLZ-021.5` states the narrow reach as the rule, so widening it rewords that criterion too.
