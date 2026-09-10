---
description: Adversarially review a change (uncommitted, branch, a second round, or PR) against this repo's conventions, the lead's known findings taxonomy, and a spec if one is given
argument-hint: [uncommitted|branch[:base]|since:<ref>|pr[:N]] [spec-path] [notes]
---

Another agent produced the change under review. You did **not** write it, and you are not here to
rewrite it — find where it diverges from what it was supposed to do, or from this repo's conventions,
and report that. Do not edit, commit, stage, or post anything to a PR.

## 1. Resolve what to review

Look at the first argument:

- omitted, or `uncommitted` → the uncommitted working tree of whatever branch you're on: `git diff HEAD`
- `branch` → everything this branch adds relative to its base (default `main`), **including** uncommitted
  work. Compute it from the fork point so the base branch's own later commits don't leak in: diff from
  `git merge-base <base> HEAD` to the working tree. Override the base with `branch:<base>`.
- `since:<ref>` → **a second round**: only what moved since an earlier round, `git diff <ref>` to the
  working tree. `<ref>` is the commit that round reviewed. See § 1a — a second round is a different job,
  not a smaller diff.
- `pr:<N>` → fetch that PR's diff read-only with `gh pr diff <N>` and review it here. Never comment on it.
- `pr` → **the PR this branch is on**: resolve it with `gh pr view --json number,url`, then proceed as
  `pr:<N>`. If the branch has no PR, say so and fall back to `branch`.

If the first argument is a path or prose rather than one of those keywords, treat the scope as `uncommitted`
— **except** when the prose plainly names a PR ("this pr", "the pr"), which means `pr`. On a fully-pushed
branch `uncommitted` is an empty diff, so following the literal mapping would report "no findings" on an
unreviewed change; say which scope you resolved before reporting anything.

**Ignore CI's snapshot commits.** `publish.yml` auto-commits regenerated
`docs/snapshots/publicapi-manifest.json` and `cli-help-snapshot.txt` onto the PR head. Those are machine
output — review what they *reveal* (an unintended public-surface change) but never the diff itself.

## 1a. A second round — what `since:` changes

The diff `since:<ref>` computes is not new. Where `<ref>` is a commit on the branch under review,
`git merge-base <ref> HEAD` **is** `<ref>`, so `branch:<ref>` already produces the same hunks. The
keyword exists because the second round is a different job, and three of its obligations do not
follow from the diff:

- **Read the first round's record before reading the diff** — the PR body's *Review round* section if
  the PR is open, otherwise the branch's `review` lines in
  [`docs/process-journal.md`](../../docs/process-journal.md) and the session's own report of it.
- **Do not re-raise what round 1 raised and the operator accepted.** An accepted finding is a decision,
  and re-reporting it costs the round its credibility. A finding round 1 raised and the change did
  **not** answer is worth raising again, said as such.
- **Check that round 1's findings were applied where they were said to be applied.** This is the pass
  on which § 5's blind spot closes: D2's stale-doc check and D10's correction check can only fire on a
  re-review, and this is the re-review.

**A merged base is not the change.** If `main` was merged in since `<ref>` — `git log --merges <ref>..HEAD`
is the check, and working agreement 3 makes it likely before a PR — the diff carries `main`'s commits
too. Say so, name the merge, and review only the hunks the branch authored.

## 2. Intent

If a spec, RFC or brief path was given (the next argument that looks like a path): read it, treat it as the
statement of intent, and focus only on the part relevant to this change. Treat any remaining text as
`notes` — decisions the user explicitly set (a naming choice, "one shared helper not four", a deliberate
breaking change). Check the change against both.

