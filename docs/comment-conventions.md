# Comment conventions

How inline comments are written in this repo. Read before writing or reworking a `//` or `/* */`
comment. **XML documentation is owned by [`sdk-surface-conventions.md`](sdk-surface-conventions.md) § 2**
— this doc is about comments inside method bodies and above private members. Adapted from mesh's
comment conventions; the journal's most repeated defect class is prose asserting something false
about the code, and every rule here exists to make a comment cheap to verify.

## Comment the why, never the what

The code already says what it does. A comment earns its place only where a reader who understands
the code would still ask *why is it like this* — a deliberate trade-off, a constraint imposed from
outside, a failure mode that motivated the shape. Everything else is noise that goes stale.

```csharp
// Deadband is applied per stream, not per member: a property and a measuring point on the
// same C# property publish independently — keyed by member name alone, the two streams
// collide and one silently suppresses the other.
```

A comment like that survives a rewrite of the code below it, because it explains a decision rather
than a line.

## A comment is a claim — it carries the same burden as code

This repo's reviews keep finding **load-bearing comments that are false**: an ordering justified by
an event no handler observes, an invariant asserted two lines above a call that violates it, advice
still given for a reason that stopped being true. Before writing a comment that states a mechanism,
**verify the mechanism the way you would verify code** — read the implementation it describes, not
the neighbouring prose. A comment describing a guard is not evidence the guard exists. When a change
falsifies a comment elsewhere, fixing that comment is part of the change.

## Build the reasoning up in order

A comment that needs more than one sentence is an explanation, and an explanation has an order:
the setup a reader needs, then the problem it creates, then what the code does about it. Each
sentence readable knowing only the ones before it. State cause before consequence.

## Terse means fewer ideas, not fewer words per idea

Cut whole points that don't need making. Do not cut the connective tissue inside a point that does —
a compressed clause standing in for a sentence is shorter to write and much slower to read. If a
phrase can only be understood by someone who already knows the answer, it is not terse, it is a
reminder — and the reader it was written for does not need it.

## Explain the mechanism, not the domain

The reader is looking at this code and needs to follow *it*. Reach for the surrounding system
(the runtime, the cloud, a consumer) only when the mechanism genuinely cannot be understood
without it.

## Name the concrete failure

"Fails loudly", "handles the error", "would be unsafe" say nothing a reader can check against the
code. Say what actually happens and what it prevents: *a torn read here hands `SetUtcNow` a past
instant, which throws and consumes the already-removed action* — not *this avoids a race*.

## No history, no tickets, no numbers that rot

The same rule XML docs follow (`sdk-surface-conventions.md` § 2) applies inline: no issue keys, no
RFC/change-doc references, no "now"/"new"/before-after framing, no version numbers — git and the
archived change docs own the history, and a procedure comment describes **today**. The one
exception: a consumer-visible workaround may cite the `VION-nn` it waits on, so it can be retired
when the answer ships (the convention its consumers already follow).

## Write it impersonally

State what the code does, declaratively. No `we`, `our`, or `you`.

## Form

- More than one line goes in a `/* */` block (or a `//` run) directly above the member, indented
  with it; blank lines separate paragraphs.
- A single line goes at the site it explains.
- Full sentences with terminal punctuation; a colon-introduced fragment is acceptable for a
  one-line comment on a constant.

## Read it back before handing it over

Re-read the finished comment for: a comma before `and`/`but` joining independent clauses; parallel
forms after `rather than` / `instead of`; comparisons that name the wrong noun; sentences over
about thirty words (split them) — and, above all, whether every claim in it is one the code below
still makes true.
