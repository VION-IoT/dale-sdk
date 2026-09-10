# Harness conventions

The harness is everything written for the agent rather than for the product — for example
`CLAUDE.md`, convention docs, commands, skills, settings, and the journal header, but not the
journal's entries, which are data. Each file is read at the moment it applies — a command when the
user invokes it, a convention doc when its trigger in `CLAUDE.md` fires, the working agreement and
the journal header throughout — and says what the reader needs at that moment and nothing else.
Ported from mesh's harness conventions.

## A file describes itself, never its callers

Write a command for the moment it is invoked: what to do, what to report, where to stop. Who
invokes it, what ran before it, and which command the user runs next are the caller's business and
stay in the caller. A convention doc states its rules without saying what triggers it or which
command enforces them; the journal header states its grammar without describing the commands that
read it.

Every sentence is an instruction to the agent. A conclusion from a design discussion, advice to the
user on what to type, or an explanation of why something is absent is not one, and a decision worth
keeping goes to the journal as a decision line instead.

Why: every mention of another file is a sentence that has to change when that file changes, and
nothing reminds the writer — a command that restates when the gate invokes it and which command
commits its drift goes stale the moment the gate is rewritten, and the copy is what a review finding
then has to catch. The agent running a command does not need to know who else runs it.

The cleanup command ✓ — *If it applied changes, say so and stop — do not commit; the user reviews
the drift.* ✗ — *the user commits it through the commit command*, *the review command invokes it
before the review*.

A caller may name what it calls — the gate says it runs the review command — because that is the
caller's own behaviour.

## Every rule and every fact has one owner

A rule, and any fact about how the work is done, lives in exactly one file. Another file that needs
it points at the owner rather than restating it: a restated rule is a twin, twins drift, and every
twin is a sentence a reviewer can find inconsistent. Mirrored sentences that a fix updates in one
file and misses in another cost review rounds to reconcile, and a file that re-mentions what another
file already said bloats for no reader's benefit.

The owner is the file whose subject the rule governs, which is the file that applies it: the
commit-message format lives in the commit convention, not in the working agreement that says to
commit through it. The working agreement says which convention to use for what; what the convention
requires is in the convention.

Point only where the reader would not get there anyway — a convention doc that a trigger in
`CLAUDE.md` already opens for that work is never pointed at from a command — and only at something
the target says. Deleting a restatement does not call for a pointer in its place.

`CLAUDE.md` owns the triggers. A trigger says on its own when to open the file it points to — the
events, in a clause each — because the agent cannot open a file to find out whether to open it; the
file then owns the definitions and the details.

Just because two files state the same writing rule does not make it a duplicate: comments and
harness files both say "write it impersonally", but one rule is about comments and the other about
harness files, each in its own file, and either can change without the other. It is only a duplicate
when two sentences are about the same thing.

The same holds inside one file: define a term or a list once and use the term after that. A second
list of the same values a few lines down is a twin like any other, and the one that gets forgotten
when the first changes.

A file outside the harness owns what it configures, and a gate that already enforces something
leaves nothing to state: a compiler error, an analyzer warning, a formatter profile or a CI job
reaches the agent without a rule repeating it. Repeating it costs twice — the copy goes stale when
the gate changes, and it outlives the gate when the gate is removed. Write the rule only where the
agent can still get it wrong: the build stays warning-clean, because warnings do not fail the
build. A command is not told to build, to run the tests or to check the style when CI does that on
the pull request; it is told to read CI's verdict.

Checking a file against this rule means reading every sentence and asking which file owns it. A
search for phrases finds only the shape searched for — a sentence about the journal that names no
other file survives a search for command names just as easily as it survives a reader's first pass.

## State the principle, illustrate with examples

Where the rule is a principle, write the principle and mark the list that follows as examples
(*for example this file, convention docs, commands*). A closed list reads as exhaustive, invites a
finding for every kind it forgot, and grows a twin wherever it is repeated. Locations the reader
already knows — the commands folder, the conventions folder — are not repeated either. An example
shows a shape and does not reference a specific place that can move: naming the cleanup command as
an example is fine, a step number or a section name is a reference that goes stale.

## Walk through the change before handing it back

When a change alters what a step does — what it reads, where it writes, what it decides — do not
stop at checking that the new text answers the request. Walk through an actual run of the command
with the new text and see what happens at each step, including the odd cases: the session ends
before the user answers, the command is rerun although nothing changed, the record it wants to read
does not exist yet. A fix suggested by a reviewer is a suggestion, not a specification: copying a
reviewer's suggested wording into the file as written, without walking through it, is how a fix
becomes what the next review finds broken.

## Writing

Terse means fewer ideas, not fewer words: say what to do and why, once, and leave out hints that
only make sense to someone who already knows the rule. Write it impersonally — what is done, not
what we do. After writing a rule, read it again as if seeing it for the first time and having to act
on it; if it is not clear on that read, rewrite it.

A harness file is written to be copied to a sibling repo: it names no editor, machine, person or
tool the repo does not require, and avoids the repo's own name where it can — a title reads
"Logging conventions", not "Logging conventions — dale-sdk". Naming the user's own editor in a
script's help text or a command file is the same mistake in miniature, and is unusable the moment
the file is read on someone else's machine.

Use the words the repo already uses. A term coined for a file is one more thing a reader has to
learn and a reviewer can find inconsistent; when no existing word fits, say so rather than coin one.