Where the intent is a `docs/specs/` page or a change doc with ids: for every test in the change that
cites an id (`[TestProperty("spec", …)]`, `[Trait("spec", …)]`, a scenario's `"specs"`), read the
criterion's **text** against the assertion. `spec-trace` checks that the id exists, not that it says
what the test proves; a citation for behavior the criterion does not state is a `[blocker]` (spec not
carried), and so is a criterion whose page text and change-doc delta line disagree.

If **no** path was given: skip intent-conformance entirely. Review only against conventions and general
quality, and say so at the top of your findings so the coverage gap is explicit — you are not in a
position to judge whether the change does the right thing, only whether it is done well.

## 3. Rubric

Read [`CLAUDE.md`](../../CLAUDE.md) and every convention doc its trigger table points to —
[`sdk-surface`](../../docs/sdk-surface-conventions.md), [`testing`](../../docs/testing-conventions.md),
[`devhost`](../../docs/devhost-conventions.md), [`introspection`](../../docs/specs/introspection.md),
[`releasing`](../../docs/releasing.md) — plus the nested `CLAUDE.md` for any area the diff touches
([`Vion.Dale.Cli`](../../Vion.Dale.Cli/CLAUDE.md),
[`Vion.Dale.DevHost.Web`](../../Vion.Dale.DevHost.Web/CLAUDE.md)). Those are your rules. Some content is
guidance for *writing* code, not criteria for reviewing it — enforce what constrains the code, not the
authoring process. Exception: the working agreement's verification obligation **is** reviewable (check D10).

The machine baseline is `dotnet build Vion.Dale.Sdk.sln`, `dotnet test Vion.Dale.Sdk.sln`, and
`pwsh scripts/cleanup-code.ps1 -Verify` — ignore anything they would catch.

## 4. Review

Read enough of the surrounding code to judge each change in context — never review a hunk blind. Report
findings ranked by severity:

- **[blocker]** — a spec requirement is unmet, or a correctness / contract / published-surface bug
- **[convention]** — violates a rule in `CLAUDE.md` or a convention doc; quote the rule
- **[judgment]** — you would have done it differently; list these last, one line each

For each finding give `file:line`, the concrete failure (not a vague smell), and the specific rule, spec
line, or taxonomy check (D-number below) it breaks. Where a `note` says a choice was deliberate, don't
flag it as a mistake — but if it still looks actively wrong, surface it as "noted as intended, but flag: …"
and let the user decide.

Prefer three real findings over fifteen speculative ones. Ignore anything on lines the change didn't touch.
If there are no material issues, say so plainly. Do not edit anything — hand the findings back.

**State the mechanism you verified at the call site** — the line that makes the failure happen,
quoted — or mark the finding `(inferred)`. **A finding is a hypothesis until the tree confirms it:** a
review reading a branch at one commit gets the shape right and the constants wrong. Premises refuted
at the call site have included an "empty schedule" that was the framework's 60 s periodic event, a
"CRLF file" that was LF, a setter called mutable that is write-once, and a criterion said to exist on
no page — each cost its finding the first paragraph, and each finding was still worth raising once
the premise was corrected. A premise about what the **build** decides — which projects pack, what an
assembly carries, what a property evaluates to — comes from `dotnet msbuild -getProperty` or the
generated `AssemblyInfo.cs`, never from a regex over the csprojs.

Hand the findings back and stop. What the session does with them — the round written into the PR body,
the operator's dispositions journalled — is § 7, and the rule that owns the dispositions is `CLAUDE.md`
working agreement 7.

## 5. The lead's taxonomy

These are the review findings the human lead actually makes in **this** repo, mined from every session
between 2026-06-12 and 2026-08-12 (evidence and verbatim quotes:
[`docs/retro/2026-08-12-review-mining-round.md`](../../docs/retro/2026-08-12-review-mining-round.md)).
Run each as a named adversarial check; cite the D-number in findings.

- **D1 — Surface minimalism.** Does every new attribute, option, public member, generator, wrapper and
  scenario-step kind trace to a consumer that exists now? A mechanism added "for later", a shorthand that
  duplicates a general path, or an explicit knob where a convention already decides (declaration order,
  a default) is a finding. Precedent: a source generator dropped as *"overkill for one line of
  boilerplate"*; parameterised scenarios dropped — *"can be done in user land via c#, no magic from SDK"*;
  `Order = X` dropped — *"declaration order is enough"*.

- **D2 — XML docs terse and reader-facing.** No RFC numbers, no analyzer IDs, no customer names, no design
  history, no `<remarks>` restating the summary. Examples are generic. *"keep the ServiceRelationAttribute
  xmldoc lighter, no rfc mentioning, no analyzer number, no customer specific examples"* ·
  *"technically correct but i find the remarks too verbose"*. And: does each claim match the code that
  implements it, rather than the neighbouring docstring? (Worked example, found in the tree on
  2026-08-12: `ScenarioStep`'s summary in `Vion.Dale.DevHost/Scenarios/ScenarioFile.cs` advertised step
  kinds the generic step kinds had replaced, while the validator three hundred lines below listed the real set.)
  Remember what survives rendering: a member's `<remarks>` is dropped entirely from the docs site
  ([`sdk-surface-conventions.md`](../../docs/sdk-surface-conventions.md) § 2).

- **D3 — Delete, don't deprecate.** When this change generalises or replaces a mechanism, is the old one
  gone and its callers migrated in the same PR? Is a nullable/optional field nullable because a live
  consumer needs it, or only out of habit? *"breaking is no problem, the devhost is the only user"* ·
  *"if the sugar is not that much of a win i'd prefer removing it"* · *"let's do it right as long as we can"*.

- **D4 — An author-facing rule ships with an analyzer.** A constraint a library author can violate in C#
  needs a DALE diagnostic, not prose or a runtime throw alone. *"The analyzer should be there to surface
  bad usage"* · *"it must be clear to users what formats are valid for which type, and the SDK analyzer
  validating it, right?"*. Two specific defects: a supported value type exempted in only one of
  `DALE003`/`DALE016`/`DALE008`; and analyzer tests that exercise rules **only in isolation** when the
  combination is what rejects valid code.

- **D5 — The analyzer is proven to fire in a real build.** Generated contract interfaces are invisible to
  analyzers in this repo's Metalama-hosted pipeline, so a symbol-only check passes its unit tests and
  no-ops for every consumer. Does the analyzer resolve contract interfaces **by name as well as by
  symbol**, and is there a test whose contract interface is genuinely unresolved (expecting `CS0246`
  alongside the Dale diagnostic)? Treat a symbol-only contract-interface check as a blocker, not a nit.

- **D6 — Validated against a real consumer shape.** Does the change work for the shapes consumers
  actually have — a type in a **different assembly**, a component service on a property, two components
  of one type on one block, a block with no service interface? An in-repo fixture that has only the
  simple shape is why gaps ship. *"when IChangeThreshold implementation is in a different assembly (say
  common.xy) than the logic block, is it safe? at compile time and runtime?"* · *"does it work for the
  multi-service example"* · *"place yourself in the shoes of the implementer … is it usable? is there
  enough guidance, documentation, examples"*.

- **D7 — Introspection verified from the packed artifact.** The parser runs on `dotnet pack`, not
  `publish`, and `tools/publish/*.json` is gitignored build output. Does a change to emitted metadata
  cite the packed JSON, plus a golden assertion in-repo? *"check the introspection result … by looking at
  the json of the packed energy examples nugpk"*. A claim about the wire shape read off the working tree
  is unverified.

- **D8 — Names are explicit and platform-aligned.** No abbreviated platform nouns (`serviceProvider`, not
  `provider`; `logicBlock`, not `block`); cross-repo vocabulary over local invention; a type name that
  states its scope. *"on naming: use serviceProvider, not provider where possible, be explicit"* — and a
  whole review round whose only content was `* "block" -> logicBlock`. Then the consequence check: does
  any rename touch an **introspection identifier** — service, member, contract, interface, enum member,
  enum/struct type name, PackageId? That
  orphans authored translations ([`specs/introspection.md`](../../docs/specs/introspection.md)) and needs
  saying out loud.

- **D9 — Silence is a defect.** An empty `else if` that drops a declaration; a "no-throw" test standing in
  for a round-trip; a serialized `[]` where content was expected; a mode-incompatible step that
  mis-behaves instead of being refused. *"contractMappings in the saved json should not be [], it should
  contain the mappings"* · *"that's not great, a real trap"*. Where a surface fails **open** deliberately,
  is that stated and does the catch log?

- **D10 — Claims verified, mechanism explained.** Every assertion about current behaviour — a call-site
  count, "X already does Y", "this can't be done", a claim about another repo — cites `file:line` or the
  command that produced it. Same check on the verification story: what was actually run, was `gh pr checks`
  read after the last push, and — for anything touching the DevHost — was it demonstrated live, not just
  asserted (`devhost-conventions.md` § 1)? A fix explains *why it slipped*, not just what changed:
  *"explain the bug, and why it could slip through, was the rfc silent about it?"* · *"why that change?
  please explain"*.

**Blind spot to state, not to hide:** D2's stale-doc check and D10's correction check can only fire on a
re-review or where the branch already answers feedback. On a first pass, § 4 is what covers them.

## 6. Standing checks from the spec corpus

Four more named checks, paid for by the fourteen area passes that seeded `docs/specs/`
([`spec-process.md`](../../docs/spec-process.md) § Lanes). Run each the way you run a D-number, and
cite the P-number in the finding.

- **P1 — Every count is pasted with its command.** A number in a change doc, a REPORT or a PR body
  that was *re-read* rather than *recounted* is stale by default; a composed one is the same defect.
  Recount the numbers the change turns on, one grep each — that is how 106 criteria against 105 on
  the page, and 466 cited tests against 452, were both caught. A count carried from an earlier run
  does not announce itself as stale; it announces itself as an interesting difference, and one round
  went into explaining a three-test gap that was three tests added in the last commit.

- **P2 — The reverse question, per anchor.** Counting rows against criteria asks whether every row
  landed; it never asks *which observable behaviours have neither a row nor a criterion*. Ask that of
  each anchor instance — each attribute, descriptor range, schema field, manifest entry the change
  touches. Two behaviours reached a page only when someone did: a lifecycle entry point, and a silent
  success. Ask it of the change doc's own reviewer's questions too — a question that describes an
  observable and recommends its disposition is an observable someone noticed and nobody tabled.

- **P3 — The fix's siblings.** A fix closes one path and opens its neighbour: the restart beside the
  double start, the second map beside the first, the other output mode of a path the change just
  added. Did the change sweep them? Three checks: a fix that names a defect *shape* sweeps the shape
  and not the one symbol it noticed; a guard added to a `switch` reaches **every** arm; a fix that
  makes previously inert inputs live re-checks every validator over those inputs.

- **P4 — Evidence is for the shape it ran.** A probe proves the fixture shape it ran against and the
  surface it read — `get;` is not `get; init;`, and the definition view is not the live view. A claim
  about a reader in another repository that is on this machine is read there, not recalled: one
  second opinion found five such claims wrong, three of them changing a decision. A demonstrated
  observation (a DevHost Tier 2 row) is a **paste**, made through the UI's own controls; a row whose
  paste shows a scripted DOM write is not an observation
  ([`devhost-conventions.md`](../../docs/devhost-conventions.md) § 1).

## 7. The record the round leaves

*(§ 1a, § 5 and § 6 are numbered as they are so that the D-numbers stay in § 5 and the P-numbers in
§ 6 — four files cite them by section.)*

You are the reviewer and you write nothing but findings. This section is for the **session that ran
you**: where the round goes once you hand them back. The review is in-session and its record is the PR
body — there is no draft PR and no GitHub review comment, on this round or any later one.

The PR body's *Review round* section ([`.github/pull_request_template.md`](../../.github/pull_request_template.md))
carries one block per round, in order:

```text
Round N — <scope argument> at <hash>, fresh-context subagent.

[blocker] <file:line> — <the finding in one line>.
  Fixed in <commit or file:line>: <what changed>.
[convention] <file:line> — <the finding in one line>.
  Accepted: <why it stands>.
[judgment] <file:line> — <the finding in one line>.
  Fixed / Accepted: <…>.
```

Four rules about that block:

- **Every finding appears, fixed or accepted, and an accepted one carries its reason.** A finding
  dropped silently between the review and the PR body is the one thing this record exists to prevent.
- **Findings keep § 4's severity order.** The reader wants the blockers first, and the ranking is the
  reviewer's judgment, not the session's to re-sort.
- **No findings is a result, not an empty section.** Write `Round N — <scope> at <hash>: no findings.`
  A missing section reads as a round that was skipped.
- **A second round is its own block**, run as `since:<the first round's hash>` (§ 1a). The body carries
  both blocks; nothing is rewritten in place, because the first round's findings and their dispositions
  are what the second round was scoped against.

**The operator's dispositions are one `review` journal line** in the commit that applies them —
`CLAUDE.md` working agreement 7 owns that rule and [`docs/process-journal.md`](../../docs/process-journal.md)
owns the line's shape, including the `(escape)` marker for work that had already passed a round. This
section does not restate either; it points at them because a round that produced findings and journalled
nothing has left no durable record of what it cost.
